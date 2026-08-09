using AngleSharp;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ZCodex.Scraper;

public static class GameUpdateChecker
{
    private const string UpdatesUrl = "https://wiki.guildwars.com/wiki/Game_updates";

    // "Update June 25, 2026" or "Update, June 17, 2026"
    private static readonly Regex DatePattern =
        new(@"^Update,?\s+(.+)$", RegexOptions.IgnoreCase);

    private static readonly string[] DateFormats =
        ["MMMM d, yyyy", "MMMM dd, yyyy"];

    public static async Task<DateTime?> GetLastUpdateDateAsync(CancellationToken ct = default)
    {
        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);
        var doc = await context.OpenAsync(UpdatesUrl, ct);

        foreach (var headline in doc.QuerySelectorAll("h2 .mw-headline"))
        {
            var text = headline.TextContent.Trim();
            var m = DatePattern.Match(text);
            if (!m.Success) continue;

            var dateStr = m.Groups[1].Value.Trim();
            if (DateTime.TryParseExact(dateStr, DateFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;
        }
        return null;
    }
}
