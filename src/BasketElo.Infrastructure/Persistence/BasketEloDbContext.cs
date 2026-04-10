using BasketElo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Infrastructure.Persistence;

public class BasketEloDbContext(DbContextOptions<BasketEloDbContext> options) : DbContext(options)
{
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamAlias> TeamAliases => Set<TeamAlias>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<CompetitionAlias> CompetitionAliases => Set<CompetitionAlias>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<TeamRating> TeamRatings => Set<TeamRating>();
    public DbSet<RatingHistory> RatingHistories => Set<RatingHistory>();
    public DbSet<RankingSnapshot> RankingSnapshots => Set<RankingSnapshot>();
    public DbSet<EloRebuildRun> EloRebuildRuns => Set<EloRebuildRun>();
    public DbSet<BackfillJob> BackfillJobs => Set<BackfillJob>();

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
            entity.HasIndex(x => x.CanonicalName);
        });

        modelBuilder.Entity<TeamAlias>(entity =>
        {
            entity.ToTable("team_aliases");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Source).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SourceTeamId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.AliasName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasOne(x => x.Team)
                .WithMany(x => x.Aliases)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.Source, x.SourceTeamId }).IsUnique();
            entity.HasIndex(x => new { x.TeamId, x.AliasName });
        });

        modelBuilder.Entity<Competition>(entity =>
        {
            entity.ToTable("competitions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(50).IsRequired();
            entity.Property(x => x.CountryCode).HasMaxLength(3);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => new { x.Name, x.CountryCode }).IsUnique();
        });

        modelBuilder.Entity<CompetitionAlias>(entity =>
        {
            entity.ToTable("competition_aliases");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Source).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SourceCompetitionId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.AliasName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasOne(x => x.Competition)
                .WithMany(x => x.Aliases)
                .HasForeignKey(x => x.CompetitionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.Source, x.SourceCompetitionId }).IsUnique();
            entity.HasIndex(x => new { x.CompetitionId, x.AliasName });
        });

        modelBuilder.Entity<Season>(entity =>
        {
            entity.ToTable("seasons");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Label).HasMaxLength(20).IsRequired();
            entity.Property(x => x.StartDateUtc).IsRequired();
            entity.Property(x => x.EndDateUtc).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasOne(x => x.Competition)
                .WithMany()
                .HasForeignKey(x => x.CompetitionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CompetitionId, x.Label }).IsUnique();
        });

        modelBuilder.Entity<Game>(entity =>
        {
            entity.ToTable("games");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Source).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SourceGameId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.GameDateTimeUtc).IsRequired();
            entity.Property(x => x.IngestedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasOne(x => x.Competition)
                .WithMany()
                .HasForeignKey(x => x.CompetitionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Season)
                .WithMany()
                .HasForeignKey(x => x.SeasonId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.HomeTeam)
                .WithMany()
                .HasForeignKey(x => x.HomeTeamId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AwayTeam)
                .WithMany()
                .HasForeignKey(x => x.AwayTeamId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.Source, x.SourceGameId }).IsUnique();
            entity.HasIndex(x => x.GameDateTimeUtc);
            entity.HasIndex(x => new { x.CompetitionId, x.GameDateTimeUtc });
        });

        modelBuilder.Entity<TeamRating>(entity =>
        {
            entity.ToTable("team_ratings");
            entity.HasKey(x => x.TeamId);
            entity.Property(x => x.Elo).HasPrecision(8, 2).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasOne(x => x.Team)
                .WithOne()
                .HasForeignKey<TeamRating>(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.LastGame)
                .WithMany()
                .HasForeignKey(x => x.LastGameId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RatingHistory>(entity =>
        {
            entity.ToTable("rating_history");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.GameDateTimeUtc).IsRequired();
            entity.Property(x => x.PreElo).HasPrecision(8, 2).IsRequired();
            entity.Property(x => x.PostElo).HasPrecision(8, 2).IsRequired();
            entity.Property(x => x.EloDelta).HasPrecision(8, 2).IsRequired();
            entity.Property(x => x.ExpectedScore).HasPrecision(6, 4).IsRequired();
            entity.Property(x => x.ActualScore).HasPrecision(4, 2).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasOne(x => x.Game)
                .WithMany()
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Team)
                .WithMany()
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.OpponentTeam)
                .WithMany()
                .HasForeignKey(x => x.OpponentTeamId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.TeamId, x.GameDateTimeUtc });
            entity.HasIndex(x => new { x.GameId, x.TeamId }).IsUnique();
        });

        modelBuilder.Entity<RankingSnapshot>(entity =>
        {
            entity.ToTable("ranking_snapshots");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SnapshotDate).IsRequired();
            entity.Property(x => x.Elo).HasPrecision(8, 2).IsRequired();
            entity.Property(x => x.Position).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasOne(x => x.Team)
                .WithMany()
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.SnapshotDate, x.Position });
            entity.HasIndex(x => new { x.SnapshotDate, x.TeamId }).IsUnique();
        });

        modelBuilder.Entity<EloRebuildRun>(entity =>
        {
            entity.ToTable("elo_rebuild_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.StartedAtUtc).IsRequired();
            entity.Property(x => x.RulesetVersion).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => x.StartedAtUtc);
        });

        modelBuilder.Entity<BackfillJob>(entity =>
        {
            entity.ToTable("backfill_jobs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Country).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LeagueName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Season).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(40).IsRequired();
            entity.Property(x => x.SummaryJson).HasColumnType("jsonb");
            entity.Property(x => x.ErrorMessage).HasMaxLength(4000);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        });
    }
}
