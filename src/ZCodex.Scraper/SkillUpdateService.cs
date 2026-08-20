using Microsoft.Extensions.Logging;
using ZCodex.Core.Models;
using ZCodex.Core.Search;
using ZCodex.Data;
using ZCodex.Data.Entities;
using ZCodex.Data.Repositories;
using System.Text.Json;

namespace ZCodex.Scraper;

public class SkillUpdateService(WikiSkillScraper scraper, AppDbContext db, ILogger<SkillUpdateService> logger)
{
    // 10 stats + 10 galeries + 1 PvE + 1 skill ID list + 10 pages de conditions
    private const int ScrapePageSteps = 32;

    // Clés d'identité des compétences sans id GW1 propre (max réel ~3431), hors de toute plage du
    // jeu. Variante « (PvP) » → 1000000 + id de sa base ; orphelines → plage 900000+.
    private const int PvpVariantKeyBase = 1_000_000;
    private const int OrphanKeyBase     =   900_000;

    // Les messages de progression remontent tels quels dans SkillUpdateWindow : ils sont donc
    // de l'UI, et suivent la langue courante (les logs, eux, restent en français).
    private static string L(string fr, string en) => AppLanguage.IsFr ? fr : en;

    public async Task<int> UpdateSkillsAsync(
        IProgress<(int done, int total, string current)>? progress = null,
        CancellationToken ct = default)
    {
        logger.LogInformation("Starting skill update from wiki");

        // ── Phase 1 : scraping des pages (21 étapes) ─────────────────────
        // On ne connaît pas encore le total final, on utilise une estimation
        const int estimatedIcons = 1500;
        int estimatedTotal = ScrapePageSteps + estimatedIcons + 2;

        var scrapeProgress = new Progress<(int done, int total, string current)>(p =>
            progress?.Report((p.done, estimatedTotal, p.current)));

        // Le scraper rappelle SaveAsync une fois le catalogue de base constitué, avant les phases
        // longues : une coupure au-delà de ce point laisse une application utilisable.
        var skills = await scraper.ScrapeAllAsync(scrapeProgress, ct,
            onBaseSkillsReady: async partial => await SaveAsync(partial, complete: false));

        if (skills.Count == 0)
        {
            logger.LogWarning("No skills scraped — aborting save");
            return 0;
        }

        // Total réel maintenant connu
        int realTotal = ScrapePageSteps + skills.Count + 2;

        // ── Phase 1 bis : conditions infligées (10 pages « Skills that cause X ») ──
        var conditionProgress = new Progress<(int done, int total, string current)>(p =>
            progress?.Report((22 + p.done, realTotal, p.current)));

        var conditionsByName = await scraper.ScrapeConditionsAsync(conditionProgress, ct);
        int conditionsApplied = 0;
        foreach (var s in skills)
        {
            // Jointure par nom exact (apostrophe normalisée des deux côtés, comme le scraper).
            // Repli allégeance : les pages de condition listent le nom de BASE (ex : « Shadow
            // Sanctuary ») alors qu'on ne garde que les variantes (Kurzick)/(Luxon).
            var key = s.Name.Replace('’', '\'');
            if (!conditionsByName.TryGetValue(key, out var entries))
            {
                if (key.EndsWith(" (Kurzick)", StringComparison.OrdinalIgnoreCase))
                    conditionsByName.TryGetValue(key[..^10], out entries);
                else if (key.EndsWith(" (Luxon)", StringComparison.OrdinalIgnoreCase))
                    conditionsByName.TryGetValue(key[..^8], out entries);
            }
            if (entries != null)
            {
                s.Conditions = string.Join(",", entries);
                conditionsApplied++;
            }
        }
        logger.LogInformation("Conditions appliquées à {Count} skills", conditionsApplied);

        // ── Phase 1 quater : méta-catégories dérivables (colonne Mechanics) ──
        // Ici et pas ailleurs : la passe conditions vient de finir, et Compute en dépend. Sans
        // cette ligne, une mise à jour du catalogue (qui réécrit toute la table) laisserait la
        // colonne vide jusqu'au prochain « Recalculer les catégories ».
        int mechanicsApplied = 0;
        foreach (var s in skills)
        {
            var probe = new Core.Models.Skill
            {
                Name = s.Name,
                Description = s.Description,
                SkillType = s.SkillType,
                Upkeep = s.Upkeep,
                Sacrifice = s.Sacrifice,
                Conditions = Core.Models.SkillCategoryData.ParseCsv(s.Conditions),
            };
            s.Mechanics = string.Join(",", Core.Models.SkillCategoryData.Compute(probe));
            if (s.Mechanics.Length > 0) mechanicsApplied++;
        }
        logger.LogInformation("Mécaniques calculées pour {Count} skills", mechanicsApplied);

        // ── Phase 1 ter : progressions saisies à la main (pages sans table standard) ──
        // Repli : les rares skills dont le wiki n'expose pas de table skill-progression
        // (Rising Bile…) gardaient une progression vide → plages non résolues. On ne remplit
        // que les trous, donc aucun effet si le wiki finit par fournir la table. SkillEntity
        // stocke la progression en JSON (comme la passe scraping) → on sérialise l'override.
        int manualProg = 0;
        foreach (var s in skills)
            if (string.IsNullOrEmpty(s.Progression)
                && ManualProgression.ByName.TryGetValue(s.Name, out var prog))
            {
                s.Progression = JsonSerializer.Serialize(prog);
                manualProg++;
            }
        if (manualProg > 0) logger.LogInformation("Progressions manuelles appliquées : {Count}", manualProg);

        // ── Phase 2 : téléchargement des icônes ───────────────────────────
        var iconProgress = new Progress<(int done, int total, string current)>(p =>
            progress?.Report((ScrapePageSteps + p.done, realTotal,
                L($"Icônes ({p.done}/{p.total}) : {p.current}",
                  $"Icons ({p.done}/{p.total}): {p.current}"))));

        await scraper.DownloadIconsAsync(skills, iconProgress, ct);

        // ── Phase 3 : sauvegarde définitive ───────────────────────────────
        progress?.Report((realTotal - 1, realTotal, L("Sauvegarde en base de données…", "Saving to database…")));
        await Task.Yield(); // laisse l'UI afficher la progression avant l'écriture DB

        skills = await SaveAsync(skills, complete: true);

        progress?.Report((realTotal, realTotal, L("Terminé", "Done")));
        logger.LogInformation("Saved {Count} skills to database", skills.Count);
        return skills.Count;

        // Filtre, attribue les clés d'identité et écrit en base. Appelé DEUX fois : sur le
        // catalogue de base (complete: false), puis en fin de scrapping. Le second appel ne
        // réattribue aucune clé — les entités sont les mêmes objets, déjà renseignés.
        async Task<List<SkillEntity>> SaveAsync(List<SkillEntity> toSave, bool complete)
        {
            // Exclure les skills inutilisables en build (versions missions/PNJ temporaires).
            toSave = toSave.Where(s => !SkillCatalogFilter.IsBuildUnusable(s.Name)).ToList();
            AssignIdentityKeys(toSave);
            await new SkillRepository(db).ReplaceAllAsync(toSave);
            new ScrapeInfo
            {
                LastScrapeDate = DateTime.UtcNow,
                SkillCount = toSave.Count,
                Complete = complete,
            }.Save();
            return toSave;
        }
    }

    // Les skills matchés ont déjà leur vrai ID GW1 (assigné dans ScrapeAllAsync). Les autres
    // ont besoin d'une CLÉ D'IDENTITÉ (pas d'un id de template : l'encodage passe par
    // SkillVariants.TemplateIdsByName, qui résout vers la version de base).
    //
    // Cette clé doit être STABLE d'un scrapping à l'autre : l'ancien compteur « 5000++ »
    // suivait l'ordre de scrapping, si bien qu'une compétence non matchée qui apparaît ou
    // disparaît décalait toutes les suivantes — et les .pn3 déjà enregistrés se mettaient à
    // désigner la mauvaise compétence.
    //   • variante « (PvP) » d'une compétence connue → dérivée de la base : totalement stable ;
    //   • autre cas → compteur, mais sur un ordre ALPHABÉTIQUE et non de scrapping.
    private static void AssignIdentityKeys(List<SkillEntity> skills)
    {
        var idsByName = skills.Where(s => s.Id != 0)
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        int orphanId = OrphanKeyBase;
        foreach (var s in skills.Where(s => s.Id == 0).OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            var baseName = SkillVariants.BaseName(s.Name);
            s.Id = SkillVariants.IsPvpVariant(s.Name) && idsByName.TryGetValue(baseName, out int baseId)
                ? PvpVariantKeyBase + baseId
                : orphanId++;
        }
    }
}
