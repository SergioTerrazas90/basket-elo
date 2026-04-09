using BasketElo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Infrastructure.Persistence;

public class BasketEloDbContext(DbContextOptions<BasketEloDbContext> options) : DbContext(options)
{
    public DbSet<Team> Teams => Set<Team>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Team>(entity =>
        {
            entity.ToTable("teams");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CanonicalName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CountryCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.IsActive).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
        });
    }
}
