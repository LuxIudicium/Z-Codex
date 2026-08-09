using Microsoft.Extensions.Logging;
using ZCodex.Data.Entities;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZCodex.Core;

namespace ZCodex.Scraper;

/// <summary>
/// Textes FRANÇAIS des compétences depuis gwiki.fr (wiki communautaire FR, MediaWiki + API).
/// Jointure par ID officiel GW1 (champ <c>id</c> de {{Infobox Compétence}}), repli par nom
/// anglais (lien interlangue <c>[[en:...]]</c>). Seuls les TEXTES sont pris : les données de
/// calcul (stats, progression) restent celles du wiki EN — les stats de l'infobox FR servent
/// uniquement de détecteur de page périmée (le jeu reçoit encore des mises à jour que le
/// wiki FR peut ne pas suivre, cf. Mighty Throw 11/2025 : split (PvP) absent, activation 3
/// au lieu de 1). Toute anomalie → <c>FrSuspect</c> + entrée du rapport JSON.
/// </summary>
public class GwikiFrScraper(ILogger logger)
{
    private const string Api = "https://www.gwiki.fr/w/api.php";
    // "Catégorie:Compétence" pré-encodé → littéral 100 % ASCII.
    private const string SkillCategory = "Cat%C3%A9gorie%3AComp%C3%A9tence";
    private static readonly TimeSpan RequestDelay = TimeSpan.FromMilliseconds(300);
    private const int BatchSize = 50;

    // gwiki (et Fandom) répondent 403 aux User-Agent d'outils → UA de navigateur générique.
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Z-Codex/1.0");
        c.Timeout = TimeSpan.FromSeconds(30);
        return c;
    }

    /// <summary>Une page skill FR parsée. Stats = params bruts de l'infobox (vérification).</summary>
    public sealed record FrPage(
        int Id, string Title, string EnName, string NameFr, string DescriptionFr,
        string AttributeFr, string TypeFr, IReadOnlyDictionary<string, string> RawParams);

    public sealed class FetchResult
    {
        public Dictionary<int, FrPage> ById { get; } = new();
        public Dictionary<string, FrPage> ByEnName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, FrPage> ByTitle { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int PageCount { get; set; }
    }

    /// <summary>Entrée du rapport d'anomalies (JSON %AppData%\Z-Codex\gwiki_fr_report.json).</summary>
    public sealed record ReportEntry(int Id, string NameEn, string NameFr, List<string> Issues);

    // ══════════════════════════════════════════════════════════════════════
    //  Fetch : catégorie → titres → wikitext par lots de 50
    // ══════════════════════════════════════════════════════════════════════

    public async Task<FetchResult> FetchAllAsync(
        IProgress<(int done, int total, string current)>? progress = null,
        CancellationToken ct = default)
    {
        var titles = await FetchCategoryTitlesAsync(ct);
        logger.LogInformation("gwiki.fr : {Count} pages dans la catégorie skills", titles.Count);

        var result = new FetchResult();
        int batches = (titles.Count + BatchSize - 1) / BatchSize;
        for (int b = 0; b < batches; b++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((b, batches, ZCodex.Core.Models.AppLanguage.IsFr
                ? $"gwiki.fr : lot {b + 1}/{batches}"
                : $"gwiki.fr: batch {b + 1}/{batches}"));

            var batch = titles.Skip(b * BatchSize).Take(BatchSize).ToList();
            try
            {
                var contents = await FetchWikitextBatchAsync(batch, ct);
                foreach (var (title, wikitext) in contents)
                    AddPage(result, title, wikitext);
            }
            catch (Exception ex) { logger.LogWarning(ex, "gwiki.fr : lot {Batch} en échec", b + 1); }

            await Task.Delay(RequestDelay, ct);
        }
        logger.LogInformation("gwiki.fr : {Ids} pages avec ID, {Names} avec nom EN",
            result.ById.Count, result.ByEnName.Count);
        return result;
    }

    private async Task<List<string>> FetchCategoryTitlesAsync(CancellationToken ct)
    {
        var titles = new List<string>();
        string? cont = null;
        do
        {
            ct.ThrowIfCancellationRequested();
            var url = $"{Api}?action=query&list=categorymembers&cmtitle={SkillCategory}"
                    + $"&cmlimit=500&cmnamespace=0&format=json"
                    + (cont is null ? "" : $"&cmcontinue={Uri.EscapeDataString(cont)}");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
            if (doc.RootElement.TryGetProperty("query", out var q)
                && q.TryGetProperty("categorymembers", out var members))
                foreach (var m in members.EnumerateArray())
                    if (m.TryGetProperty("title", out var t) && t.GetString() is { } title)
                        titles.Add(title);
            cont = doc.RootElement.TryGetProperty("continue", out var c)
                   && c.TryGetProperty("cmcontinue", out var cc) ? cc.GetString() : null;
            await Task.Delay(RequestDelay, ct);
        } while (cont is not null);
        return titles;
    }

    private async Task<List<(string Title, string Wikitext)>> FetchWikitextBatchAsync(
        List<string> titles, CancellationToken ct)
    {
        var url = $"{Api}?action=query&prop=revisions&rvprop=content&rvslots=main&redirects=1"
                + $"&format=json&titles={Uri.EscapeDataString(string.Join("|", titles))}";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
        var list = new List<(string, string)>();
        if (!doc.RootElement.TryGetProperty("query", out var q)
            || !q.TryGetProperty("pages", out var pages)) return list;
        foreach (var page in pages.EnumerateObject())
        {
            var p = page.Value;
            if (!p.TryGetProperty("title", out var t) || t.GetString() is not { } title) continue;
            if (!p.TryGetProperty("revisions", out var revs) || revs.GetArrayLength() == 0) continue;
            var main = revs[0].GetProperty("slots").GetProperty("main");
            if (main.TryGetProperty("*", out var content) && content.GetString() is { } wikitext)
                list.Add((title, wikitext));
        }
        return list;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Parse d'une page (infobox + lien interlangue)
    // ══════════════════════════════════════════════════════════════════════

    private static readonly Regex EnLinkRegex = new(@"\[\[en:([^\]]+)\]\]", RegexOptions.Compiled);

    private void AddPage(FetchResult result, string title, string wikitext)
    {
        var p = ParsePage(title, wikitext);
        if (p is null) return;
        result.PageCount++;
        result.ByTitle.TryAdd(title, p);

        if (p.Id > 0)
        {
            // Collision d'ID (ex. page (PvP) portant l'id de la base) : la page dont le titre
            // n'est PAS suffixé " (PvP)" gagne l'entrée ById.
            if (!result.ById.TryGetValue(p.Id, out var existing))
                result.ById[p.Id] = p;
            else if (existing.Title.EndsWith(" (PvP)", StringComparison.OrdinalIgnoreCase)
                     && !p.Title.EndsWith(" (PvP)", StringComparison.OrdinalIgnoreCase))
                result.ById[p.Id] = p;
        }
        if (p.EnName.Length > 0)
        {
            result.ByEnName.TryAdd(p.EnName, p);
            // Titre FR "(PvP)" mais lien interlangue sans suffixe → enregistre aussi la
            // variante suffixée (nos entités PvP s'appellent "X (PvP)").
            if (p.Title.EndsWith(" (PvP)", StringComparison.OrdinalIgnoreCase)
                && !p.EnName.EndsWith(" (PvP)", StringComparison.OrdinalIgnoreCase))
                result.ByEnName.TryAdd(p.EnName + " (PvP)", p);
        }
    }

    private FrPage? ParsePage(string title, string wikitext)
    {
        var raw = ParseInfobox(wikitext);
        if (raw is null) return null;   // pas une page skill (pas d'{{Infobox Compétence}})

        int id = raw.TryGetValue("id", out var idStr) && int.TryParse(idStr.Trim(), out var i) ? i : 0;
        var enName = EnLinkRegex.Match(wikitext) is { Success: true } m
            ? CleanWikitext(m.Groups[1].Value) : string.Empty;

        var nameFr = Get(raw, "nom");
        if (nameFr.Length == 0) nameFr = title;
        // Concise en priorité (parité avec la tooltip EN) ; repli sur la description complète.
        var desc = Get(raw, "desc_concise");
        if (desc.Length == 0) desc = Get(raw, "description");

        return new FrPage(id, title, enName, nameFr, desc,
            Get(raw, "caracteristique"), Get(raw, "type"), raw);
    }

    private static string Get(Dictionary<string, string> raw, string key)
        => raw.TryGetValue(key, out var v) ? CleanWikitext(v) : string.Empty;

    // Params de {{Infobox Compétence...}} : lignes "| clé = valeur" jusqu'au "}}" seul.
    // Une ligne sans "|" de tête est une continuation de la valeur précédente.
    private static Dictionary<string, string>? ParseInfobox(string wikitext)
    {
        int start = wikitext.IndexOf("{{Infobox Comp", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? lastKey = null;
        foreach (var line in wikitext[start..].Split('\n').Skip(1))
        {
            var trimmed = line.TrimEnd('\r').Trim();
            if (trimmed == "}}") break;
            if (trimmed.StartsWith('|'))
            {
                int eq = trimmed.IndexOf('=');
                if (eq < 0) continue;
                lastKey = trimmed[1..eq].Trim().ToLowerInvariant();
                dict[lastKey] = trimmed[(eq + 1)..].Trim();
            }
            else if (lastKey is not null && trimmed.Length > 0)
                dict[lastKey] += " " + trimmed;
        }
        return dict.Count > 0 ? dict : null;
    }

    // ── Nettoyage wikitext → texte plat ────────────────────────────────────
    // {{range|a|c}} et {{range2|a|c}} → "a...c" (ancres rangs 0/15, le clamp de la
    // résolution couvre les pistes de titre). Liens, gras/italique et tags retirés.

    // Les bornes peuvent être SIGNÉES sur gwiki ({{range|+1|+2}} d'Affinité élémentaire) : sans
    // le [+-]? le template ne matchait pas ici et se faisait effacer plus bas par OtherTplRegex
    // → « augmentent de . ». Le signe de la 1re borne est conservé en préfixe, celui de la 2e est
    // retiré : la plage doit rester de la forme « 1...2 » pour que FrRangeRegex (vérification) et
    // SkillProgression.Resolve (résolution au rang) la reconnaissent, comme en anglais (« +1...2 »).
    private static readonly Regex RangeTplRegex = new(@"\{\{\s*range2?\s*\|\s*([+-]?\d+)\s*\|\s*([+-]?\d+)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LinkRegex = new(@"\[\[(?:[^\[\]|]*\|)?([^\[\]|]*)\]\]", RegexOptions.Compiled);
    private static readonly Regex OtherTplRegex = new(@"\{\{[^{}]*\}\}", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    public static string CleanWikitext(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        s = RangeTplRegex.Replace(s, m =>
        {
            var lo = m.Groups[1].Value;
            var hi = m.Groups[2].Value.TrimStart('+', '-');
            return $"{lo}...{hi}";
        });
        s = LinkRegex.Replace(s, "$1");
        for (int i = 0; i < 3 && s.Contains("{{"); i++)   // templates résiduels (innermost d'abord)
            s = OtherTplRegex.Replace(s, string.Empty);
        s = s.Replace("'''", string.Empty).Replace("''", string.Empty);
        s = TagRegex.Replace(s, " ");
        s = s.Replace("&nbsp;", " ").Replace("&#160;", " ");
        // Décode les entités HTML restantes APRÈS le retrait des tags (sinon un &lt; décodé en '<'
        // serait ré-avalé par TagRegex) : &#34; → «"» (cris « ... ! »), &#39; → «'», &amp; → «&»…
        s = System.Net.WebUtility.HtmlDecode(s);
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Application aux entités + vérification de fraîcheur
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reporte les textes FR sur les entités (jointure ID puis nom EN) et vérifie la
    /// fraîcheur de chaque page (stats d'infobox vs DB, ancres des plages vs progression).
    /// Anomalie → FrSuspect + entrée de rapport ; page absente → champs FR vides + rapport.
    /// </summary>
    public static (int Applied, int Missing, List<ReportEntry> Report) Apply(
        IEnumerable<SkillEntity> skills, FetchResult fr)
    {
        int applied = 0, missing = 0;
        var report = new List<ReportEntry>();

        foreach (var e in skills)
        {
            if (!fr.ById.TryGetValue(e.Id, out var p) && !fr.ByEnName.TryGetValue(e.Name, out p))
            {
                e.NameFr = e.DescriptionFr = e.AttributeFr = e.TypeFr = string.Empty;
                e.FrSuspect = false;
                missing++;
                report.Add(new ReportEntry(e.Id, e.Name, string.Empty, ["page FR absente"]));
                continue;
            }
            p = FixAllegiancePage(e.Name, p, fr);

            e.NameFr = p.NameFr;
            e.DescriptionFr = p.DescriptionFr;
            e.AttributeFr = p.AttributeFr;
            e.TypeFr = p.TypeFr;

            // Suspecte (→ repli EN de la DESCRIPTION affichée) : uniquement une plage qui ne
            // s'apparie plus à notre progression — seul cas où le texte FR montrerait des
            // chiffres faux (les stats E/C/R affichées viennent toujours de nos données EN).
            // Les écarts de stats d'infobox signalent une page en retard : rapport seulement.
            var rangeIssues = new List<string>();
            CheckRanges(e, rangeIssues);
            e.FrSuspect = rangeIssues.Count > 0;

            var issues = new List<string>(rangeIssues);
            CheckStats(e, p, issues);
            if (issues.Count > 0)
                report.Add(new ReportEntry(e.Id, e.Name, p.NameFr, issues));
            applied++;
        }
        return (applied, missing, report);
    }

    // Paires Kurzick/Luxon : gwiki et notre ID list ne s'accordent pas sur quel ID est K et
    // lequel est L (gwiki : Cauchemar éthéré (Kurzick)=2092/(Luxon)=1949 ; notre convention
    // « premier de la Skill list = Kurzick » dit l'inverse). Jointure par le SUFFIXE du titre :
    // l'entité "(Kurzick)" reçoit la page titrée "(Kurzick)" — cohérent quel que soit l'ID.
    private static FrPage FixAllegiancePage(string enName, FrPage p, FetchResult fr)
    {
        string? want = enName.EndsWith(" (Kurzick)", StringComparison.OrdinalIgnoreCase) ? " (Kurzick)"
                     : enName.EndsWith(" (Luxon)", StringComparison.OrdinalIgnoreCase) ? " (Luxon)" : null;
        if (want is null || p.Title.EndsWith(want, StringComparison.OrdinalIgnoreCase)) return p;
        string other = want == " (Kurzick)" ? " (Luxon)" : " (Kurzick)";
        if (p.Title.EndsWith(other, StringComparison.OrdinalIgnoreCase)
            && fr.ByTitle.TryGetValue(p.Title[..^other.Length] + want, out var twin))
            return twin;
        return p;
    }

    // Stats de l'infobox FR ≠ valeurs EN scrapées → page FR en retard sur un patch.
    // Seuls les params PRÉSENTS ET numériques sont comparés.
    private static void CheckStats(SkillEntity e, FrPage p, List<string> issues)
    {
        CompareInt(p, "energie", e.EnergyCost, issues);
        CompareInt(p, "adrenaline", e.Adrenaline, issues);
        CompareInt(p, "sacrifice", e.Sacrifice, issues);
        CompareTime(p, "activation", e.CastTime, issues);
        CompareTime(p, "rechargement", e.Recharge, issues);
    }

    private static void CompareInt(FrPage p, string key, int expected, List<string> issues)
    {
        if (!p.RawParams.TryGetValue(key, out var raw)) return;
        var v = CleanWikitext(raw).TrimEnd('%').Trim();
        if (v.Length == 0 || !int.TryParse(v, out var n)) return;
        if (n != expected) issues.Add($"{key}: FR={n} EN={expected}");
    }

    private static void CompareTime(FrPage p, string key, float expected, List<string> issues)
    {
        if (!p.RawParams.TryGetValue(key, out var raw)) return;
        var v = CleanWikitext(raw);
        if (ParseTime(v) is not { } t) return;
        if (Math.Abs(t - expected) > 0.001f) issues.Add($"{key}: FR={v} EN={expected}");
    }

    // "2", "1/4", "3/4", "1.5", "1,5" → secondes. Null = non numérique / absent.
    private static float? ParseTime(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return null;
        var frac = Regex.Match(s, @"^(?:(\d+)\s+)?(\d+)/(\d+)$");
        if (frac.Success)
        {
            float whole = frac.Groups[1].Success ? float.Parse(frac.Groups[1].Value) : 0;
            return whole + float.Parse(frac.Groups[2].Value) / float.Parse(frac.Groups[3].Value);
        }
        return float.TryParse(s.Replace(',', '.'),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : null;
    }

    // Chaque plage "a...c" de la description FR doit s'apparier à une colonne de NOTRE
    // progression (ancres rangs 0/15, clampé). Sans appariement, la valeur ne se résoudra
    // pas au rang → et surtout, des ancres qui ne matchent plus = équilibrage passé derrière.
    private static readonly Regex FrRangeRegex = new(@"\d+(?:\.\.\.\d+)+", RegexOptions.Compiled);

    private static void CheckRanges(SkillEntity e, List<string> issues)
    {
        if (e.DescriptionFr.Length == 0) return;
        var ranges = FrRangeRegex.Matches(e.DescriptionFr);
        if (ranges.Count == 0) return;

        string[][]? prog = null;
        if (e.Progression.Length > 0)
            try { prog = JsonSerializer.Deserialize<string[][]>(e.Progression); }
            catch (JsonException) { /* progression illisible → vérif impossible */ }
        if (prog is null || prog.Length == 0)
        {
            issues.Add($"plage FR \"{ranges[0].Value}\" sans progression EN");
            return;
        }

        foreach (Match m in ranges)
        {
            var parts = m.Value.Split("...");
            if (parts.Length != 2 || !prog.Any(v => At(v, 0) == parts[0] && At(v, 15) == parts[1]))
                issues.Add($"plage FR \"{m.Value}\" non appariée à la progression");
        }
    }

    private static string At(string[] v, int i)
        => v.Length == 0 ? "x" : v[Math.Min(i, v.Length - 1)];

    // ── Rapport JSON pour revue manuelle (Philippe) ────────────────────────

    public static string ReportPath => AppPaths.In("gwiki_fr_report.json");

    public static void SaveReport(List<ReportEntry> report, int applied, int missing)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
            var payload = new
            {
                GeneratedAt = DateTime.Now,
                Applied = applied,
                Missing = missing,
                Suspects = report.Where(r => r.Issues.Count > 0 && r.NameFr.Length > 0).ToList(),
                MissingPages = report.Where(r => r.NameFr.Length == 0).Select(r => $"{r.Id} {r.NameEn}").ToList(),
            };
            File.WriteAllText(ReportPath, JsonSerializer.Serialize(payload,
                new JsonSerializerOptions { WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GwikiFrScraper.SaveReport: {ex.Message}");
        }
    }
}
