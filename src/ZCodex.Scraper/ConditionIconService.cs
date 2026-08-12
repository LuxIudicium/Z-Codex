using ZCodex.Core.Data;
using System.IO;
using System.Net.Http;
using ZCodex.Core;

namespace ZCodex.Scraper;

/// <summary>
/// Télécharge et expose les icônes 64px des 10 conditions GW1 (fichier wiki "&lt;Nom&gt;.jpg",
/// ex : File:Bleeding.jpg) pour le bandeau de conditions. Miroir de
/// <see cref="SkillStatIconService"/> : cache disque, téléchargement au démarrage, non bloquant.
/// </summary>
public static class ConditionIconService
{
    private static readonly string IconsDir =
        AppPaths.In("conditions");

    // Chemin local de l'icône d'une condition (le fichier peut ne pas encore être téléchargé).
    public static string GetLocalPath(string conditionName)
        => Path.Combine(IconsDir, $"{conditionName}.jpg");

    private static IEnumerable<(string Url, string LocalPath)> Entries()
        => GwConditionData.All.Select(c =>
            (IconDownloader.WikiUrl($"{c.Name.Replace(' ', '_')}.jpg"), GetLocalPath(c.Name)));

    public static Task<IconReport> DownloadAllAsync(HttpClient? http = null)
        => IconDownloader.EnsureAsync(Entries(), http);
}
