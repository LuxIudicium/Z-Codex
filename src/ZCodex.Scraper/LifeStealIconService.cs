using System.IO;
using System.Net.Http;
using ZCodex.Core;

namespace ZCodex.Scraper;

/// <summary>
/// Télécharge et expose l'icône de vol de vie du wiki (File:Life_Draining.jpg, 64×64 — format
/// natif des icônes de compétences), affichée par les lignes ARTIFICIELLES de vol de vie du
/// calculateur de spike (mods d'arme vampiriques). Miroir de <see cref="FluxIconService"/> :
/// cache disque, téléchargement au démarrage, non bloquant.
/// </summary>
public static class LifeStealIconService
{
    // Icône unique : pas de sous-dossier, elle vit à la racine du dossier de données comme
    // flux.jpg et scrape_info.json.
    private static readonly string IconPath = AppPaths.In("life-draining.jpg");

    /// <summary>Chemin local de l'icône, ou <c>null</c> tant qu'elle n'a pas été téléchargée —
    /// ou qu'elle est illisible (les lignes de vol de vie s'affichent alors sans icône).</summary>
    public static string? GetLocalPath() => IconDownloader.IsUsable(IconPath) ? IconPath : null;

    public static Task<IconReport> DownloadAllAsync(HttpClient? http = null)
        => IconDownloader.EnsureAsync(
            new[] { (IconDownloader.WikiUrl("Life_Draining.jpg"), IconPath) }, http);
}
