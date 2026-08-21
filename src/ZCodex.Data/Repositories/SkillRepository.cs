using Microsoft.EntityFrameworkCore;
using ZCodex.Data.Entities;

namespace ZCodex.Data.Repositories;

public class SkillRepository(AppDbContext db)
{
    public async Task<List<SkillEntity>> GetAllAsync()
        => await db.Skills.AsNoTracking().ToListAsync();

    public async Task<int> CountAsync()
        => await db.Skills.CountAsync();

    public async Task<List<SkillEntity>> GetByProfessionAsync(int professionId)
        => await db.Skills.AsNoTracking()
            .Where(s => s.ProfessionId == professionId)
            .ToListAsync();

    // Recalcule la colonne Mechanics de toute la table à partir de ce que la base sait déjà (type,
    // upkeep, sacrifice, conditions scrapées) — aucun accès réseau. Les clés qui ne sont PAS
    // calculables (aucune à ce jour) sont préservées telles quelles.
    // Renvoie le nombre de compétences dont la valeur a changé.
    public async Task<int> RecomputeMechanicsAsync()
    {
        var entities = await db.Skills.ToListAsync();
        int changed = 0;
        foreach (var e in entities)
        {
            // Sonde : seuls les champs que Compute regarde sont renseignés. Name et Description en
            // font partie depuis l'aire d'effet — c'est le TEXTE de la description qui porte les
            // portées (« adjacent », « nearby », « in the area », « party »).
            var probe = new Core.Models.Skill
            {
                Name = e.Name,
                Description = e.Description,
                SkillType = e.SkillType,
                Upkeep = e.Upkeep,
                Sacrifice = e.Sacrifice,
                // ⚠ Ajoutés le 21/08/2026 avec les mécaniques Adrénaline et Épuisement : elles
                // se calculent depuis ces COLONNES, pas depuis le texte. Sans elles ici, la
                // sonde vaut 0 et « Recalculer les catégories » effacerait les deux entrées.
                Adrenaline = e.Adrenaline,
                Overcast = e.Overcast,
                Conditions = Core.Models.SkillCategoryData.ParseCsv(e.Conditions),
            };
            var csv = string.Join(",",
                Core.Models.SkillCategoryData.Merge(
                    Core.Models.SkillCategoryData.ParseCsv(e.Mechanics), probe));
            if (e.Mechanics == csv) continue;
            e.Mechanics = csv;
            changed++;
        }
        if (changed > 0) await db.SaveChangesAsync();
        return changed;
    }

    // Remplace toute la table Skills : supprime l'existant, déduplique par ID, réinsère.
    public async Task ReplaceAllAsync(IEnumerable<SkillEntity> skills)
    {
        // Déduplique par ID avant toute opération DB
        // En cas de conflit : préfère le skill avec profession connue (non None)
        var unique = skills
            .GroupBy(s => s.Id)
            .Select(g => g.OrderBy(s => s.ProfessionId == (int)Core.Models.Profession.None ? 1 : 0).First())
            .ToList();

        // EF Core 8 bulk delete (plus rapide que ExecuteSqlRawAsync)
        await db.Skills.ExecuteDeleteAsync();

        // Insert dans une transaction explicite → 1 seul flush disque pour 1500 entités
        using var tx = await db.Database.BeginTransactionAsync();
        db.Skills.AddRange(unique);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    [Obsolete("Utiliser ReplaceAllAsync pour garantir les IDs GW1 corrects.")]
    public async Task UpsertBatchAsync(IEnumerable<SkillEntity> skills)
    {
        foreach (var skill in skills)
        {
            var existing = await db.Skills.FindAsync(skill.Id);
            if (existing == null)
                db.Skills.Add(skill);
            else
            {
                existing.Name        = skill.Name;
                existing.ProfessionId = skill.ProfessionId;
                existing.Attribute   = skill.Attribute;
                existing.Description = skill.Description;
                existing.EnergyCost  = skill.EnergyCost;
                existing.Adrenaline  = skill.Adrenaline;
                existing.Sacrifice   = skill.Sacrifice;
                existing.Overcast    = skill.Overcast;
                existing.Upkeep      = skill.Upkeep;
                existing.CastTime    = skill.CastTime;
                existing.Recharge    = skill.Recharge;
                existing.SkillType   = skill.SkillType;
                existing.Campaign    = skill.Campaign;
                existing.Progression = skill.Progression;
                existing.IconUrl     = skill.IconUrl;
                existing.IconUrlHd   = skill.IconUrlHd;
                existing.WikiUrl     = skill.WikiUrl;
                existing.UpdatedAt   = DateTime.UtcNow;
            }
        }
        await db.SaveChangesAsync();
    }
}
