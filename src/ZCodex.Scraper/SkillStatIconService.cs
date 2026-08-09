using System.IO;
using System.Net.Http;
using ZCodex.Core;

namespace ZCodex.Scraper;

/// <summary>
/// Télécharge et expose les petites icônes de stats GW1 (énergie, activation, recharge,
/// adrénaline utilisées dans l'infobulle de compétence ; + jeu "mech-*" pour les en-têtes
/// de colonnes du catalogue de compétences). Miroir de <see cref="ProfessionIconService"/>.
/// </summary>
public static class SkillStatIconService
{
    private static readonly string IconsDir =
        AppPaths.In("stats");

    // Clé logique → nom de fichier sur le wiki (Special:FilePath).
    private static readonly Dictionary<string, string> WikiFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        { "energy",     "Energy.png" },
        { "cast",       "Activation.png" },
        { "recharge",   "Recharge.png" },
        { "adrenaline", "Adrenaline.png" },
        // Icônes des en-têtes de colonnes du catalogue de compétences (jeu d'icônes "Tango",
        // distinct des icônes ci-dessus déjà utilisées dans l'infobulle). Clés préfixées "mech-"
        // pour ne jamais partager un nom de fichier en cache avec les entrées existantes.
        { "mech-adrenaline", "Tango-adrenaline.png" },
        { "mech-sacrifice",  "Tango-sacrifice.png" },
        { "mech-overcast",   "Tango-overcast.png" },
        { "mech-energy",     "Tango-energy.png" },
        { "mech-upkeep",     "Tango-upkeep.png" },
        { "mech-activation", "Tango-activation-darker.png" },
        { "mech-recharge",   "Tango-recharge-darker.png" },
    };

    public static string? GetLocalPath(string key)
    {
        if (!WikiFiles.ContainsKey(key)) return null;
        return Path.Combine(IconsDir, $"{key}.png");
    }

    public static async Task DownloadAllAsync(HttpClient? http = null)
    {
        Directory.CreateDirectory(IconsDir);
        bool ownHttp = http == null;
        http ??= new HttpClient();
        http.DefaultRequestHeaders.UserAgent.TryParseAdd("Z-Codex/1.0");

        try
        {
            foreach (var (key, file) in WikiFiles)
            {
                var localPath = GetLocalPath(key)!;
                if (File.Exists(localPath)) continue;
                try
                {
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
