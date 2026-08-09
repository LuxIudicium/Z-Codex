using ZCodex.Core.Models;
using System.IO;
using System.Net.Http;
using ZCodex.Core;

namespace ZCodex.Scraper;

public static class ProfessionIconService
{
    private static readonly string IconsDir =
        AppPaths.In("professions");

    private static readonly Dictionary<Profession, string> WikiUrls = new()
    {
        { Profession.Warrior,      "https://wiki.guildwars.com/wiki/Special:FilePath/Warrior-tango-icon-48.png" },
        { Profession.Ranger,       "https://wiki.guildwars.com/wiki/Special:FilePath/Ranger-tango-icon-48.png" },
        { Profession.Monk,         "https://wiki.guildwars.com/wiki/Special:FilePath/Monk-tango-icon-48.png" },
        { Profession.Necromancer,  "https://wiki.guildwars.com/wiki/Special:FilePath/Necromancer-tango-icon-48.png" },
        { Profession.Mesmer,       "https://wiki.guildwars.com/wiki/Special:FilePath/Mesmer-tango-icon-48.png" },
        { Profession.Elementalist, "https://wiki.guildwars.com/wiki/Special:FilePath/Elementalist-tango-icon-48.png" },
        { Profession.Assassin,     "https://wiki.guildwars.com/wiki/Special:FilePath/Assassin-tango-icon-48.png" },
        { Profession.Ritualist,    "https://wiki.guildwars.com/wiki/Special:FilePath/Ritualist-tango-icon-48.png" },
        { Profession.Paragon,      "https://wiki.guildwars.com/wiki/Special:FilePath/Paragon-tango-icon-48.png" },
        { Profession.Dervish,      "https://wiki.guildwars.com/wiki/Special:FilePath/Dervish-tango-icon-48.png" },
    };

    public static string? GetLocalPath(Profession profession)
    {
        if (profession == Profession.None) return null;
        return Path.Combine(IconsDir, $"{profession}.png");
    }

    public static async Task DownloadAllAsync(HttpClient? http = null)
    {
        Directory.CreateDirectory(IconsDir);
        bool ownHttp = http == null;
        http ??= new HttpClient();
        http.DefaultRequestHeaders.UserAgent.TryParseAdd("Z-Codex/1.0");

        try
        {
            foreach (var (profession, url) in WikiUrls)
            {
                var localPath = GetLocalPath(profession)!;
                if (File.Exists(localPath)) continue;
                try
                {
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
