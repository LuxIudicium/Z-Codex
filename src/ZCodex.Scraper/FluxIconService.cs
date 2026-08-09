using System.IO;
using System.Net.Http;
using ZCodex.Core;

namespace ZCodex.Scraper;

/// <summary>
/// Télécharge et expose l'icône de flux du wiki (File:Flux.jpg, 64×64), affichée par
/// l'indicateur de flux du teambuild et du build simple. Miroir de
/// <see cref="ConditionIconService"/> : cache disque, téléchargement au démarrage, non bloquant.
///
/// Elle était EMBARQUÉE dans l'exécutable jusqu'ici (Assets\Flux.jpg). C'était la dernière
/// ressource du jeu que Z-Codex redistribuait, ce que le README affirme pourtant ne pas faire.
/// Elle est donc récupérée comme toutes les autres, sur la machine de l'utilisateur.
/// </summary>
public static class FluxIconService
{
    // Icône unique : pas de sous-dossier, elle vit à la racine du dossier de données comme
    // scrape_info.json.
    private static readonly string IconPath = AppPaths.In("flux.jpg");

    /// <summary>Chemin local de l'icône, ou <c>null</c> tant qu'elle n'a pas été téléchargée.</summary>
    public static string? GetLocalPath() => File.Exists(IconPath) ? IconPath : null;

    public static async Task DownloadAllAsync(HttpClient? http = null)
    {
        if (File.Exists(IconPath)) return;

        bool ownHttp = http == null;
        http ??= new HttpClient();
        http.DefaultRequestHeaders.UserAgent.TryParseAdd("Z-Codex/1.0");

        try
        {
            var bytes = await http.GetByteArrayAsync(
                "https://wiki.guildwars.com/wiki/Special:FilePath/Flux.jpg");
            await File.WriteAllBytesAsync(IconPath, bytes);
        }
        catch { /* non bloquant */ }
        finally
        {
            if (ownHttp) http.Dispose();
        }
    }
}
