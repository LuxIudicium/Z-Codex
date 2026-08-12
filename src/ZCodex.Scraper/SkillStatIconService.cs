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

    private static IEnumerable<(string Url, string LocalPath)> Entries()
        => WikiFiles.Select(kv => (IconDownloader.WikiUrl(kv.Value), GetLocalPath(kv.Key)!));

    public static Task<IconReport> DownloadAllAsync(HttpClient? http = null)
        => IconDownloader.EnsureAsync(Entries(), http);
}
