using AngleSharp;
using AngleSharp.Dom;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ZCodex.Scraper;

public static class GameUpdateChecker
{
    private const string UpdatesUrl = "https://wiki.guildwars.com/wiki/Game_updates";
    private const string MainPageUrl = "https://wiki.guildwars.com/wiki/Main_Page";

    // Dans un titre de section, on ne cherche QUE la date, jamais ce qui la précède.
    //
    // Ce préfixe a déjà changé sans prévenir : "Update June 25, 2026" et "Update, June 17, 2026"
    // pendant des années, puis "Update - August 13, 2026" à partir du 13 août 2026. L'ancienne
    // version exigeait le préfixe : elle rejetait les trois dernières mises à jour en silence et
    // retenait la première ligne encore lisible (29 juillet), plus ancienne que le catalogue de
    // l'utilisateur — donc aucune notification.
    private static readonly Regex DatePattern =
        new(@"[A-Za-z]+\s+\d{1,2},\s*\d{4}", RegexOptions.IgnoreCase);

    // Le lien vers la mise à jour du jour dans l'encart « News » de la page d'accueil.
    private static readonly Regex NewsLinkPattern =
        new(@"Feedback:Game_updates/(\d{8})", RegexOptions.IgnoreCase);

    private static readonly string[] DateFormats =
        ["MMMM d, yyyy", "MMMM dd, yyyy"];

    public static async Task<DateTime?> GetLastUpdateDateAsync(CancellationToken ct = default)
    {
        // Deux sources indépendantes, et la plus récente des deux l'emporte. Elles se
        // désynchronisent : mesuré le 26 août 2026, l'encart d'accueil annonçait encore la mise à
        // jour de la semaine précédente. Exiger leur accord ferait donc manquer des mises à jour ;
        // les croiser ainsi ne fait qu'ajouter des chances d'en repérer une.
        DateTime? fromUpdates = null, fromNews = null;

        try { fromUpdates = await ReadUpdatesPageAsync(ct); }
        catch { /* une source muette ne doit pas emporter l'autre */ }

        try { fromNews = await ReadMainPageAsync(ct); }
        catch { /* idem */ }

        if (fromUpdates == null) return fromNews;
        if (fromNews == null) return fromUpdates;
        return fromUpdates > fromNews ? fromUpdates : fromNews;
    }

    // Source 1 : la page Game_updates, une section de titre par mise à jour.
    private static async Task<DateTime?> ReadUpdatesPageAsync(CancellationToken ct)
    {
        var doc = await LoadAsync(UpdatesUrl, ct);
        DateTime? latest = null;

        // Les h2 directement, et non les ".mw-headline" qu'ils contiennent : ce span interne est
        // une particularité de MediaWiki que les versions récentes suppriment. Lire le titre
        // lui-même fonctionne dans les deux cas.
        foreach (var headline in doc.QuerySelectorAll("h2"))
        {
            var text = headline.TextContent.Trim();
            if (!text.StartsWith("Update", StringComparison.OrdinalIgnoreCase)) continue;

            var m = DatePattern.Match(text);
            if (m.Success && DateTime.TryParseExact(m.Value, DateFormats,
                    CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
                latest = Keep(latest, date);
        }
        return latest;
    }

    // Source 2 : l'encart « News » de la page d'accueil, qui désigne la dernière mise à jour par
    // un lien « Feedback:Game_updates/AAAAMMJJ ». On lit la date DU LIEN et non le texte affiché :
    // ce format ne dépend d'aucune tournure de phrase ni d'aucun nom de mois anglais.
    private static async Task<DateTime?> ReadMainPageAsync(CancellationToken ct)
    {
        var doc = await LoadAsync(MainPageUrl, ct);
        DateTime? latest = null;

        foreach (var link in doc.QuerySelectorAll("a[href]"))
        {
            var m = NewsLinkPattern.Match(link.GetAttribute("href") ?? string.Empty);
            if (m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                latest = Keep(latest, date);
        }
        return latest;
    }

    // Chargement hors cache (cf. WikiFetch : le wiki sert au hasard des copies d'âges différents,
    // et une copie antérieure à la mise à jour du jour la rendait tout bonnement invisible).
    // Deux requêtes par démarrage tout au plus : la charge ajoutée est négligeable.
    private static Task<IDocument> LoadAsync(string url, CancellationToken ct)
        => BrowsingContext.New(Configuration.Default.WithDefaultLoader()).OpenFreshAsync(url, ct);

    // Retient la plus récente des dates, en écartant celles franchement futures : la page
    // d'accueil peut annoncer une mise à jour à venir, et une date future rendrait la
    // notification permanente. Un jour de marge couvre le décalage horaire du serveur.
    private static DateTime? Keep(DateTime? latest, DateTime candidate)
    {
        if (candidate.Date > DateTime.UtcNow.Date.AddDays(1)) return latest;
        return latest == null || candidate > latest ? candidate : latest;
    }
}
