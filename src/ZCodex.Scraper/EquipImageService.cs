using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using ZCodex.Core;

namespace ZCodex.Scraper;

// Skins visuels d'équipement (armure genrée + icônes d'arme, cf. GwEquipmentInfo.ArmorSkinFile /
// WeaponIconFile) — mirroir de ProfessionIconService mais LAZY : une image à la demande au
// premier affichage, jamais de bulk download. Les 404 sont mémorisés (négatif caché) pour ne
// jamais retenter la même image en boucle dans une session.
public static class EquipImageService
{
    private static readonly string ArmorDir =
        AppPaths.In("armor");
    private static readonly string WeaponDir =
        AppPaths.In("weapons");

    // fileName → chemin local (succès) ou null (404 confirmé, ne pas retenter).
    private static readonly ConcurrentDictionary<string, string?> _cache = new();
    private static readonly SemaphoreSlim _gate = new(4); // poli envers le wiki, pas de rafale
    private static HttpClient? _http;

    private static HttpClient Http()
    {
        if (_http != null) return _http;
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.TryParseAdd("Z-Codex/1.0 (armor skins)");
        return _http = http;
    }

    // Chemin local du skin d'armure pour cet item (fileName déjà construit par
    // GwEquipmentInfo.ArmorSkinFile). Null si le fichier n'existe pas sur le wiki (404 négatif
    // caché) — l'appelant doit alors afficher le placeholder neutre.
    public static Task<string?> GetArmorSkinAsync(string fileName) => GetAsync(fileName, ArmorDir);

    // Chemin local de l'icône d'arme (fileName construit par GwEquipmentInfo.WeaponIconFile).
    public static Task<string?> GetWeaponIconAsync(string fileName) => GetAsync(fileName, WeaponDir);

    private static async Task<string?> GetAsync(string fileName, string dir)
    {
        if (_cache.TryGetValue(fileName, out var cached)) return cached;

        Directory.CreateDirectory(dir);
        var localPath = Path.Combine(dir, fileName);
        if (File.Exists(localPath)) return _cache[fileName] = localPath;

        await _gate.WaitAsync();
        try
        {
            // Un autre appel concurrent a pu résoudre entre-temps (double-check après le gate).
            if (_cache.TryGetValue(fileName, out cached)) return cached;
            if (File.Exists(localPath)) return _cache[fileName] = localPath;

            try
            {
                var url = $"https://wiki.guildwars.com/wiki/Special:FilePath/{Uri.EscapeDataString(fileName)}";
                var bytes = await Http().GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(localPath, bytes);
                return _cache[fileName] = localPath;
            }
            catch
            {
                return _cache[fileName] = null; // 404 (ou erreur réseau) — négatif caché pour la session
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
