using System.Diagnostics;
using System.Text;

namespace ZCodex.Core.Sync;

/// <summary>Bilan d'un rafraîchissement, affiché à l'utilisateur.</summary>
public sealed record GwRankSyncReport(
    GwRankStatus Status,
    int MyBuilds, int MyTeamBuilds,
    int PublicBuilds, int PublicTeamBuilds,
    int Skipped,
    string? Message)
{
    public bool IsOk => Status == GwRankStatus.Ok;
    public int Total => MyBuilds + MyTeamBuilds + PublicBuilds + PublicTeamBuilds;
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
    /// Rapatrie tout ce qui est visible et reconstruit le miroir.
    ///
    /// Deux appels sont nécessaires faute de champ d'auteur côté API : <c>/export</c> donne tout
    /// (les miens ET les publics des autres), la liste paginée donne les miens — la différence sur
    /// <c>sourceId</c> livre les publics d'autrui. Un simple <c>owner</c> dans le résumé
    /// supprimerait le second appel (cf. docs/gwrank_api_retours.md §0.2).
    /// </summary>
    public static async Task<GwRankSyncReport> RefreshAsync(GwRankClient client,
                                                            CancellationToken ct = default)
    {
        if (!client.HasToken) return Fail(GwRankStatus.NoToken, null);

        var export = await client.ExportAsync(ct: ct);
        if (!export.IsOk) return Fail(export.Status, export.Message);

        var mine = await CollectMineAsync(client, ct);
        if (mine is null) return Fail(GwRankStatus.ServerError, "liste des builds personnels illisible");

        int myB = 0, myT = 0, pubB = 0, pubT = 0, skipped = 0;
        var staged = new List<(string Folder, string Name, string Json)>();

        foreach (var item in export.Value!.Teambuilds)
        {
            // Un document absent (ou illisible) n'est pas une raison de faire échouer toute la
            // synchronisation : on compte et on continue.
            if (item.DocumentJson is not { Length: > 0 } json) { skipped++; continue; }

            bool isMine   = mine.Contains(item.SourceId);
            bool isPublic = string.Equals(item.Visibility, "public", StringComparison.OrdinalIgnoreCase);
            // Un build simple est un teambuild à UN personnage : c'est le seul critère, et il est
            // déjà calculé par le serveur.
            bool isSingle = item.PlayerCount <= 1;
            var name = FileNameFor(item);

            // Les deux rubriques ne s'excluent PAS : partager un build ne le retire pas de sa
            // collection. Un build à soi ET partagé apparaît donc dans « My » et dans « Public ».
            if (isMine)
            {
                staged.Add((isSingle ? MyBuildsFolder : MyTeamBuildsFolder, name, json));
                if (isSingle) myB++; else myT++;
            }

            // Tout ce qui n'est pas à soi et que l'API laisse voir est public par construction :
            // le drapeau manquant ne doit pas faire disparaître un build de la vue.
            if (isPublic || !isMine)
            {
                staged.Add((isSingle ? PublicBuildsFolder : PublicTeamBuildsFolder, name, json));
                if (isSingle) pubB++; else pubT++;
            }
        }

        try { Write(staged); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GwRank] écriture du miroir impossible : {ex}");
            return Fail(GwRankStatus.ServerError, ex.Message);
        }

        return new GwRankSyncReport(GwRankStatus.Ok, myB, myT, pubB, pubT, skipped, null);
    }

    // ── Interne ───────────────────────────────────────────────────────────────

    private static GwRankSyncReport Fail(GwRankStatus s, string? m)
        => new(s, 0, 0, 0, 0, 0, m);

    /// <summary>Les <c>sourceId</c> des builds de l'utilisateur, toutes pages confondues.</summary>
    private static async Task<HashSet<string>?> CollectMineAsync(GwRankClient client, CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int page = 1; page <= 50; page++)   // garde-fou : 5 000 builds personnels suffisent
        {
            var r = await client.ListAsync(page, perPage: 100, ct);
            if (!r.IsOk) return null;

            var batch = r.Value!.Teambuilds;
            foreach (var t in batch) ids.Add(t.SourceId);

            var total = r.Value.Pagination?.TotalCount ?? batch.Count;
            if (batch.Count == 0 || ids.Count >= total) break;
        }
        return ids;
    }

    /// <summary>Reconstruit le miroir : on écrit à côté puis on remplace, pour qu'une coupure
    /// réseau ou une erreur d'écriture ne laisse pas l'utilisateur devant un dossier vidé.</summary>
    private static void Write(List<(string Folder, string Name, string Json)> items)
    {
        var root = Root;
        var staging = root + ".new";

        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        foreach (var f in Folders) Directory.CreateDirectory(Path.Combine(staging, f));

        // Les homonymes sont la norme, pas l'exception (plusieurs auteurs, mêmes noms de build) :
        // on suffixe à partir du deuxième pour ne jamais en écraser un.
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, name, json) in items)
        {
            var candidate = Path.Combine(folder, name);
            for (int n = 2; !used.Add(candidate); n++)
                candidate = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({n}).zcx");

            File.WriteAllText(Path.Combine(staging, candidate), json, new UTF8Encoding(false));
        }

        // Remplacement en dernier : jusqu'ici, l'ancienne vue était encore consultable.
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.Move(staging, root);
    }

    /// <summary>Nom de fichier lisible tiré du nom du build. Les caractères interdits par Windows
    /// sont remplacés, pas supprimés : « Team - A/B » et « Team - AB » doivent rester distincts.</summary>
    private static string FileNameFor(GwRankExportItem item)
    {
        var name = item.Name;
        if (string.IsNullOrWhiteSpace(name)) name = item.SourceId;

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '-' : c);

        // Windows refuse les points et espaces en fin de nom, et tronque au-delà du chemin maximal.
        var clean = sb.ToString().Trim().TrimEnd('.', ' ');
        if (clean.Length == 0) clean = item.SourceId;
        if (clean.Length > 90) clean = clean[..90].TrimEnd('.', ' ');

        return clean + ".zcx";
    }
}
