using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using ZCodex.Core.Data;
using ZCodex.Core.Models;
using ZCodex.Data.Entities;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZCodex.Core;

namespace ZCodex.Scraper;

public class WikiSkillScraper(ILogger<WikiSkillScraper> logger)
{
    private const string BaseUrl = "https://wiki.guildwars.com";
    private static readonly TimeSpan RequestDelay = TimeSpan.FromMilliseconds(600);
    // Délai plus court pour la passe progression (≈1300 pages individuelles).
    private static readonly TimeSpan ProgressionDelay = TimeSpan.FromMilliseconds(100);
    // Requêtes simultanées de la passe progression. En séquentiel cette phase pesait à elle
    // seule ~12 min sur les ~15 min du premier lancement. Volontairement sous les 6 de la
    // phase d'icônes : le wiki est un service communautaire gratuit.
    private const int ProgressionConcurrency = 4;
    // Détecte une plage de variable "n...n[...n]" dans une description.
    private static readonly Regex VariableRangeRegex = new(@"\d+(?:\.\.\.\d+)+", RegexOptions.Compiled);

    // Les messages de progression sont affichés par SkillUpdateWindow → langue courante.
    private static string L(string fr, string en) => AppLanguage.IsFr ? fr : en;

    // ── Sources skills (stats) ─────────────────────────────────────────────
    private static readonly Dictionary<Profession, string> SkillListPages = new()
    {
        { Profession.Warrior,      "/wiki/List_of_warrior_skills" },
        { Profession.Ranger,       "/wiki/List_of_ranger_skills" },
        { Profession.Monk,         "/wiki/List_of_monk_skills" },
        { Profession.Necromancer,  "/wiki/List_of_necromancer_skills" },
        { Profession.Mesmer,       "/wiki/List_of_mesmer_skills" },
        { Profession.Elementalist, "/wiki/List_of_elementalist_skills" },
        { Profession.Assassin,     "/wiki/List_of_assassin_skills" },
        { Profession.Ritualist,    "/wiki/List_of_ritualist_skills" },
        { Profession.Paragon,      "/wiki/List_of_paragon_skills" },
        { Profession.Dervish,      "/wiki/List_of_dervish_skills" },
    };

    // ── Sources icônes HD (gallery) — EN PAUSE ──────────────────────────────
    // Désactivé : IconUrlHd n'est utilisée nulle part dans l'UI (le natif 64px suffit à
    // l'affichage). Code gardé pour réactivation future, pas supprimé.
    /*
    private static readonly Dictionary<Profession, string> GalleryPages = new()
    {
        { Profession.Warrior,      "/wiki/Gallery_of_high_resolution_warrior_skill_icons" },
        { Profession.Ranger,       "/wiki/Gallery_of_high_resolution_ranger_skill_icons" },
        { Profession.Monk,         "/wiki/Gallery_of_high_resolution_monk_skill_icons" },
        { Profession.Necromancer,  "/wiki/Gallery_of_high_resolution_necromancer_skill_icons" },
        { Profession.Mesmer,       "/wiki/Gallery_of_high_resolution_mesmer_skill_icons" },
        { Profession.Elementalist, "/wiki/Gallery_of_high_resolution_elementalist_skill_icons" },
        { Profession.Assassin,     "/wiki/Gallery_of_high_resolution_assassin_skill_icons" },
        { Profession.Ritualist,    "/wiki/Gallery_of_high_resolution_ritualist_skill_icons" },
        { Profession.Paragon,      "/wiki/Gallery_of_high_resolution_paragon_skill_icons" },
        { Profession.Dervish,      "/wiki/Gallery_of_high_resolution_dervish_skill_icons" },
    };
    */

    // ── PvE cross-profession ───────────────────────────────────────────────
    private static readonly Dictionary<string, string> PvEH2ToAttribute = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Core",             "No Attribute" },
        { "Factions",         "Factions PvE only (all professions)" },
        { "Nightfall",        "Nightfall PvE only (all professions)" },
        { "Eye of the North", "Eye of the North PvE only" },
    };

    // H3 sous-sections → attribut précis (priorité sur H2)
    private static readonly Dictionary<string, string> PvEH3ToAttribute = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Anniversary skills",   "No Attribute" },
        { "Allegiance skills",    "Allegiance rank" },
        { "Lightbringer skills",  "Lightbringer rank" },
        { "Sunspear skills",      "Sunspear rank" },
        { "Asura skills",         "Asura rank" },
        { "Deldrimor skills",     "Deldrimor rank" },
        { "Ebon Vanguard skills", "Ebon Vanguard rank" },
        { "Norn skills",          "Norn rank" },
    };

    // ── Skills renommées : pages de profession = nom actuel, ID list = ancien nom ──
    // GW1 a renommé certaines skills sans mettre à jour Skill_template_format/Skill_list.
    // Sans alias, la skill actuelle n'est pas matchée (→ ID de repli) et l'ancien nom est
    // injecté en orphelin "No Attribute" (phase 4.5) tout en portant le vrai ID GW1.
    // Clé = nom actuel (page de profession), valeur = nom dans l'ID list.
    private static readonly Dictionary<string, string> IdListAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "\"Help!\"", "\"Help Me!\"" },
    };

    private static readonly HashSet<string> IdListAliasTargets =
        new(IdListAliases.Values, StringComparer.OrdinalIgnoreCase);

    // ══════════════════════════════════════════════════════════════════════
    //  Point d'entrée principal
    // ══════════════════════════════════════════════════════════════════════

    /// <param name="onBaseSkillsReady">
    /// Appelé une fois le catalogue de base constitué, AVANT les phases longues (progressions,
    /// textes FR). Permet à l'appelant de le persister pour qu'une coupure ne perde pas tout.
    /// </param>
    public async Task<List<SkillEntity>> ScrapeAllAsync(
        IProgress<(int done, int total, string current)>? progress = null,
        CancellationToken ct = default,
        Func<List<SkillEntity>, Task>? onBaseSkillsReady = null)
    {
        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);

        int total = SkillListPages.Count + 2; // stats + PvE + skill ID list (icônes HD en pause, cf. étape 2 commentée)
        int done = 0;
        var results = new List<SkillEntity>();

        // ── 1. Skills par profession (stats) ──────────────────────────────
        foreach (var (profession, path) in SkillListPages)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((done++, total, $"{profession} (stats)"));

            try
            {
                var skills = await ScrapeStatsPageAsync(context, profession, path, ct);
                results.AddRange(skills);
                logger.LogInformation("{Profession}: {Count} skills", profession, skills.Count);
            }
            catch (Exception ex) { logger.LogError(ex, "Erreur stats {Profession}", profession); }

            await Task.Delay(RequestDelay, ct);
        }

        // ── 2. Icônes HD depuis les galleries — EN PAUSE ──────────────────
        // Désactivé : IconUrlHd n'est utilisée nulle part dans l'UI. Code gardé pour réactivation
        // future (cf. commentaire GalleryPages plus haut).
        /*
        var iconsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (profession, path) in GalleryPages)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((done++, total, L($"{profession} (icônes HD)", $"{profession} (HD icons)")));

            try
            {
                var icons = await ScrapeGalleryAsync(context, path, ct);
                foreach (var (name, url) in icons)
                    iconsByName.TryAdd(name, url);
            }
            catch (Exception ex) { logger.LogError(ex, "Erreur gallery {Profession}", profession); }

            await Task.Delay(RequestDelay, ct);
        }

        // Applique les icônes HD aux skills — EN RÉSERVE (IconUrlHd), sans écraser le natif 64px
        // capté depuis la page de liste (IconUrl), qui sert à l'affichage UI (net en 1:1).
        foreach (var skill in results)
            if (TryResolveHdIcon(skill.Name, iconsByName, out var hdUrl))
                skill.IconUrlHd = hdUrl;
        */

        // ── 3. Skills PvE cross-profession ────────────────────────────────
        progress?.Report((done++, total, L("Skills PvE (toutes professions)", "PvE skills (all professions)")));
        try
        {
            var pveSkills = await ScrapePvEPageAsync(context, ct);
            // Icônes HD en pause (cf. étape 2 commentée) :
            // foreach (var s in pveSkills)
            //     if (TryResolveHdIcon(s.Name, iconsByName, out var hdUrl))
            //         s.IconUrlHd = hdUrl;
            results.AddRange(pveSkills);
            logger.LogInformation("PvE cross-profession: {Count} skills", pveSkills.Count);
        }
        catch (Exception ex) { logger.LogError(ex, "Erreur PvE page"); }

        // ── 4. Assignation des vrais IDs GW1 ──────────────────────────────
        progress?.Report((done++, total, L("IDs GW1 (Skill_template_format/Skill_list)…",
                                           "GW1 IDs (Skill_template_format/Skill_list)…")));
        try
        {
            var (singles, pairs) = await ScrapeSkillIdListAsync(context, ct);
            int matched = 0;
            foreach (var skill in results)
            {
                // Paires Kurzick/Luxon : on retire le suffixe pour chercher dans `pairs`
                if (skill.Name.EndsWith(" (Kurzick)", StringComparison.OrdinalIgnoreCase))
                {
                    var baseName = skill.Name[..^10]; // retire " (Kurzick)"
                    if (pairs.TryGetValue(baseName, out var p)) { skill.Id = p.K; matched++; }
                }
                else if (skill.Name.EndsWith(" (Luxon)", StringComparison.OrdinalIgnoreCase))
                {
                    var baseName = skill.Name[..^8]; // retire " (Luxon)"
                    if (pairs.TryGetValue(baseName, out var p)) { skill.Id = p.L; matched++; }
                }
                else if (singles.TryGetValue(skill.Name, out int gwId)
                      || (IdListAliases.TryGetValue(skill.Name, out var aliasName)
                          && singles.TryGetValue(aliasName, out gwId)))
                {
                    skill.Id = gwId;
                    matched++;
                }
            }
            logger.LogInformation("IDs GW1 : {Matched}/{Total} matchés", matched, results.Count);

            // 4.5 — Ajoute les skills cross-profession absents des pages de profession.
            // "Resurrection Signet" n'apparaît sur aucune List_of_X_skills (c'est une No Attribute
            // skill universelle) ; on l'injecte ici avec l'ID exact tiré du skill list.
            var existingNames = new HashSet<string>(results.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
            var existingIds   = new HashSet<int>(results.Select(s => s.Id));
            foreach (var (name, id) in singles)
            {
                if (id is <= 0 or >= 3000) continue;           // skip placeholders et PvP-splits
                if (IdListAliasTargets.Contains(name)) continue; // ancien nom remappé sur la skill actuelle
                if (existingNames.Contains(name)) continue;     // déjà scraped
                if (existingIds.Contains(id)) continue;         // collision d'ID
                var orphan = new SkillEntity
                {
                    Id          = id,
                    Name        = name,
                    ProfessionId = (int)Profession.None,
                    Attribute   = "No Attribute",
                    IconUrl     = $"{BaseUrl}/wiki/Special:FilePath/{Uri.EscapeDataString(name.Replace(' ', '_'))}.jpg",
                    WikiUrl     = $"{BaseUrl}/wiki/{Uri.EscapeDataString(name.Replace(' ', '_'))}",
                    UpdatedAt   = DateTime.UtcNow,
                };
                // Ces orphelins n'ont aucune ligne de List_of_X_skills : leur infobox individuelle
                // est la seule source de description/stats (sinon l'infobulle est vide, cf. Resurrection
                // Signet). On lit la page maintenant.
                try
                {
                    var pageDoc = await context.OpenFreshAsync(orphan.WikiUrl, ct);
                    EnrichFromSkillPage(pageDoc, orphan);
                }
                catch (Exception ex) { logger.LogWarning(ex, "Enrichissement orphelin échoué {Skill}", name); }
                await Task.Delay(RequestDelay, ct);

                results.Add(orphan);
                existingNames.Add(name);
                existingIds.Add(id);
            }
        }
        catch (Exception ex) { logger.LogError(ex, "Erreur liste IDs GW1"); }

        // ── 5. Retire les doublons "plain" des skills Kurzick/Luxon ──────────
        // Ces skills apparaissent sur la page de leur profession (ex: "Triple Shot" sur Ranger)
        // ET dans la section Allegiance de la page PvE — on ne garde que les variantes suffixées.
        var dualBases = new HashSet<string>(
            results
                .Where(s => s.Name.EndsWith(" (Kurzick)", StringComparison.OrdinalIgnoreCase)
                         || s.Name.EndsWith(" (Luxon)",   StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Name.EndsWith(" (Kurzick)", StringComparison.OrdinalIgnoreCase)
                    ? s.Name[..^10] : s.Name[..^8]),
            StringComparer.OrdinalIgnoreCase);

        results.RemoveAll(s => dualBases.Contains(s.Name));
        logger.LogInformation("Doublons Allegiance supprimés : {Count} entrées retirées", dualBases.Count);

        // ── Point de sauvegarde intermédiaire ────────────────────────────────
        // Le catalogue est exploitable ici : noms, professions, attributs, coûts, descriptions.
        // Ce qui suit (progressions ~1300 pages, textes FR, puis icônes côté service) représente
        // l'essentiel de la durée. On laisse l'appelant écrire ce qu'on a déjà, pour qu'une
        // coupure réseau laisse une application utilisable plutôt qu'une base vide.
        if (onBaseSkillsReady != null)
        {
            progress?.Report((done, total, L("Sauvegarde intermédiaire…", "Saving partial catalogue…")));
            await onBaseSkillsReady(results);
        }

        // ── 6. Tables de progression (page par page, en parallèle) ───────────
        // Coût : une requête par PAGE de skill à variables (~1300). Donne les valeurs réelles
        // par rang ; pas d'interpolation possible, il faut la table complète. Skills sans plage
        // dans la description → sautés (rien à résoudre).
        //
        // Les URL sont dédoublonnées AVANT le téléchargement (Kurzick/Luxon partagent une page),
        // là où l'ancienne version sérialisait tout et découvrait les doublons en chemin.
        var needProgression = results
            .Where(s => !string.IsNullOrEmpty(s.WikiUrl) && VariableRangeRegex.IsMatch(s.Description))
            .ToList();
        var progressionUrls = needProgression
            .Select(s => s.WikiUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        int progTotal = done + progressionUrls.Count;
        var progressionByUrl = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int progDone = 0;

        // Récupère un lot d'URL en parallèle ; renvoie celles qui ont échoué.
        async Task<List<string>> FetchProgressionsAsync(IReadOnlyList<string> urls, bool countsTowardProgress)
        {
            var failed = new ConcurrentBag<string>();
            using var gate = new SemaphoreSlim(ProgressionConcurrency, ProgressionConcurrency);
            var jobs = urls.Select(async url =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    // Un IBrowsingContext AngleSharp n'est pas prévu pour un usage concurrent :
                    // chaque tâche ouvre le sien plutôt que de partager celui de la méthode.
                    var ctx = BrowsingContext.New(config);
                    // Cache ordinaire ASSUMÉ ici, seul endroit à ne pas passer par OpenFreshAsync :
                    // ~1300 pages, et forcer autant de régénérations sur un wiki communautaire
                    // serait hors de proportion. Le retard possible est borné à 5 h, sans commune
                    // mesure avec le délai que met le wiki à être édité après une mise à jour.
                    var doc = await ctx.OpenAsync(url, ct);
                    var prog = WikiProgressionParser.Parse(doc);
                    progressionByUrl[url] = prog is null ? string.Empty : JsonSerializer.Serialize(prog);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failed.Add(url);
                    logger.LogWarning(ex, "Progression échouée {Url}", url);
                }
                finally
                {
                    if (countsTowardProgress)
                    {
                        int d = Interlocked.Increment(ref progDone);
                        progress?.Report((done + d, progTotal,
                            L($"Progression : {d}/{progressionUrls.Count}",
                              $"Progression: {d}/{progressionUrls.Count}")));
                    }
                    gate.Release();
                }

                // Marge de politesse en plus du plafond de simultanéité.
                await Task.Delay(ProgressionDelay, ct);
            });
            await Task.WhenAll(jobs);
            return failed.ToList();
        }

        var failedUrls = await FetchProgressionsAsync(progressionUrls, countsTowardProgress: true);

        // Un seul réessai. Sans lui, un incident passager laisse la compétence avec des plages
        // non résolues dans son infobulle, sans que rien ne le signale à l'utilisateur.
        if (failedUrls.Count > 0)
        {
            logger.LogInformation("Progression : réessai de {Count} page(s) en échec", failedUrls.Count);
            progress?.Report((done + progDone, progTotal,
                L($"Progression : réessai ({failedUrls.Count})", $"Progression: retrying ({failedUrls.Count})")));
            var stillFailed = await FetchProgressionsAsync(failedUrls, countsTowardProgress: false);
            if (stillFailed.Count > 0)
                logger.LogWarning("Progression : {Count} page(s) toujours en échec après réessai", stillFailed.Count);
        }
        done += progressionUrls.Count;

        foreach (var skill in needProgression)
            if (progressionByUrl.TryGetValue(skill.WikiUrl, out var json))
                skill.Progression = json;

        int progMatched = needProgression.Count(s => !string.IsNullOrEmpty(s.Progression));
        logger.LogInformation("Progression : {Matched} skills avec table ({Pages} pages, {Concurrency} en parallèle)",
            progMatched, progressionUrls.Count, ProgressionConcurrency);

        // ── 7. Textes français (gwiki.fr, jointure par ID GW1) ───────────────
        // Ne touche qu'aux champs *Fr : les stats/progressions EN restent la référence de
        // calcul. Rapport d'anomalies (pages FR périmées/absentes) → gwiki_fr_report.json.
        progress?.Report((done, progTotal, L("Textes français (gwiki.fr)…", "French texts (gwiki.fr)…")));
        try
        {
            var gwiki = new GwikiFrScraper(logger);
            var frPages = await gwiki.FetchAllAsync(progress, ct);
            var (applied, missing, report) = GwikiFrScraper.Apply(results, frPages);
            GwikiFrScraper.SaveReport(report, applied, missing);
            logger.LogInformation("gwiki.fr : {Applied} skills FR, {Missing} sans page, {Suspects} suspectes",
                applied, missing, report.Count(r => r.NameFr.Length > 0));
        }
        catch (Exception ex) { logger.LogError(ex, "Erreur textes français gwiki.fr"); }

        progress?.Report((progTotal, progTotal, L("Terminé", "Done")));
        return results;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Scraper stats (List_of_[profession]_skills)
    // ══════════════════════════════════════════════════════════════════════

    private async Task<List<SkillEntity>> ScrapeStatsPageAsync(
        IBrowsingContext ctx, Profession profession, string path, CancellationToken ct)
    {
        var doc = await ctx.OpenFreshAsync(BaseUrl + path, ct);
        var skills = new List<SkillEntity>();
        var table = doc.QuerySelector("table.sortable");
        if (table == null) { logger.LogWarning("Pas de table.sortable sur {Path}", path); return skills; }

        foreach (var row in table.QuerySelectorAll("tr[data-name]"))
        {
            var skill = ParseStatsRow(row, profession);
            if (skill != null) skills.Add(skill);
        }
        return skills;
    }

    private SkillEntity? ParseStatsRow(IElement row, Profession profession)
    {
        var th = row.QuerySelectorAll("th").ToList();
        var td = row.QuerySelectorAll("td").ToList();
        if (th.Count < 2 || td.Count < 7) return null;

        // Icône native 64px (affichage UI). Enrichissement HD en pause (cf. étape 2 commentée).
        var iconSrc = th[0].QuerySelector("img")?.GetAttribute("src") ?? string.Empty;
        var iconUrl = iconSrc.StartsWith("/") ? BaseUrl + iconSrc : iconSrc;

        // Nom + URL wiki
        var nameLink = th[1].QuerySelector("a");
        if (nameLink == null) return null;
        var name = (nameLink.GetAttribute("title") ?? nameLink.TextContent).Trim();
        if (string.IsNullOrEmpty(name)) return null;
        var wikiHref = nameLink.GetAttribute("href") ?? string.Empty;
        var wikiUrl = wikiHref.StartsWith("/") ? BaseUrl + wikiHref : wikiHref;

        // Rejette les sous-pages d'historique du wiki (ex. "Verata's_Gaze/Skill_history") : ce ne sont
        // pas des skills. Leur ligne a une disposition de colonnes décalée → l'attribut (td[6]) tombe
        // sur la description, qui remontait alors comme fausse "caractéristique" dans le catalogue.
        if (wikiHref.Contains("/Skill_history", StringComparison.OrdinalIgnoreCase)) return null;

        // Elite ?
        bool isElite = (th[0].GetAttribute("style") ?? "").Contains("#FD0", StringComparison.OrdinalIgnoreCase);

        // Description + type de skill
        var description = td[0].TextContent.Trim();
        var skillType = ExtractSkillType(description, isElite);

        // Stats numériques. Layout : td[0]=desc, td[1]=coût spécial (mécanique variable selon la
        // profession), td[2]=énergie, td[3]=activation, td[4]=recharge, td[5]=icône d'acquisition,
        // td[6]=attribut, td[7]=campagne.
        ParseSpecialCost(td[1], out int adrenaline, out int sacrifice, out int overcast, out int upkeep);
        int energy     = ParseHiddenSpanInt(td[2]);
        float castTime = ParseHiddenSpanFloat(td[3]);
        float recharge = ParseHiddenSpanFloat(td[4]);

        // Attribut : td[6], retire le caractère de tri final
        string attribute = "No Attribute";
        if (td.Count > 6)
        {
            var raw = td[6].TextContent.Trim();
            var stripped = Regex.Replace(raw, @"[A-Za-z]$", string.Empty).Trim();
            attribute = string.IsNullOrEmpty(stripped) ? "No Attribute" : stripped;
        }

        // Campagne : td[7], texte simple (Core/Prophecies/Factions/Nightfall/Eye of the North).
        // Pas de caractère de tri ⇒ Trim uniquement (surtout pas le regex d'attribut : casserait "Core").
        string campaign = td.Count > 7 ? td[7].TextContent.Trim() : string.Empty;

        return new SkillEntity
        {
            Name        = name,
            ProfessionId = (int)profession,
            Attribute   = attribute,
            Description = description,
            EnergyCost  = energy,
            Adrenaline  = adrenaline,
            Sacrifice   = sacrifice,
            Overcast    = overcast,
            Upkeep      = upkeep,
            CastTime    = castTime,
            Recharge    = recharge,
            SkillType   = skillType,
            Campaign    = campaign,
            IconUrl     = iconUrl,
            WikiUrl     = wikiUrl,
            UpdatedAt   = DateTime.UtcNow,
        };
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Scraper icônes HD (Gallery_of_high_resolution_[profession]_skill_icons) — EN PAUSE
    //  Désactivé (cf. ScrapeAllAsync étape 2) : HD non utilisée dans l'UI. Gardé pour reprise future.
    // ══════════════════════════════════════════════════════════════════════
    /*
    private async Task<Dictionary<string, string>> ScrapeGalleryAsync(
        IBrowsingContext ctx, string path, CancellationToken ct)
    {
        var doc = await ctx.OpenFreshAsync(BaseUrl + path, ct);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Structure : <div><a href="File:..."><img srcset="...url 2x"></a><br><a title="Name">Name</a></div>
        foreach (var div in doc.QuerySelectorAll("div[style*='float']"))
        {
            var img = div.QuerySelector("img");
            if (img == null) continue;

            // Nom du skill depuis le titre de l'image ou le lien wiki
            var skillName = img.GetAttribute("alt")?.Trim() ?? string.Empty;
            // Retire le suffixe ".jpg" éventuel dans l'alt
            skillName = Regex.Replace(skillName, @"\.(jpg|png)$", "", RegexOptions.IgnoreCase).Trim();
            if (string.IsNullOrEmpty(skillName)) continue;

            // URL HD depuis srcset "2x"
            var srcset = img.GetAttribute("srcset") ?? string.Empty;
            var hdMatch = Regex.Match(srcset, @"(/images/[^\s]+)\s+2x");
            if (!hdMatch.Success) continue;

            var hdUrl = BaseUrl + hdMatch.Groups[1].Value;
            result[skillName] = hdUrl;
        }

        return result;
    }

    // Cherche l'URL HD d'un skill dans le dict galerie. Repli sur le nom de base des variantes
    // PvP : la galerie n'indexe que le nom de base, mais la version " (PvP)" partage la même icône.
    private static bool TryResolveHdIcon(
        string name, Dictionary<string, string> iconsByName, out string hdUrl)
    {
        if (iconsByName.TryGetValue(name, out hdUrl!)) return true;
        if (name.EndsWith(" (PvP)", StringComparison.OrdinalIgnoreCase)
            && iconsByName.TryGetValue(name[..^6], out hdUrl!)) return true;
        hdUrl = string.Empty;
        return false;
    }
    */

    // ══════════════════════════════════════════════════════════════════════
    //  Scraper PvE cross-profession (/wiki/PvE-only_skill)
    // ══════════════════════════════════════════════════════════════════════

    private async Task<List<SkillEntity>> ScrapePvEPageAsync(IBrowsingContext ctx, CancellationToken ct)
    {
        var doc = await ctx.OpenFreshAsync(BaseUrl + "/wiki/PvE-only_skill", ct);
        var results = new List<SkillEntity>();
        string currentAttribute = "No Attribute";

        // Itère H2, H3 et tables pour avoir les attributs précis par sous-section.
        // Le wiki génère "CoreEdit" (sans crochets) → on lit le span .mw-headline.
        foreach (var node in doc.Body!.QuerySelectorAll("h2, h3, table.sortable"))
        {
            if (node.TagName == "H2")
            {
                var heading = HeadlineText(node);
                if (PvEH2ToAttribute.TryGetValue(heading, out var attr))
                    currentAttribute = attr;
                continue;
            }
            if (node.TagName == "H3")
            {
                var heading = HeadlineText(node);
                if (PvEH3ToAttribute.TryGetValue(heading, out var attr))
                    currentAttribute = attr;
                continue;
            }

            foreach (var row in node.QuerySelectorAll("tr[data-name]"))
                results.AddRange(ParsePvERow(row, currentAttribute));
        }
        return results;
    }

    // Retourne 1 ou 2 skills (2 pour les icônes 64×128 Kurzick/Luxon)
    private List<SkillEntity> ParsePvERow(IElement row, string attribute)
    {
        var results = new List<SkillEntity>();
        var th = row.QuerySelectorAll("th").ToList();
        var td = row.QuerySelectorAll("td").ToList();
        if (th.Count < 2 || td.Count < 4) return results;

        var iconImg  = th[0].QuerySelector("img");
        var iconSrc  = iconImg?.GetAttribute("src") ?? string.Empty;
        var iconUrl  = iconSrc.StartsWith("/") ? BaseUrl + iconSrc : iconSrc;
        bool isDual  = iconImg?.GetAttribute("height") == "128"; // 64×128 → Kurzick/Luxon

        var nameLink = th[1].QuerySelector("a");
        if (nameLink == null) return results;
        var baseName = (nameLink.GetAttribute("title") ?? nameLink.TextContent).Trim();
        if (string.IsNullOrEmpty(baseName)) return results;
        var wikiHref = nameLink.GetAttribute("href") ?? string.Empty;
        var wikiUrl  = wikiHref.StartsWith("/") ? BaseUrl + wikiHref : wikiHref;

        // Symétrie avec ParseStatsRow : jamais de sous-page d'historique du wiki.
        if (wikiHref.Contains("/Skill_history", StringComparison.OrdinalIgnoreCase)) return results;

        var description = td[0].TextContent.Trim();
        bool isElite = (th[0].GetAttribute("style") ?? "").Contains("#FD0", StringComparison.OrdinalIgnoreCase);
        // Même colonne "coût spécial" (td[1]) que les tables de profession : mécanique par ligne,
        // portée par l'icône Tango de la cellule (ex : "Over the Limit" → upkeep -1).
        ParseSpecialCost(td[1], out int adrenaline, out int sacrifice, out int overcast, out int upkeep);
        int energy     = ParseHiddenSpanInt(td[2]);
        float castTime = ParseHiddenSpanFloat(td[3]);
        float recharge = ParseHiddenSpanFloat(td[4]);
        string sType   = ExtractSkillType(description, isElite);
        var cost = (adrenaline, sacrifice, overcast, upkeep);
        // TODO(skilldata): campagne des skills PvE — colonne absente/spéciale dans cette table,
        // à dériver lors du crawl page-par-page (chantier données, étape 4).

        if (isDual)
        {
            // Crée deux entrées distinctes — les IDs GW1 sont différents pour Kurzick/Luxon
            results.Add(MakePvEEntity(baseName + " (Kurzick)", attribute, description, energy, cost, castTime, recharge, sType, iconUrl + "#top",    wikiUrl));
            results.Add(MakePvEEntity(baseName + " (Luxon)",   attribute, description, energy, cost, castTime, recharge, sType, iconUrl + "#bottom", wikiUrl));
        }
        else
        {
            results.Add(MakePvEEntity(baseName, attribute, description, energy, cost, castTime, recharge, sType, iconUrl, wikiUrl));
        }
        return results;
    }

    private static SkillEntity MakePvEEntity(
        string name, string attribute, string description,
        int energy, (int Adrenaline, int Sacrifice, int Overcast, int Upkeep) cost,
        float castTime, float recharge, string skillType,
        string iconUrl, string wikiUrl) => new()
    {
        Name         = name,
        ProfessionId = (int)Profession.None,
        Attribute    = attribute,
        Description  = description,
        EnergyCost   = energy,
        Adrenaline   = cost.Adrenaline,
        Sacrifice    = cost.Sacrifice,
        Overcast     = cost.Overcast,
        Upkeep       = cost.Upkeep,
        CastTime     = castTime,
        Recharge     = recharge,
        SkillType    = skillType,
        IconUrl      = iconUrl,
        WikiUrl      = wikiUrl,
        UpdatedAt    = DateTime.UtcNow,
    };

    // ══════════════════════════════════════════════════════════════════════
    //  IDs GW1 officiels (Skill_template_format/Skill_list)
    // ══════════════════════════════════════════════════════════════════════

    // Scrape la table ID↔Nom et retourne :
    //   singles[name]       → ID unique pour les skills standards
    //   pairs[name]         → (premierID, deuxièmeID) pour les paires Kurzick/Luxon
    // La liste wiki utilise le MÊME nom pour les deux variantes (ex. "Aura of Holy Might" × 2).
    // Le premier ID est le LUXON et le second le KURZICK — vérifié sur des codes de template
    // réels du jeu (19/07/2026, Philippe) : Shadow Sanctuary (Kurzick)=2091, Ether Nightmare
    // (Kurzick)=2092, soit à chaque fois le SECOND id de la paire. gwiki.fr concorde.
    private async Task<(Dictionary<string, int> Singles, Dictionary<string, (int K, int L)> Pairs)>
        ScrapeSkillIdListAsync(IBrowsingContext ctx, CancellationToken ct)
    {
        var doc = await ctx.OpenFreshAsync(BaseUrl + "/wiki/Skill_template_format/Skill_list", ct);
        var singles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pairs   = new Dictionary<string, (int K, int L)>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in doc.QuerySelectorAll("table tr"))
        {
            var cells = row.QuerySelectorAll("td").ToList();
            if (cells.Count < 2) continue;
            if (!int.TryParse(cells[0].TextContent.Trim(), out int id) || id <= 0) continue;

            var name = cells[1].QuerySelector("a")?.GetAttribute("title")?.Trim()
                       ?? cells[1].TextContent.Trim();
            if (string.IsNullOrEmpty(name)) continue;

            if (singles.TryGetValue(name, out int firstId))
            {
                // Deuxième occurrence → paire : premier id = Luxon, second = Kurzick.
                pairs[name] = (K: id, L: firstId);
                singles.Remove(name);
            }
            else
            {
                singles[name] = id;
            }
        }

        logger.LogInformation("Skill ID list : {S} singles, {P} paires K/L", singles.Count, pairs.Count);
        return (singles, pairs);
    }

    // Extrait le texte du heading depuis le span .mw-headline (le wiki met "CoreEdit" en TextContent).
    private static string HeadlineText(IElement heading) =>
        heading.QuerySelector(".mw-headline")?.TextContent?.Trim()
        ?? heading.TextContent.Trim();

    // ══════════════════════════════════════════════════════════════════════
    //  Conditions infligées (pages « Skills that cause X »)
    // ══════════════════════════════════════════════════════════════════════

    // Termes qui matérialisent la condition dans une phrase de description (formes verbales
    // incluses). Sert UNIQUEMENT à classer cible/lanceur — l'appartenance à la liste vient
    // de la table wiki, pas du texte.
    private static readonly Dictionary<string, string[]> ConditionTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Bleeding",      ["Bleeding"] },
        { "Blind",         ["Blind", "Blinded", "Blindness"] },
        { "Burning",       ["Burning", "on fire"] },
        { "Cracked Armor", ["Cracked Armor"] },
        { "Crippled",      ["Crippled"] },
        { "Dazed",         ["Dazed"] },
        { "Deep Wound",    ["Deep Wound"] },
        { "Disease",       ["Disease", "Diseased"] },
        { "Poison",        ["Poison", "Poisoned"] },
        { "Weakness",      ["Weakness", "Weakened"] },
    };

    /// <summary>
    /// Scrape les 10 pages de condition (GwConditionData.All) : pour chacune, UNIQUEMENT la
    /// table sous le h2 « Skills that cause X » (les tables « Related skills » qui suivent —
    /// benefit from / dependent effects — sont hors périmètre). Chaque ligne porte
    /// data-name = nom exact du skill (variantes PvP incluses) → jointure directe.
    /// Retour : nom de skill (apostrophe normalisée) → entrées ("X" cible / "X:self" lanceur).
    /// </summary>
    public async Task<Dictionary<string, List<string>>> ScrapeConditionsAsync(
        IProgress<(int done, int total, string current)>? progress = null,
        CancellationToken ct = default)
    {
        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);

        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        int done = 0;

        foreach (var condition in GwConditionData.All)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((done++, GwConditionData.All.Count,
                L($"Conditions : {condition.Name}", $"Conditions: {condition.Name}")));

            try
            {
                var path = "/wiki/" + condition.Name.Replace(' ', '_');
                var doc = await context.OpenFreshAsync(BaseUrl + path, ct);

                var table = FindCauseTable(doc);
                if (table is null)
                {
                    logger.LogWarning("Conditions : table « Skills that cause » introuvable sur {Path}", path);
                    continue;
                }

                int rows = 0;
                foreach (var tr in table.QuerySelectorAll("tr[data-name]"))
                {
                    var name = tr.GetAttribute("data-name")!.Replace('’', '\'').Trim();
                    // Les noms à GUILLEMETS (cris : "Finish Him!", "You're All Alone!"…) ont un
                    // data-name VIDE sur le wiki (bug de leur template) → repli sur le texte du
                    // lien de la cellule nom (le lien de la cellule icône n'a pas de texte).
                    if (name.Length == 0)
                        name = tr.QuerySelectorAll("th a")
                            .Select(a => a.TextContent.Trim().Replace('’', '\''))
                            .FirstOrDefault(t => t.Length > 0) ?? string.Empty;
                    if (name.Length == 0) continue;

                    // 1re cellule td = description (les th sont l'icône et le nom).
                    var desc = tr.QuerySelector("td")?.TextContent ?? string.Empty;
                    var entry = IsSelfInflicted(desc, condition.Name)
                        ? condition.Name + GwConditionData.SelfSuffix
                        : condition.Name;

                    if (!result.TryGetValue(name, out var list))
                        result[name] = list = [];
                    if (!list.Contains(entry)) list.Add(entry);
                    rows++;
                }
                logger.LogInformation("Conditions : {Condition} — {Rows} skills", condition.Name, rows);
            }
            catch (Exception ex) { logger.LogError(ex, "Erreur page condition {Condition}", condition.Name); }

            await Task.Delay(RequestDelay, ct);
        }

        return result;
    }

    // Table directement sous le h2 dont l'ancre commence par "Skills_that_cause" ; on s'arrête
    // au heading suivant pour ne JAMAIS déborder sur les tables « Related skills ».
    // Deux skins possibles selon ce que MediaWiki sert : desktop (la table est un SIBLING du h2)
    // ou mobile/MobileFrontend (la section est emballée dans <section class="mf-section-N">
    // sibling du h2, la table est DEDANS) — constaté au dry-run : AngleSharp reçoit le mobile.
    private static IElement? FindCauseTable(IDocument doc)
    {
        var headline = doc.QuerySelector("span.mw-headline[id^='Skills_that_cause']");
        var heading = headline?.Closest("h2");
        for (var el = heading?.NextElementSibling; el is not null; el = el.NextElementSibling)
        {
            if (el.LocalName is "h2" or "h3") return null;      // section suivante sans table
            if (el.LocalName == "table") return el;             // skin desktop
            if (el.LocalName == "section")                      // skin mobile : table dans la section
                return el.QuerySelector("table");
        }
        return null;
    }

    // Classement cible/lanceur : la condition n'est « self » que si TOUTES les phrases qui la
    // mentionnent commencent par « You » (ex : Signet of Agony « You begin Bleeding »). Une seule
    // phrase l'infligeant à la cible suffit à la classer cible. Découpe sur ". " (et pas '.')
    // pour ne pas casser les plages "3...13...15". Aucun terme trouvé → cible (le wiki fait foi :
    // l'infliction peut passer par une créature, ex : Animate Shambling Horror).
    private static bool IsSelfInflicted(string description, string conditionName)
    {
        var terms = ConditionTerms.TryGetValue(conditionName, out var t) ? t : [conditionName];
        bool anySelf = false;
        foreach (var raw in description.Split(". ", StringSplitOptions.RemoveEmptyEntries))
        {
            var sentence = raw.Trim();
            if (!terms.Any(term => sentence.Contains(term, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (sentence.StartsWith("You ", StringComparison.Ordinal)) anySelf = true;
            else return false;   // au moins une phrase inflige à la cible
        }
        return anySelf;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Téléchargement des icônes en local
    // ══════════════════════════════════════════════════════════════════════

    public static string IconsDirectory => AppPaths.In("icons");

    public async Task DownloadIconsAsync(
        List<SkillEntity> skills,
        IProgress<(int done, int total, string current)>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(IconsDirectory);

        // Icône native 64px (IconUrl, affichage UI). Cache clé = nom du FICHIER SOURCE (pas le nom
        // du skill) : les skills partageant une icône (base/PvP, dual Kurzick/Luxon 64×128) la
        // téléchargent une seule fois.
        // Téléchargement HD (IconUrlHd) EN PAUSE — le scraper ne la renseigne plus (cf. ScrapeAllAsync
        // étape 2 commentée). Ligne gardée en commentaire pour reprise future.
        var plan = new List<(SkillEntity skill, bool hd, string url, string fragment, string local)>();
        foreach (var skill in skills)
        {
            AddIconTarget(plan, skill, hd: false, skill.IconUrl);
            // AddIconTarget(plan, skill, hd: true,  skill.IconUrlHd);
        }

        // Check « besoin de télécharger ou pas » : ne retient que les fichiers distincts absents du cache.
        var toDownload = plan
            .Where(p => !File.Exists(p.local))
            .GroupBy(p => p.local)
            .Select(g => (local: g.Key, url: g.First().url))
            .ToList();

        logger.LogInformation("Icônes : {Cached} en cache, {Missing} à télécharger",
            plan.Count(p => File.Exists(p.local)), toDownload.Count);

        if (toDownload.Count > 0)
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.Add("User-Agent", "Z-Codex/1.0 (skill icon cache)");

            var semaphore = new SemaphoreSlim(6, 6);
            int total = toDownload.Count;
            int done = 0;

            var tasks = toDownload.Select(async dl =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var bytes = await http.GetByteArrayAsync(dl.url, ct);
                    await File.WriteAllBytesAsync(dl.local, bytes, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Icône non téléchargée ({Url}): {Msg}", dl.url, ex.Message);
                }
                finally
                {
                    semaphore.Release();
                    int d = Interlocked.Increment(ref done);
                    progress?.Report((d, total, Path.GetFileName(dl.local)));
                }
            });

            await Task.WhenAll(tasks);
        }

        // Repointe chaque skill vers ses fichiers locaux (s'ils ont bien été récupérés).
        // On enregistre le NOM DE FICHIER SEUL, pas le chemin absolu : la base reste alors
        // valable quel que soit l'emplacement du dossier de données (renommage de l'application,
        // autre profil utilisateur, base recopiée). La lecture recolle le dossier courant via
        // AppPaths.IconFile — qui accepte aussi les chemins absolus des bases antérieures.
        foreach (var (skill, hd, _, fragment, local) in plan)
            if (File.Exists(local))
            {
                var stored = Path.GetFileName(local) + fragment;
                if (hd) skill.IconUrlHd = stored;
                else    skill.IconUrl   = stored;
            }
    }

    // Ajoute une icône (native ou HD) au plan de téléchargement : sépare le fragment #top/#bottom,
    // ignore les URLs vides, et calcule le chemin local de cache.
    private static void AddIconTarget(
        List<(SkillEntity skill, bool hd, string url, string fragment, string local)> plan,
        SkillEntity skill, bool hd, string? rawUrl)
    {
        rawUrl ??= string.Empty;
        int hashIdx = rawUrl.LastIndexOf('#');
        var fragment    = hashIdx >= 0 ? rawUrl[hashIdx..] : string.Empty;
        var downloadUrl = hashIdx >= 0 ? rawUrl[..hashIdx] : rawUrl;
        if (string.IsNullOrEmpty(downloadUrl)) return;
        var local = Path.Combine(IconsDirectory, IconFileNameFromUrl(downloadUrl));
        plan.Add((skill, hd, downloadUrl, fragment, local));
    }

    // Nom de fichier cache dérivé de l'URL source (décodée, assainie). Conserve l'unicité du fichier
    // wiki : low-res et "(large)" donnent des noms distincts → pas d'écrasement, upgrade naturel.
    private static string IconFileNameFromUrl(string url)
    {
        var path = url;
        int q = path.IndexOf('?');
        if (q >= 0) path = path[..q];
        var raw = Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
        return SafeFileName(Path.GetFileNameWithoutExtension(raw));
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_')) + ".jpg";

    // ══════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════

    private static int ParseHiddenSpanInt(IElement cell)
    {
        var span = cell.QuerySelector("span[style]");
        if (span == null) return 0;
        return int.TryParse(span.TextContent.Trim(), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int v) ? v : 0;
    }

    // Colonne "coût spécial" (td[1]) : une SEULE mécanique par ligne, portée par l'icône Tango de la
    // cellule (adrénaline pour Warrior/Paragon/Dervish, sacrifice pour Necro/Ritu, upkeep pour
    // Assassin/Monk, overcast pour Elementalist ; vide pour Mesmer/Ranger). La table PvE mélange les
    // mécaniques ligne à ligne, d'où la détection par cellule plutôt que par profession. La valeur
    // affichée est visible (hors span de tri masqué) ; l'upkeep wiki est négatif ("-1") et stocké en
    // magnitude positive (cf. commentaire Skill.Upkeep, "1 = -1 pip"). Cellule vide → tout à 0.
    private static void ParseSpecialCost(
        IElement cell, out int adrenaline, out int sacrifice, out int overcast, out int upkeep)
    {
        adrenaline = sacrifice = overcast = upkeep = 0;

        // Retire les spans masqués (clé de tri) pour ne lire que la valeur affichée.
        foreach (var hidden in cell.QuerySelectorAll("span[style*='display']").ToList())
            hidden.Remove();

        var m = Regex.Match(cell.TextContent, @"-?\d+");
        if (!m.Success || !int.TryParse(m.Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int value))
            return;

        // Mécanique = icône Tango restante dans la cellule (alt du <img> ou title du lien <a>).
        var key = (cell.QuerySelector("img")?.GetAttribute("alt")
                   ?? cell.QuerySelector("a")?.GetAttribute("title")
                   ?? string.Empty).ToLowerInvariant();

        if (key.Contains("adrenaline"))     adrenaline = value;
        else if (key.Contains("sacrifice")) sacrifice = value;
        else if (key.Contains("overcast"))  overcast = value;
        else if (key.Contains("upkeep"))    upkeep = Math.Abs(value);
    }

    private static float ParseHiddenSpanFloat(IElement cell)
    {
        var span = cell.QuerySelector("span[style]");
        if (span == null) return 0f;
        return float.TryParse(span.TextContent.Trim(), NumberStyles.Float,
            CultureInfo.InvariantCulture, out float v) ? v : 0f;
    }

    // Complète un skill orphelin (injecté sans stats en phase 4.5) depuis sa page individuelle.
    // Source : l'infobox "div.skill-box" — description = 1er <p> hors infobox ; chaque <li> de
    // div.skill-stats = une valeur + une icône Tango dont l'alt nomme la mécanique ; campagne dans
    // la <dl>. Une valeur non chiffrée (recharge "morale boost") est ignorée → reste à 0.
    private void EnrichFromSkillPage(IDocument doc, SkillEntity skill)
    {
        var descP = doc.QuerySelectorAll("#mw-content-text p")
            .FirstOrDefault(p => p.Closest("div.infobox") is null
                                 && !string.IsNullOrWhiteSpace(p.TextContent));
        if (descP is not null)
            skill.Description = descP.TextContent.Trim();

        foreach (var li in doc.QuerySelectorAll("div.skill-stats li"))
        {
            var stat = (li.QuerySelector("img")?.GetAttribute("alt") ?? string.Empty).ToLowerInvariant();
            var m = Regex.Match(li.TextContent, @"\d+(?:\.\d+)?");
            if (!m.Success) continue; // ex. recharge "morale boost" : pas de valeur chiffrée
            int i = int.TryParse(m.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv) ? iv : 0;
            float f = float.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv) ? fv : 0f;
            switch (stat)
            {
                case "energy":     skill.EnergyCost = i; break;
                case "adrenaline": skill.Adrenaline = i; break;
                case "sacrifice":  skill.Sacrifice  = i; break;
                case "overcast":   skill.Overcast   = i; break;
                case "upkeep":     skill.Upkeep     = Math.Abs(i); break;
                case "activation": skill.CastTime   = f; break;
                case "recharge":   skill.Recharge   = f; break;
            }
        }

        skill.SkillType = ExtractSkillType(skill.Description, isElite: false);
        var campaign = InfoboxDefinitionValue(doc, "Campaign");
        if (!string.IsNullOrEmpty(campaign))
            skill.Campaign = campaign;
    }

    // Valeur du <dd> suivant le <dt> d'intitulé `label` dans la <dl> de l'infobox (Type, Campaign…).
    private static string InfoboxDefinitionValue(IDocument doc, string label)
    {
        foreach (var dt in doc.QuerySelectorAll("div.infobox dl dt"))
            if (dt.TextContent.Trim().Equals(label, StringComparison.OrdinalIgnoreCase))
                return dt.NextElementSibling?.TextContent.Trim() ?? string.Empty;
        return string.Empty;
    }

    private static string ExtractSkillType(string description, bool isElite)
    {
        var dotIndex = description.IndexOf('.');
        if (dotIndex <= 0) return string.Empty;
        var firstPart = description[..dotIndex].Trim();
        if (firstPart.StartsWith("Elite ", StringComparison.OrdinalIgnoreCase))
            firstPart = firstPart[6..].Trim();
        return Regex.IsMatch(firstPart, @"^\d") ? string.Empty : firstPart;
    }
}
