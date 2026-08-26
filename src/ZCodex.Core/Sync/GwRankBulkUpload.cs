using System.Diagnostics;
using ZCodex.Core.Importers;
using ZCodex.Core.Models;
using ZCodex.Core.Serialization;

namespace ZCodex.Core.Sync;

/// <summary>Ce qu'un fichier va devenir dans l'envoi de masse. Établi SANS réseau.</summary>
public enum GwRankBulkVerdict
{
    /// <summary>Jamais déposé depuis ce poste : sera créé.</summary>
    New,
    /// <summary>Déjà déposé, mais le contenu a changé depuis : sera mis à jour.</summary>
    Changed,
    /// <summary>Identique au dernier dépôt : ne partira pas du tout, pas même en requête.</summary>
    Unchanged,
    /// <summary>⚠ Un AUTRE fichier a déjà déposé cette identité. Envoyer écraserait son build
    /// côté serveur : dans un lot, on ne touche pas — on le signale et on passe.</summary>
    IdentityCollision,
    /// <summary>Fichier illisible, ou d'un format que le catalogue courant ne sait pas résoudre.</summary>
    Unreadable,
    /// <summary>Lisible mais ce n'est pas un build : gabarit d'équipement, fichier vide.</summary>
    NotABuild,
    /// <summary>⚠ Le build contient des compétences absentes du catalogue local. Les déposer les
    /// écrirait en « emplacement vide » côté serveur : perte de données silencieuse.</summary>
    UnknownSkills,
}

/// <summary>Un fichier de la bibliothèque, son verdict, et ce qu'il est devenu après l'envoi.</summary>
public sealed class GwRankBulkItem
{
    public required string FilePath { get; init; }

    /// <summary>Nom sous lequel le build sera déposé.</summary>
    public required string Name { get; init; }

    public GwRankBulkVerdict Verdict { get; set; }

    /// <summary>Le fichier qui détient déjà cette identité (verdict <see cref="GwRankBulkVerdict.IdentityCollision"/>).</summary>
    public string? ConflictingPath { get; set; }

    /// <summary>Le modèle prêt à partir. Null quand le fichier n'est pas exploitable.</summary>
    public TeamBuild? Model { get; set; }

    /// <summary>Issue de l'envoi, une fois le lot passé. Null tant qu'il n'a pas été tenté.</summary>
    public GwRankStatus? Result { get; set; }

    public string? Message { get; set; }

    public bool WillBeSent => Verdict is GwRankBulkVerdict.New or GwRankBulkVerdict.Changed;
}

/// <summary>Bilan d'un lot.</summary>
public sealed record GwRankBulkReport(
    int Created, int Updated, int Unchanged,
    int Collisions, int Unreadable, int UnknownSkills, int Conflicts, int Failed,
    IReadOnlyList<GwRankBulkItem> Items,
    GwRankStatus? StoppedBy = null, int Sent = 0, int Planned = 0)
{
    /// <summary>Le lot s'est arrêté en route parce que le serveur ne répondait plus. Ce n'est pas
    /// un échec par build : les suivants n'ont même pas été tentés.</summary>
    public bool WasInterrupted => StoppedBy is not null;

    /// <summary>Combien n'ont pas été tentés du tout après l'arrêt.</summary>
    public int NotAttempted => Math.Max(0, Planned - Sent);

    /// <summary>Combien partiraient si on lançait le lot maintenant.</summary>
    public int ToSend => Items.Count(i => i.WillBeSent);

    /// <summary>Les fichiers qui réclament une action de l'utilisateur : rien ne sera fait pour
    /// eux tant qu'il n'aura pas tranché.</summary>
    public IEnumerable<GwRankBulkItem> NeedsAttention => Items.Where(i =>
        i.Verdict is GwRankBulkVerdict.IdentityCollision
                  or GwRankBulkVerdict.Unreadable
                  or GwRankBulkVerdict.UnknownSkills
        || (i.Result is { } r && r != GwRankStatus.Ok));
}

/// <summary>
/// Envoi de TOUTE une bibliothèque sur GWRank (lot 2 du périmètre du 22/08).
///
/// Deux temps volontairement séparés : <see cref="Analyze"/> établit hors ligne ce qui partirait,
/// <see cref="UploadAsync"/> exécute. L'utilisateur voit donc le lot AVANT qu'une seule requête
/// ne parte — sur des centaines de fichiers, découvrir après coup ce qu'on a envoyé serait
/// intenable.
///
/// Trois règles tiennent tout le reste :
///   • un build inchangé ne déclenche AUCUNE requête (l'empreinte locale suffit à le savoir) ;
///   • une identité déjà prise par un autre fichier n'est jamais déposée — ce serait effacer le
///     build du jumeau côté serveur, et un lot ne doit pas pouvoir faire ça en masse ;
///   • un build modifié ailleurs (412) est SAUTÉ, jamais écrasé : personne ne peut arbitrer
///     des dizaines de conflits à la volée, et l'écrasement n'est offert qu'à l'unité.
/// </summary>
public static class GwRankBulkUpload
{
    /// <summary>Les formats qu'un dépôt de masse accepte de lire.</summary>
    public static readonly string[] Extensions =
        [".zcx", ".pn3", PwndImporter.Extension, SkillTemplateImporter.Extension];

    /// <summary>
    /// Les fichiers d'un dossier qu'un dépôt de masse sait lire, sous-dossiers compris.
    ///
    /// Public parce que l'interface en a besoin AVANT toute analyse : elle en dresse la liste pour
    /// que l'utilisateur puisse décocher au fichier près.
    /// </summary>
    public static IEnumerable<string> EnumerateBuilds(string folder) => Enumerate(folder);

    /// <summary>
    /// Ce que le lot ferait, sans toucher au réseau.
    ///
    /// <paramref name="files"/> est une liste EXPLICITE, jamais déduite d'une racine : la
    /// bibliothèque de référence contient 3 260 gabarits `.txt` de personnage venus de packs
    /// téléchargés, qui n'ont rien à faire sur GWRank. C'est l'utilisateur qui désigne, dossier
    /// par dossier puis fichier par fichier.
    /// </summary>
    /// <param name="gameMode">Mode de jeu à inscrire dans les documents. ⚠ Doit être CELUI QUE
    /// L'INTERFACE AFFICHE, comme le fait l'envoi à l'unité : le mode n'est pas une propriété du
    /// fichier mais du contexte d'envoi. Choisir autre chose ici ferait faire la navette à chaque
    /// build entre le bouton et le lot, chacun défaisant l'estampille de l'autre — mesuré sur un
    /// `.txt` réel, seul champ qui divergeait.</param>
    public static List<GwRankBulkItem> Analyze(IEnumerable<string> files,
                                               IReadOnlyDictionary<int, Skill> skillsById,
                                               GwRankSyncIndex index,
                                               GameMode gameMode,
                                               CancellationToken ct = default)
    {
        var items = new List<GwRankBulkItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            // Deux dossiers cochés dont l'un contient l'autre ne doivent pas déposer deux fois.
            if (!seen.Add(Normalize(path))) continue;
            items.Add(Examine(path, skillsById, index, gameMode));
        }

        FlagTwinsWithinBatch(items);
        return items;
    }

    /// <summary>
    /// ⚠ Deux fichiers jumeaux JAMAIS déposés passent tous deux pour neufs : l'index ne connaît
    /// encore ni l'un ni l'autre, donc <see cref="GwRankSyncIndex.Check"/> ne voit aucune
    /// collision. Le second écraserait alors le premier À L'INTÉRIEUR DU MÊME LOT, sans que rien
    /// ne le signale — précisément ce que le garde-fou de collision existe pour empêcher.
    ///
    /// On tranche par le chemin, pour que deux analyses successives désignent toujours le même
    /// gagnant : l'utilisateur ne doit pas voir le sort de ses fichiers changer d'un passage à
    /// l'autre.
    /// </summary>
    private static void FlagTwinsWithinBatch(List<GwRankBulkItem> items)
    {
        foreach (var twins in items.Where(i => i.WillBeSent && i.Model is not null)
                                   .GroupBy(i => i.Model!.Id)
                                   .Where(g => g.Count() > 1))
        {
            var ordered = twins.OrderBy(i => i.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var loser in ordered.Skip(1))
            {
                loser.Verdict = GwRankBulkVerdict.IdentityCollision;
                loser.ConflictingPath = ordered[0].FilePath;
            }
        }
    }

    /// <summary>Dépose ce que <see cref="Analyze"/> a retenu. L'index est enregistré à la fin,
    /// une seule fois, et aussi en cas d'abandon : ce qui est déjà parti doit être retenu, sinon
    /// la reprise redéposerait tout.</summary>
    public static async Task<GwRankBulkReport> UploadAsync(IReadOnlyList<GwRankBulkItem> items,
                                                           GwRankClient client,
                                                           GwRankSyncIndex index,
                                                           IProgress<GwRankBulkItem>? progress = null,
                                                           CancellationToken ct = default)
    {
        int created = 0, updated = 0, conflicts = 0, failed = 0, sent = 0;
        GwRankStatus? stoppedBy = null;
        var todo = items.Where(i => i.WillBeSent && i.Model is not null).ToList();

        try
        {
            foreach (var item in todo)
            {
                ct.ThrowIfCancellationRequested();
                var model = item.Model!;

                // Un build déjà partagé le RESTE : un envoi de masse ne doit jamais changer la
                // visibilité de quoi que ce soit. Les nouveaux, eux, partent en privé — partager
                // est un geste délibéré, build par build.
                bool isPublic = string.Equals(index.VisibilityOf(model.Id), "public",
                                              StringComparison.OrdinalIgnoreCase);

                var res = await client.UploadAsync(model, isPublic, index.ServerHashOf(model.Id), ct);
                item.Result = res.Status;
                item.Message = res.Message;

                if (res.IsOk)
                {
                    index.Record(model, item.FilePath, res.Value!.Id,
                                 isPublic ? "public" : "private", res.Value.DocumentHash);
                    if (res.Value.Created) created++; else updated++;
                }
                else if (res.Status == GwRankStatus.Conflict) conflicts++;
                else failed++;

                sent++;
                progress?.Report(item);

                // ⚠ Coupe-circuit. Le client a DÉJÀ réessayé ce build deux fois : si la panne
                // tient encore, elle ne vient pas de ce fichier-là et les suivants échoueront
                // pareil. Insister coûterait 30 s de fenêtre figée par build — mesuré à 26
                // minutes sur une bibliothèque de 51 builds face à un serveur muet.
                // Ce qui est déjà déposé reste acquis : relancer ne reprendra que le reste.
                if (res.Status.IsTransient()) { stoppedBy = res.Status; break; }
            }
        }
        finally { index.Save(); }

        return Summarize(items, created, updated, conflicts, failed) with
        {
            StoppedBy = stoppedBy,
            Sent = sent,
            Planned = todo.Count,
        };
    }

    /// <summary>Bilan d'une analyse seule, avant tout envoi.</summary>
    public static GwRankBulkReport Summarize(IReadOnlyList<GwRankBulkItem> items)
        => Summarize(items, 0, 0, 0, 0);

    // ── Interne ───────────────────────────────────────────────────────────────

    private static GwRankBulkReport Summarize(IReadOnlyList<GwRankBulkItem> items,
                                              int created, int updated, int conflicts, int failed)
        => new(created, updated,
               items.Count(i => i.Verdict == GwRankBulkVerdict.Unchanged),
               items.Count(i => i.Verdict == GwRankBulkVerdict.IdentityCollision),
               items.Count(i => i.Verdict is GwRankBulkVerdict.Unreadable or GwRankBulkVerdict.NotABuild),
               items.Count(i => i.Verdict == GwRankBulkVerdict.UnknownSkills),
               conflicts, failed, items);

    private static IEnumerable<string> Enumerate(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) yield break;

        IEnumerable<string> all;
        try { all = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GwRank] dossier illisible ({folder}) — {ex.Message}");
            yield break;
        }

        foreach (var path in all)
        {
            // ⚠ Le miroir des builds d'AUTRUI n'est pas la bibliothèque de l'utilisateur : le
            // déposer reviendrait à republier le travail des autres sous son propre compte.
            if (GwRankBrowserCache.IsInCache(path)) continue;

            var ext = Path.GetExtension(path);
            if (Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) yield return path;
        }
    }

    private static GwRankBulkItem Examine(string path, IReadOnlyDictionary<int, Skill> skillsById,
                                          GwRankSyncIndex index, GameMode gameMode)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        TeamBuild? model;
        var unresolved = new List<int>();
        try { model = LoadForUpload(path, skillsById, index, unresolved); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GwRank] lecture impossible ({path}) — {ex.Message}");
            return new GwRankBulkItem { FilePath = path, Name = fileName,
                                        Verdict = GwRankBulkVerdict.Unreadable };
        }

        if (model is null)
            return new GwRankBulkItem { FilePath = path, Name = fileName,
                                        Verdict = GwRankBulkVerdict.Unreadable };

        // Un `.txt` peut être un gabarit d'ÉQUIPEMENT (codes « P ») : lisible, mais ce n'est pas
        // un build et il n'a rien à faire sur GWRank.
        if (model.Characters.Count == 0)
            return new GwRankBulkItem { FilePath = path, Name = fileName,
                                        Verdict = GwRankBulkVerdict.NotABuild };

        // ⚠ Un id de compétence absent du catalogue est réécrit en 0 à la sérialisation
        // (cf. TeamBuildSerializer) : déposer ce build remplacerait des emplacements pleins par
        // du vide, côté serveur, sans que rien ne le dise. Un lot ne peut pas poser la question
        // fichier par fichier — il s'abstient et le signale.
        if (unresolved.Count > 0)
            return new GwRankBulkItem { FilePath = path, Name = fileName,
                                        Verdict = GwRankBulkVerdict.UnknownSkills,
                                        Message = string.Join(", ", unresolved.Distinct().Take(8)) };

        // Le mode de jeu est estampillé par le CONTEXTE d'envoi, pas lu dans le fichier — c'est
        // ce que fait l'envoi à l'unité, et les deux chemins doivent produire le même document.
        model.GameMode = gameMode;

        // Le nom choisi lors d'un dépôt précédent l'emporte — sinon le lot écraserait à chaque
        // passage le nom que l'utilisateur avait pris la peine de saisir. À défaut, le nom du
        // FICHIER fait foi : renommer depuis l'explorateur Windows laisse en arrière le nom
        // inscrit dans le document (mesuré : 13 fichiers sur 263).
        model.Name = index.NameOf(model.Id) ?? fileName;

        // ⚠ Même piège que le mode de jeu : les étiquettes GWRank ne sont pas dans le fichier
        // mais dans l'index. Sans cette reprise, un passage du lot les EFFACERAIT sur le serveur
        // (il remplace la liste par celle qu'il reçoit), et le build ferait ensuite la navette
        // entre le bouton et le lot, chacun défaisant le travail de l'autre.
        // Remplacer et non compléter : un `.zcx` venu d'ailleurs peut porter des étiquettes
        // libres, que la liste fermée du serveur refuse désormais en 422.
        model.Tags = [.. index.TagsOf(model.Id)];

        var verdict = index.Check(model, path);
        return new GwRankBulkItem
        {
            FilePath        = path,
            Name            = model.Name,
            Model           = model,
            ConflictingPath = verdict.ConflictingPath,
            Verdict = verdict.Check switch
            {
                GwRankUploadCheck.Unchanged         => GwRankBulkVerdict.Unchanged,
                GwRankUploadCheck.IdentityCollision => GwRankBulkVerdict.IdentityCollision,
                // « Ready » couvre deux cas très différents pour l'utilisateur : jamais déposé,
                // ou déposé puis modifié. C'est la PRÉSENCE dans l'index qui les sépare — pas
                // l'empreinte serveur, absente des dépôts antérieurs à ce mécanisme.
                _                                   => index.Knows(model.Id)
                                                       ? GwRankBulkVerdict.Changed
                                                       : GwRankBulkVerdict.New,
            },
        };
    }

    /// <summary>
    /// Charge un fichier sous la forme exacte qui sera déposée.
    ///
    /// ⚠ Le point délicat est l'IDENTITÉ des formats non natifs. Un `.txt` ou un `.pwnd` ne porte
    /// aucun identifiant : lui en donner un neuf à chaque lecture ferait changer l'empreinte du
    /// document à chaque passage du lot, et le serveur verrait bouger des builds que personne
    /// n'a touchés. On la dérive donc du CHEMIN, de façon reproductible, exactement comme le fait
    /// l'envoi à l'unité (cf. <c>BuildTabToModel</c>).
    /// </summary>
    private static TeamBuild? LoadForUpload(string path, IReadOnlyDictionary<int, Skill> skillsById,
                                            GwRankSyncIndex index, List<int> unresolved)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        // Format natif : le document porte sa propre identité, il n'y a rien à dériver.
        if (TeamBuildSerializer.IsNativeExtension(ext))
        {
            // `out` réaffecte la variable locale : sans cette recopie, l'appelant ne verrait
            // jamais les compétences non résolues et le garde-fou serait mort-né.
            var native = TeamBuildSerializer.Load(path, skillsById, out var missing);
            unresolved.AddRange(missing);
            return native;
        }

        var model = ext == PwndImporter.Extension
            ? PwndImporter.Import(path, skillsById)
            : SkillTemplateImporter.Import(path, skillsById);
        if (model is null) return null;

        // ⚠ Le cas à UN personnage doit produire EXACTEMENT la même identité que l'envoi à
        // l'unité (`BuildTabToModel`), qui dérive celle du personnage du seul chemin. Suffixer
        // ici donnerait un document différent pour le même fichier : un `.txt` déjà déposé
        // depuis la barre d'outils repartirait comme « modifié » au premier lot, et inversement.
        // Un `.pwnd` porte plusieurs personnages, qui exigent chacun un identifiant distinct.
        if (model.Characters.Count == 1)
            model.Characters[0].Id = GwRankSyncIndex.DeterministicId(path);
        else
            for (int i = 0; i < model.Characters.Count; i++)
                model.Characters[i].Id = GwRankSyncIndex.DeterministicId($"{Normalize(path)}#char{i}");

        model.Id = index.FindIdByPath(path) ?? GwRankSyncIndex.DeterministicId(path);

        // Même exigence de stabilité que l'identité : un `UtcNow` ferait changer l'empreinte à
        // chaque analyse, et le « déjà à jour » ne tomberait jamais.
        try { if (File.Exists(path)) model.CreatedAt = File.GetLastWriteTimeUtc(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return model;
    }

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd('\\', '/'); }
        catch { return path; }
    }
}
