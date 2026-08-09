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
