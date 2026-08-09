using Microsoft.EntityFrameworkCore;
using ZCodex.Data.Entities;

namespace ZCodex.Data;

public class AppDbContext : DbContext
{
    public DbSet<SkillEntity> Skills => Set<SkillEntity>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SkillEntity>()
            .HasIndex(s => s.Name);

        modelBuilder.Entity<SkillEntity>()
            .HasIndex(s => s.ProfessionId);
    }
}
