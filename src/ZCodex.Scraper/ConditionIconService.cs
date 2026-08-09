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

    public static async Task DownloadAllAsync(HttpClient? http = null)
    {
        Directory.CreateDirectory(IconsDir);
        bool ownHttp = http == null;
        http ??= new HttpClient();
        http.DefaultRequestHeaders.UserAgent.TryParseAdd("Z-Codex/1.0");

        try
        {
            foreach (var condition in GwConditionData.All)
            {
                var localPath = GetLocalPath(condition.Name);
                if (File.Exists(localPath)) continue;
                try
                {
                    var file = $"{condition.Name.Replace(' ', '_')}.jpg";
                    var url = $"https://wiki.guildwars.com/wiki/Special:FilePath/{file}";
                    var bytes = await http.GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(localPath, bytes);
                }
                catch { /* non bloquant */ }
            }
        }
        finally
        {
            if (ownHttp) http.Dispose();
        }
    }
}
