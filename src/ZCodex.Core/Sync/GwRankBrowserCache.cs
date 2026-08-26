using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ZCodex.Core.Sync;

/// <summary>Bilan d'un rafraîchissement, affiché à l'utilisateur.</summary>
public sealed record GwRankSyncReport(
    GwRankStatus Status,
    int MyBuilds, int MyTeamBuilds,
    int PublicBuilds, int PublicTeamBuilds,
    int Skipped,
    string? Message,
    int Downloaded = 0)
{
    public bool IsOk => Status == GwRankStatus.Ok;
    public int Total => MyBuilds + MyTeamBuilds + PublicBuilds + PublicTeamBuilds;
}

/// <summary>
/// Ce que le miroir retient d'une synchronisation à l'autre, dans
/// <c>%AppData%\Z-Codex\gwrank_browser.json</c>.
///
/// Sert à ne PAS retélécharger un document déjà sur disque : l'empreinte du serveur dit si le
/// fichier gardé est encore le bon. Perdre ce fichier ne coûte qu'un rapatriement complet, jamais
/// une donnée — les documents, eux, sont dans le miroir.
/// </summary>
public sealed class GwRankBrowserState
{
    public sealed class Entry
    {
        /// <summary>Empreinte serveur du document tel qu'il a été écrit dans le miroir.</summary>
        public string DocumentHash { get; set; } = string.Empty;

        /// <summary>Chemin du fichier écrit, RELATIF à la racine du miroir. Le nom réel peut
        /// différer du nom du build (caractères interdits, homonymes suffixés) : le recalculer
        /// serait un pari, le retenir est exact.</summary>
        public string RelativePath { get; set; } = string.Empty;
    }

    private static string FilePath => AppPaths.In("gwrank_browser.json");

    /// <summary>Clé = <c>sourceId</c> du build.</summary>
    public Dictionary<string, Entry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Dernière synchronisation RÉUSSIE. Null tant qu'il n'y en a jamais eu. Sert à dire
    /// à l'utilisateur de QUAND date la vue qu'il consulte quand GWRank ne répond plus.</summary>
    public DateTime? LastSyncUtc { get; set; }

    public static GwRankBrowserState Load()
    {
        if (!File.Exists(FilePath)) return new GwRankBrowserState();
        try
        {
            var loaded = JsonSerializer.Deserialize<GwRankBrowserState>(File.ReadAllText(FilePath));
            if (loaded is null) return new GwRankBrowserState();
            loaded.Entries = new Dictionary<string, Entry>(loaded.Entries, StringComparer.OrdinalIgnoreCase);
            return loaded;
        }
        catch (Exception ex)
        {
            // Illisible = on repart d'un rapatriement complet. Coûteux, jamais faux.
            Debug.WriteLine($"[GwRank] état du miroir illisible — {ex.Message}");
            return new GwRankBrowserState();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GwRank] écriture de l'état du miroir impossible — {ex.Message}");
        }
    }
}

/// <summary>
/// Miroir local de ce qui est visible sur GWRank, sous <c>%AppData%\Z-Codex\GWRank</c>.
///
/// C'est une VUE, pas une bibliothèque : le dossier est entièrement reconstruit à chaque
/// rafraîchissement, et rien n'y est jamais modifié par l'utilisateur — pour garder un build, il
/// l'enregistre explicitement dans ses propres dossiers. Le stocker sur disque plutôt qu'en
/// mémoire permet de réutiliser tel quel le navigateur de fichiers existant (aperçu, ouverture,
/// recherche) et de consulter la vue hors ligne entre deux synchronisations.
///
/// ⚠ Rien n'est écrit dans les dossiers de builds de l'utilisateur, et la purge ne touche QUE
/// l'arborescence ci-dessous : mélanger les builds d'autrui à sa bibliothèque fausserait la
/// recherche, les sauvegardes et la détection de doublons d'identité.
/// </summary>
public static class GwRankBrowserCache
{
    /// <summary>Noms des quatre dossiers, en anglais comme le reste du canon interne.</summary>
    public const string MyBuildsFolder        = "My GWRank Builds";
    public const string MyTeamBuildsFolder    = "My GWRank Teambuilds";
    public const string PublicBuildsFolder    = "Public GWRank Builds";
    public const string PublicTeamBuildsFolder = "Public GWRank Teambuilds";

    /// <summary>Racine du miroir. Hors des dossiers de builds de l'utilisateur, volontairement.</summary>
    public static string Root => AppPaths.In("GWRank");

    /// <summary>Quand la vue locale a-t-elle été rafraîchie pour la dernière fois avec succès ?
    /// Null si jamais. C'est ce qu'on montre à l'utilisateur quand GWRank ne répond plus : la vue
    /// reste consultable, mais il doit savoir de quand elle date.</summary>
    public static DateTime? LastSyncUtc => GwRankBrowserState.Load().LastSyncUtc;

    public static IReadOnlyList<string> Folders =>
        [MyBuildsFolder, MyTeamBuildsFolder, PublicBuildsFolder, PublicTeamBuildsFolder];

    /// <summary>Vrai si un chemin appartient au miroir — sert à traiter ces fichiers en
    /// « enregistrer une copie » plutôt qu'en écriture sur place.</summary>
    public static bool IsInCache(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(Root).TrimEnd('\\', '/');
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Rapatrie ce qui a changé et reconstruit le miroir.
    ///
    /// Trois appels, dont un seul est lourd :
    ///   1. l'INVENTAIRE — la liste paginée, des résumés sans documents. C'est lui qui fait
    ///      autorité sur ce qui existe encore : un filtre temporel ne dit JAMAIS ce qui a été
    ///      supprimé, et s'y fier seul laisserait des fantômes dans la vue à jamais ;
    ///   2. la même liste filtrée sur <c>mine</c> — la seule autorité sur la PROPRIÉTÉ. Déduire
    ///      « c'est à moi » du nom d'auteur reviendrait à parier que deux joueurs ne portent
    ///      jamais le même pseudo ;
    ///   3. <c>/export</c>, borné par <c>updated_since</c> — et seulement s'il manque vraiment un
    ///      document. Les autres sont relus dans le miroir existant, l'empreinte du serveur
    ///      garantissant qu'ils sont encore les bons.
    ///
    /// <paramref name="force"/> ignore le cache et reprend tout : filet en cas de doute.
    /// </summary>
    public static async Task<GwRankSyncReport> RefreshAsync(GwRankClient client,
                                                            bool force = false,
                                                            CancellationToken ct = default)
    {
        if (!client.HasToken) return Fail(GwRankStatus.NoToken, null);

        var visible = await CollectAsync(client, null, ct);
        if (visible is null) return Fail(GwRankStatus.ServerError, "liste des builds illisible");

        var owned = await CollectAsync(client, "mine", ct);
        if (owned is null) return Fail(GwRankStatus.ServerError, "liste des builds personnels illisible");
        var mine = new HashSet<string>(owned.Select(t => t.SourceId), StringComparer.OrdinalIgnoreCase);

        var state = GwRankBrowserState.Load();
        var docs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var needed = new List<GwRankSummary>();

        foreach (var t in visible)
        {
            // Le document sur disque n'est réutilisable que si le serveur confirme, empreinte à
            // l'appui, qu'il n'a pas bougé — et qu'il est encore là. Une empreinte vide (serveur
            // plus ancien) fait retomber sur le rapatriement, jamais sur une supposition.
            if (!force
                && t.DocumentHash.Length > 0
                && state.Entries.TryGetValue(t.SourceId, out var known)
                && string.Equals(known.DocumentHash, t.DocumentHash, StringComparison.OrdinalIgnoreCase)
                && ReadCached(known.RelativePath) is { Length: > 0 } cached)
            {
                docs[t.SourceId] = cached;
                continue;
            }
            needed.Add(t);
        }

        int downloaded = 0;
        if (needed.Count > 0)
        {
            // Borne = le PLUS ANCIEN des instants concernés, moins une seconde. Prendre la date du
            // dernier passage laisserait filer un build enregistré pendant la synchro précédente.
            // Elle n'est posée que si CHAQUE build attendu porte un instant : sans quoi le filtre
            // en écarterait un en silence.
            DateTime? since = null;
            if (!force && needed.Count < visible.Count && needed.All(t => t.UpdatedAt.HasValue))
                since = needed.Min(t => t.UpdatedAt!.Value).AddSeconds(-1);

            var export = await client.ExportAsync(updatedSince: since, ct: ct);
            if (!export.IsOk) return Fail(export.Status, export.Message);
            downloaded = Absorb(export.Value!, docs);

            // Un build annoncé par l'inventaire mais absent de la réponse filtrée : on ne devine
            // pas ce qui s'est passé (horloges décalées, mise à jour concurrente), on reprend tout
            // une fois. Le cas est rare, le filet est simple.
            if (since is not null && needed.Any(t => !docs.ContainsKey(t.SourceId)))
            {
                var all = await client.ExportAsync(ct: ct);
                if (!all.IsOk) return Fail(all.Status, all.Message);
                downloaded = Absorb(all.Value!, docs);
            }
        }

        int myB = 0, myT = 0, pubB = 0, pubT = 0, skipped = 0;
        var staged = new List<StagedFile>();

        foreach (var item in visible)
        {
            // Un document absent (ou illisible) n'est pas une raison de faire échouer toute la
            // synchronisation : on compte et on continue.
            if (!docs.TryGetValue(item.SourceId, out var json) || json.Length == 0) { skipped++; continue; }

            bool isMine   = mine.Contains(item.SourceId);
            bool isPublic = string.Equals(item.Visibility, "public", StringComparison.OrdinalIgnoreCase);
            // Un build simple est un teambuild à UN personnage : c'est le seul critère, et il est
            // déjà calculé par le serveur.
            bool isSingle = item.PlayerCount <= 1;
            var name = FileNameFor(item, isMine);

            // Les deux rubriques ne s'excluent PAS : partager un build ne le retire pas de sa
            // collection. Un build à soi ET partagé apparaît donc dans « My » et dans « Public ».
            if (isMine)
            {
                staged.Add(new StagedFile(isSingle ? MyBuildsFolder : MyTeamBuildsFolder, name, json, item.SourceId));
                if (isSingle) myB++; else myT++;
            }

            // Tout ce qui n'est pas à soi et que l'API laisse voir est public par construction :
            // le drapeau manquant ne doit pas faire disparaître un build de la vue.
            if (isPublic || !isMine)
            {
                staged.Add(new StagedFile(isSingle ? PublicBuildsFolder : PublicTeamBuildsFolder, name, json, item.SourceId));
                if (isSingle) pubB++; else pubT++;
            }
        }

        Dictionary<string, string> written;
        try { written = Write(staged); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GwRank] écriture du miroir impossible : {ex}");
            return Fail(GwRankStatus.ServerError, ex.Message);
        }

        // L'état n'est enregistré qu'APRÈS l'écriture réussie du miroir : le décrire avant
        // reviendrait à promettre des fichiers qui n'existent peut-être pas.
        var next = new GwRankBrowserState { LastSyncUtc = DateTime.UtcNow };
        foreach (var item in visible)
            if (written.TryGetValue(item.SourceId, out var rel))
                next.Entries[item.SourceId] = new GwRankBrowserState.Entry
                {
                    DocumentHash = item.DocumentHash,
                    RelativePath = rel,
                };
        next.Save();

        return new GwRankSyncReport(GwRankStatus.Ok, myB, myT, pubB, pubT, skipped, null, downloaded);
    }

    // ── Interne ───────────────────────────────────────────────────────────────

    /// <summary>Un fichier prêt à écrire. Le <c>SourceId</c> voyage avec lui parce que le nom
    /// réellement retenu (suffixé en cas d'homonymie) n'est connu qu'à l'écriture.</summary>
    private readonly record struct StagedFile(string Folder, string Name, string Json, string SourceId);

    private static GwRankSyncReport Fail(GwRankStatus s, string? m)
        => new(s, 0, 0, 0, 0, 0, m);

    /// <summary>Range les documents d'un export dans le dictionnaire commun et dit combien il en
    /// portait d'exploitables.</summary>
    private static int Absorb(GwRankExport export, Dictionary<string, string> docs)
    {
        int n = 0;
        foreach (var item in export.Teambuilds)
            if (item.DocumentJson is { Length: > 0 } json) { docs[item.SourceId] = json; n++; }
        return n;
    }

    /// <summary>Relit un document déjà présent dans le miroir. Toute anomalie (fichier disparu,
    /// droits, chemin invalide) rend null, ce qui le fera simplement retélécharger.</summary>
    private static string? ReadCached(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        try
        {
            var full = Path.Combine(Root, relativePath);
            return File.Exists(full) ? File.ReadAllText(full) : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GwRank] cache illisible ({relativePath}) — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Les résumés visibles, toutes pages confondues.
    ///
    /// ⚠ <paramref name="visibility"/> null ne veut PAS dire « les miens » : le serveur renvoie
    /// alors aussi les builds publics des autres joueurs. Seule la valeur <c>mine</c> filtre.
    /// </summary>
    private static async Task<List<GwRankSummary>?> CollectAsync(GwRankClient client, string? visibility,
                                                                 CancellationToken ct)
    {
        var all = new List<GwRankSummary>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int page = 1; page <= 50; page++)   // garde-fou : 5 000 builds suffisent
        {
            var r = await client.ListAsync(page, perPage: 100, visibility, ct: ct);
            if (!r.IsOk) return null;

            var batch = r.Value!.Teambuilds;
            // Dédoublonnage : un build ajouté pendant la pagination décale les pages et peut
            // remonter deux fois. Deux fichiers pour un seul build serait visible à l'écran.
            foreach (var t in batch)
                if (seen.Add(t.SourceId)) all.Add(t);

            var total = r.Value.Pagination?.TotalCount ?? batch.Count;
            if (batch.Count == 0 || all.Count >= total) break;
        }
        return all;
    }

    /// <summary>Reconstruit le miroir : on écrit à côté puis on remplace, pour qu'une coupure
    /// réseau ou une erreur d'écriture ne laisse pas l'utilisateur devant un dossier vidé.</summary>
    /// <returns>Pour chaque <c>sourceId</c>, le chemin RELATIF du fichier écrit — celui de la
    /// première rubrique où il apparaît. Un build à soi et partagé est écrit deux fois ; en relire
    /// un seul suffit à le réutiliser au passage suivant.</returns>
    private static Dictionary<string, string> Write(List<StagedFile> items)
    {
        var root = Root;
        var staging = root + ".new";

        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        foreach (var f in Folders) Directory.CreateDirectory(Path.Combine(staging, f));

        // Les homonymes sont la norme, pas l'exception (plusieurs auteurs, mêmes noms de build) :
        // on suffixe à partir du deuxième pour ne jamais en écraser un.
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, name, json, sourceId) in items)
        {
            var candidate = Path.Combine(folder, name);
            for (int n = 2; !used.Add(candidate); n++)
                candidate = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({n}).zcx");

            File.WriteAllText(Path.Combine(staging, candidate), json, new UTF8Encoding(false));
            written.TryAdd(sourceId, candidate);
        }

        // Remplacement en dernier : jusqu'ici, l'ancienne vue était encore consultable.
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.Move(staging, root);
        return written;
    }

    /// <summary>Nom de fichier lisible tiré du nom du build. Les caractères interdits par Windows
    /// sont remplacés, pas supprimés : « Team - A/B » et « Team - AB » doivent rester distincts.</summary>
    private static string FileNameFor(GwRankSummary item, bool isMine)
    {
        var clean = Sanitize(item.Name);
        if (clean.Length == 0) clean = item.SourceId;
        if (clean.Length > 90) clean = clean[..90].TrimEnd('.', ' ');

        // L'auteur ne s'affiche que sur les builds des AUTRES : le rappeler sur les siens
        // n'apprendrait rien et allongerait chaque nom. Un dossier de fichiers n'offre pas
        // d'autre endroit pour le montrer, et l'API ne le donne que depuis sa v3.
        if (!isMine)
        {
            var author = Sanitize(item.Author);
            if (author.Length > 40) author = author[..40].TrimEnd('.', ' ');
            if (author.Length > 0) clean += $" ({author})";
        }

        return clean + ".zcx";
    }

    /// <summary>Rend un fragment de nom utilisable par Windows. Les caractères interdits sont
    /// REMPLACÉS, pas supprimés : « Team - A/B » et « Team - AB » doivent rester distincts.</summary>
    private static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '-' : c);

        // Windows refuse les points et espaces en fin de nom.
        return sb.ToString().Trim().TrimEnd('.', ' ');
    }
}
