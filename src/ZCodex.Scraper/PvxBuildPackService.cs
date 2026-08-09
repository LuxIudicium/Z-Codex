using System.IO;
using System.IO.Compression;
using System.Net.Http;
using ZCodex.Core.Importers;
using ZCodex.Core.Models;
using ZCodex.Core.Serialization;

namespace ZCodex.Scraper;

public enum PvxPack { Pve, Pvp }

public enum PvxStage { Downloading, Extracting, ConvertingTeams }

public sealed record PvxProgress(PvxStage Stage, PvxPack Pack, int Done, int Total);

/// <summary>Bilan d'un import, affiché à l'utilisateur en fin d'opération.</summary>
public sealed class PvxImportReport
{
    public int FilesWritten { get; set; }
    public int TeamsConverted { get; set; }
    /// <summary>Dossiers d'équipe laissés en .txt car ils dépassent 8 barres (cf. PvxTeamBuilder).</summary>
    public int TeamsTooLarge { get; set; }
    public List<string> Errors { get; } = new();
}

/// <summary>
/// Récupère les « build packs » publiés par PvXwiki et les dépose chez l'utilisateur.
///
/// Le contenu est produit par la communauté PvXwiki et reste sous licence CC BY-NC-SA 3.0 :
/// il est téléchargé À L'EXÉCUTION, sur la machine de l'utilisateur, et rien n'en est
/// redistribué avec Z-Codex. Voir LICENSE, section THIRD-PARTY NOTICES.
///
/// Les archives sont des .txt de codes template GW1 — le format que le jeu lui-même lit depuis
/// son dossier Templates. On les dépose donc TELS QUELS : les convertir en .zcx les rendrait
/// invisibles au jeu sans rien apporter (un .txt contient déjà professions, caractéristiques et
/// les 8 compétences ; le reste d'un .zcx n'existe qu'une fois saisi par l'utilisateur).
/// Seuls les dossiers d'ÉQUIPE gagnent un .zcx, écrit à côté du dossier : là, le .txt seul ne
/// dit pas que les 8 barres forment un groupe.
/// </summary>
public static class PvxBuildPackService
{
    /// <summary>Page de référence, affichée à l'utilisateur si un téléchargement échoue.</summary>
    public const string BuildPacksPageUrl = "https://gwpvx.fandom.com/wiki/PvXwiki:Build_Packs";

    // SEUL endroit du code où vivent ces URL. Elles pointent des archives hébergées sur Box par
    // un contributeur de PvXwiki : si elles meurent un jour, c'est ici — et seulement ici —
    // qu'on les remplace, en les relevant sur BuildPacksPageUrl.
    private const string PveUrl =
        "https://app.box.com/index.php?rm=box_download_shared_file&shared_name=81xeocotdxpmgava80bc&file_id=f_1237320387";
    private const string PvpUrl =
        "https://app.box.com/index.php?rm=box_download_shared_file&shared_name=3symlcio9053goksy4q1&file_id=f_1237320761";

    private static string UrlOf(PvxPack pack) => pack == PvxPack.Pve ? PveUrl : PvpUrl;

    /// <summary>Dossier créé par l'archive : c'est aussi ce que l'utilisateur aura à supprimer.</summary>
    public static string FolderNameOf(PvxPack pack) =>
        pack == PvxPack.Pve ? "PvE Build Packs" : "PvP Build Packs";

    public static async Task<PvxImportReport> ImportAsync(
        IReadOnlyList<PvxPack> packs,
        string destinationRoot,
        IReadOnlyDictionary<int, Skill> skillsById,
        IProgress<PvxProgress>? progress = null,
        CancellationToken ct = default)
    {
        var report = new PvxImportReport();

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.TryParseAdd("Z-Codex/1.0");

        foreach (var pack in packs)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new PvxProgress(PvxStage.Downloading, pack, 0, 0));

            byte[] bytes;
            try
            {
                bytes = await http.GetByteArrayAsync(UrlOf(pack), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                report.Errors.Add($"{FolderNameOf(pack)} : {ex.Message}");
                continue;
            }

            // Box renvoie une page HTML (et non un 404) quand un lien partagé a expiré : sans ce
            // contrôle, l'utilisateur verrait « archive corrompue » au lieu de comprendre que le
            // lien est mort.
            if (bytes.Length < 4 || bytes[0] != 'P' || bytes[1] != 'K')
            {
                report.Errors.Add($"{FolderNameOf(pack)} : réponse inattendue (lien expiré ?)");
                continue;
            }

            try
            {
                ExtractPack(bytes, destinationRoot, pack, report, progress, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                report.Errors.Add($"{FolderNameOf(pack)} : {ex.Message}");
                continue;
            }

            ConvertTeams(Path.Combine(destinationRoot, FolderNameOf(pack)), pack,
                         skillsById, report, progress, ct);
        }

        return report;
    }

    private static void ExtractPack(byte[] zipBytes, string destinationRoot, PvxPack pack,
                                    PvxImportReport report, IProgress<PvxProgress>? progress,
                                    CancellationToken ct)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        // Racine canonique, terminée par un séparateur : sert de préfixe de référence au contrôle
        // anti-évasion ci-dessous.
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationRoot))
                   + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);

        var files = archive.Entries.Where(e => e.Name.Length > 0).ToList();
        int done = 0;

        foreach (var entry in files)
        {
            ct.ThrowIfCancellationRequested();

            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));

            // Zip slip : une entrée dont le nom contient « .. » peut sortir du dossier choisi et
            // écraser n'importe quel fichier de l'utilisateur. On refuse tout ce qui n'atterrit
            // pas SOUS la racine, quelle que soit la confiance qu'on accorde à la source.
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                report.Errors.Add($"entrée ignorée (chemin hors du dossier) : {entry.FullName}");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            report.FilesWritten++;

            if (++done % 50 == 0 || done == files.Count)
                progress?.Report(new PvxProgress(PvxStage.Extracting, pack, done, files.Count));
        }
    }

    /// <summary>
    /// Écrit un .zcx par dossier d'équipe, À CÔTÉ du dossier et non dedans : le dossier garde ses
    /// .txt pour Guild Wars, et Z-Codex voit une équipe ouvrable d'un clic au même endroit.
    /// </summary>
    private static void ConvertTeams(string packRoot, PvxPack pack,
                                     IReadOnlyDictionary<int, Skill> skillsById,
                                     PvxImportReport report, IProgress<PvxProgress>? progress,
                                     CancellationToken ct)
    {
        if (!Directory.Exists(packRoot)) return;

        // Un dossier d'équipe = un dossier contenant des .txt, à l'intérieur d'une catégorie.
        var teamFolders = Directory.GetDirectories(packRoot)
            .SelectMany(Directory.GetDirectories)
            .Where(d => Directory.EnumerateFiles(d, "*" + SkillTemplateImporter.Extension).Any())
            .ToList();

        int done = 0;
        foreach (var folder in teamFolders)
        {
            ct.ThrowIfCancellationRequested();

            // Distingué AVANT l'appel : FromFolder renvoie null aussi bien pour un dossier trop
            // grand que pour une barre illisible, et ces deux cas ne se rapportent pas pareil.
            int bars = Directory.GetFiles(folder, "*" + SkillTemplateImporter.Extension).Length;
            if (bars > PvxTeamBuilder.MaxMembers)
            {
                report.TeamsTooLarge++;
            }
            else if (PvxTeamBuilder.FromFolder(folder, skillsById) is { } team)
            {
                try
                {
                    TeamBuildSerializer.Save(team, folder + TeamBuildSerializer.Extension);
                    report.TeamsConverted++;
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"{Path.GetFileName(folder)} : {ex.Message}");
                }
            }
            else
            {
                report.Errors.Add($"{Path.GetFileName(folder)} : barre illisible");
            }

            if (++done % 10 == 0 || done == teamFolders.Count)
                progress?.Report(new PvxProgress(PvxStage.ConvertingTeams, pack, done, teamFolders.Count));
        }
    }
}
