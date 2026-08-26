using System.IO;
using System.Net;
using System.Net.Http;
using ZCodex.Core.Importers;
using ZCodex.Core.Models;
using ZCodex.Core.Serialization;

namespace ZCodex.Scraper;

/// <summary>Bilan de l'écriture, affiché à l'utilisateur en fin d'opération.</summary>
public sealed class GvgReportWriteReport
{
    public int FilesWritten { get; set; }
    public List<string> Errors { get; } = [];
}

/// <summary>
/// Récupère un rapport de match publié par gvg.report et le dépose en .zcx chez l'utilisateur.
///
/// La page du site ne contient rien : tout est chargé après coup depuis son API. On interroge donc
/// directement la charge « overview », la plus légère (une centaine de Ko) — elle porte les
/// guildes, les classements, la carte, la date et les seize barres. Les autres charges du site
/// pèsent des dizaines de Mo pour de l'analyse de combat dont un team build n'a que faire.
///
/// Le contenu appartient à gvg.report et à ses contributeurs : il est téléchargé À L'EXÉCUTION,
/// sur la machine de l'utilisateur, et rien n'en est redistribué avec Z-Codex.
/// </summary>
public static class GvgReportService
{
    /// <summary>Site de référence, affiché à l'utilisateur quand rien ne peut être lu.</summary>
    public const string SiteUrl = "https://gvg.report";

    /// <summary>URL de la page d'un rapport — telle qu'on la range dans les notes du build.</summary>
    public static string PageUrlOf(string reportId) => $"{SiteUrl}/report/{reportId}";

    /// <summary>
    /// Lit le rapport et prépare les deux équipes, SANS rien écrire : l'utilisateur doit d'abord
    /// voir quelles guildes s'affrontent pour choisir celles qu'il garde.
    ///
    /// Renvoie <c>null</c> si le document reçu n'a pas la forme attendue (le site peut changer).
    /// Lève <see cref="HttpRequestException"/> si le rapport est introuvable ou le réseau muet.
    /// </summary>
    public static async Task<GvgReportImport?> FetchAsync(string reportId,
                                                          IReadOnlyDictionary<int, Skill> skillsById,
                                                          CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.TryParseAdd("Z-Codex/1.0");

        // Deux routes pour la même charge. Le site lui-même bascule sur la seconde quand la
        // première ne répond pas : on fait pareil plutôt que d'échouer là où sa propre page passe.
        var json = await GetOrNull(http, $"{SiteUrl}/api/reports/{reportId}?payload=overview", ct)
                ?? await GetOrNull(http, $"{SiteUrl}/api/reports/{reportId}.overview.json", ct)
                ?? throw new HttpRequestException(null, null, HttpStatusCode.NotFound);

        return GvgReportTeamBuilder.FromOverviewJson(json, skillsById, PageUrlOf(reportId));
    }

    private static async Task<string?> GetOrNull(HttpClient http, string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
    }

    /// <summary>
    /// Écrit les équipes retenues. Un échec sur l'une n'empêche pas l'autre : le nom de fichier
    /// porte des noms de guilde, que rien ne garantit acceptables partout (partage réseau, clé USB).
    /// </summary>
    public static GvgReportWriteReport Write(IEnumerable<GvgReportTeam> teams, string destination)
    {
        var report = new GvgReportWriteReport();

        foreach (var team in teams)
        {
            var path = Path.Combine(destination, team.FileName + TeamBuildSerializer.Extension);
            try
            {
                TeamBuildSerializer.Save(team.Build, path);
                report.FilesWritten++;
            }
            catch (Exception ex)
            {
                report.Errors.Add($"{team.FileName} : {ex.Message}");
            }
        }

        return report;
    }
}
