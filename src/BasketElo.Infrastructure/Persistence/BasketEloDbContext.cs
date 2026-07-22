using BasketElo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Infrastructure.Persistence;

public class BasketEloDbContext(DbContextOptions<BasketEloDbContext> options) : DbContext(options)
{
    private static readonly Guid AdminRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
    public DbSet<ApplicationUserExternalLogin> ApplicationUserExternalLogins => Set<ApplicationUserExternalLogin>();
    public DbSet<ApplicationRole> ApplicationRoles => Set<ApplicationRole>();
    public DbSet<ApplicationUserRole> ApplicationUserRoles => Set<ApplicationUserRole>();
    public DbSet<ModelLabModel> ModelLabModels => Set<ModelLabModel>();
    public DbSet<ModelLabModelVersion> ModelLabModelVersions => Set<ModelLabModelVersion>();
    public DbSet<ModelLabRun> ModelLabRuns => Set<ModelLabRun>();
    public DbSet<ModelLabRunScope> ModelLabRunScopes => Set<ModelLabRunScope>();
    public DbSet<ModelLabRunPrediction> ModelLabRunPredictions => Set<ModelLabRunPrediction>();
    public DbSet<ModelLabRunRating> ModelLabRunRatings => Set<ModelLabRunRating>();
    public DbSet<ModelLabRunPeriodMetric> ModelLabRunPeriodMetrics => Set<ModelLabRunPeriodMetric>();
    public DbSet<ModelLabRunMetricBreakdown> ModelLabRunMetricBreakdowns => Set<ModelLabRunMetricBreakdown>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamAlias> TeamAliases => Set<TeamAlias>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<CompetitionAlias> CompetitionAliases => Set<CompetitionAlias>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<TeamRating> TeamRatings => Set<TeamRating>();
    public DbSet<RatingHistory> RatingHistories => Set<RatingHistory>();
    public DbSet<EloRebuildRun> EloRebuildRuns => Set<EloRebuildRun>();
    public DbSet<BackfillJob> BackfillJobs => Set<BackfillJob>();
    public DbSet<BackfillInspectionDecision> BackfillInspectionDecisions => Set<BackfillInspectionDecision>();
    public DbSet<IdentityHealthCheckRun> IdentityHealthCheckRuns => Set<IdentityHealthCheckRun>();
    public DbSet<IdentityHealthCheckFinding> IdentityHealthCheckFindings => Set<IdentityHealthCheckFinding>();
    public DbSet<IdentityReviewDecision> IdentityReviewDecisions => Set<IdentityReviewDecision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("application_users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(x => x.AvatarUrl).HasMaxLength(1000);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.LastLoginAtUtc).IsRequired();
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<ApplicationUserExternalLogin>(entity =>
        {
            entity.ToTable("application_user_external_logins");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ProviderUserId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.EmailAtLogin).HasMaxLength(320).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.LastLoginAtUtc).IsRequired();
            entity.HasOne(x => x.User)
                .WithMany(x => x.ExternalLogins)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.Provider, x.ProviderUserId }).IsUnique();
        });

        modelBuilder.Entity<ApplicationRole>(entity =>
        {
            entity.ToTable("application_roles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Key).IsUnique();
            entity.HasData(new ApplicationRole
            {
                Id = AdminRoleId,
                Key = ApplicationRoleKeys.Admin,
                Name = "Admin"
            });
        });

        modelBuilder.Entity<ApplicationUserRole>(entity =>
        {
            entity.ToTable("application_user_roles");
            entity.HasKey(x => new { x.UserId, x.RoleId });
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasOne(x => x.User)
                .WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Role)
                .WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ModelLabModel>(entity =>
        {
            entity.ToTable("model_lab_models");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.LeagueName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.IsArchived).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasOne(x => x.OwnerUser)
                .WithMany(x => x.ModelLabModels)
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OwnerUserId, x.IsArchived, x.UpdatedAtUtc });
            entity.HasIndex(x => new { x.OwnerUserId, x.Name });
            entity.HasIndex(x => new { x.OwnerUserId, x.LeagueName });
        });

        modelBuilder.Entity<ModelLabModelVersion>(entity =>
        {
            entity.ToTable("model_lab_model_versions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.VersionNumber).IsRequired();
            entity.Property(x => x.ParameterSchemaVersion).HasMaxLength(40).IsRequired();
            entity.Property(x => x.BaseRating).HasPrecision(8, 2).IsRequired();
            entity.Property(x => x.KFactor).IsRequired();
            entity.Property(x => x.HomeAdvantageElo).HasPrecision(8, 2).IsRequired();
            entity.Property(x => x.ProbabilityScale).HasPrecision(8, 2).IsRequired();
            entity.Property(x => x.UsesMarginAdjustment).IsRequired();
            entity.Property(x => x.PointsPerEloMargin).HasPrecision(8, 2);
            entity.Property(x => x.CompetitionWeight).HasPrecision(6, 4).IsRequired();
            entity.Property(x => x.MarginDampenerFactor).HasPrecision(6, 2).IsRequired();
            entity.Property(x => x.MaxMarginMultiplier).HasPrecision(6, 4).IsRequired();
            entity.Property(x => x.ExtensionDataJson).HasColumnType("jsonb");
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasOne(x => x.Model)
                .WithMany(x => x.Versions)
                .HasForeignKey(x => x.ModelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ModelId, x.VersionNumber }).IsUnique();
            entity.HasIndex(x => x.CreatedAtUtc);
        });

        modelBuilder.Entity<ModelLabRun>(entity =>
        {
            entity.ToTable("model_lab_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ModelName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.LeagueName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ScopeType).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.InitializationFromUtc).IsRequired();
            entity.Property(x => x.InitializationToUtc).IsRequired();
            entity.Property(x => x.InitializationGames).IsRequired();
            entity.Property(x => x.ScoredFromUtc).IsRequired();
            entity.Property(x => x.ScoredToUtc).IsRequired();
            entity.Property(x => x.ScoredGames).IsRequired();
            entity.Property(x => x.CorrectWinners).IsRequired();
            entity.Property(x => x.WinnerAccuracy).HasPrecision(6, 2).IsRequired();
            entity.Property(x => x.BrierScore).HasPrecision(8, 4).IsRequired();
            entity.Property(x => x.LogLoss).HasPrecision(8, 4).IsRequired();
            entity.Property(x => x.AverageMarginError).HasPrecision(10, 2).IsRequired();
            entity.Property(x => x.AveragePredictedHomeWinProbability).HasPrecision(6, 2).IsRequired();
            entity.Property(x => x.BaselineScoredGames).IsRequired();
            entity.Property(x => x.BaselineCorrectWinners).IsRequired();
            entity.Property(x => x.BaselineWinnerAccuracy).HasPrecision(6, 2).IsRequired();
            entity.Property(x => x.BaselineBrierScore).HasPrecision(8, 4).IsRequired();
            entity.Property(x => x.BaselineLogLoss).HasPrecision(8, 4).IsRequired();
            entity.Property(x => x.BaselineAverageMarginError).HasPrecision(10, 2).IsRequired();
            entity.Property(x => x.BaselineAveragePredictedHomeWinProbability).HasPrecision(6, 2).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.ErrorMessage).HasMaxLength(4000);
            entity.HasOne(x => x.OwnerUser)
                .WithMany(x => x.ModelLabRuns)
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Model)
                .WithMany(x => x.Runs)
                .HasForeignKey(x => x.ModelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ModelVersion)
                .WithMany(x => x.Runs)
                .HasForeignKey(x => x.ModelVersionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OwnerUserId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.OwnerUserId, x.Status, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.ModelId, x.CreatedAtUtc });
            entity.HasIndex(x => x.ModelVersionId);
        });

        modelBuilder.Entity<ModelLabRunScope>(entity =>
        {
            entity.ToTable("model_lab_run_scopes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CompetitionName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CountryCode).HasMaxLength(3);
            entity.HasOne(x => x.Run)
                .WithMany(x => x.Scopes)
                .HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Competition)
                .WithMany()
                .HasForeignKey(x => x.CompetitionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.RunId, x.CompetitionId }).IsUnique();
        });

        modelBuilder.Entity<ModelLabRunPrediction>(entity =>
        {
            entity.ToTable("model_lab_run_predictions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CompetitionName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Season).HasMaxLength(20).IsRequired();
            entity.Property(x => x.HomeTeamName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.AwayTeamName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PredictedHomeWinProbability).HasPrecision(6, 4).IsRequired();
            entity.Property(x => x.PredictedHomeMargin).HasPrecision(10, 2).IsRequired();
            entity.Property(x => x.ActualHomeMargin).HasPrecision(10, 2).IsRequired();
            entity.Property(x => x.MarginError).HasPrecision(10, 2).IsRequired();
            entity.HasOne(x => x.Run)
                .WithMany(x => x.Predictions)
                .HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.OwnerUser)
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Game)
                .WithMany()
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Competition)
                .WithMany()
                .HasForeignKey(x => x.CompetitionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.HomeTeam)
                .WithMany()
                .HasForeignKey(x => x.HomeTeamId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AwayTeam)
                .WithMany()
                .HasForeignKey(x => x.AwayTeamId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.RunId, x.GameId }).IsUnique();
            entity.HasIndex(x => new { x.RunId, x.GameDateTimeUtc });
            entity.HasIndex(x => new { x.RunId, x.CompetitionId, x.GameDateTimeUtc });
            entity.HasIndex(x => new { x.RunId, x.MarginError }).IsDescending(false, true);
            entity.HasIndex(x => new { x.OwnerUserId, x.RunId });
        });

        modelBuilder.Entity<ModelLabRunRating>(entity =>
        {
            entity.ToTable("model_lab_run_ratings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TeamName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Elo).HasPrecision(10, 2).IsRequired();
            entity.Property(x => x.RecentMovement).HasPrecision(10, 2).IsRequired();
            entity.HasOne(x => x.Run)
                .WithMany(x => x.Ratings)
                .HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.OwnerUser)
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Team)
                .WithMany()
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.RunId, x.TeamId }).IsUnique();
            entity.HasIndex(x => new { x.RunId, x.Rank });
            entity.HasIndex(x => new { x.RunId, x.Elo }).IsDescending(false, true);
            entity.HasIndex(x => new { x.OwnerUserId, x.RunId });
        });

        modelBuilder.Entity<ModelLabRunPeriodMetric>(entity =>
        {
            entity.ToTable("model_lab_run_period_metrics");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PeriodKey).HasMaxLength(20).IsRequired();
            entity.Property(x => x.WinnerAccuracy).HasPrecision(6, 2).IsRequired();
            entity.Property(x => x.AverageMarginError).HasPrecision(10, 2).IsRequired();
            entity.HasOne(x => x.Run)
                .WithMany(x => x.PeriodMetrics)
                .HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.OwnerUser)
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.RunId, x.PeriodKey }).IsUnique();
            entity.HasIndex(x => new { x.OwnerUserId, x.RunId });
        });

        modelBuilder.Entity<ModelLabRunMetricBreakdown>(entity =>
        {
            entity.ToTable("model_lab_run_metric_breakdowns");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SegmentType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.SegmentKey).HasMaxLength(220).IsRequired();
            entity.Property(x => x.Label).HasMaxLength(220).IsRequired();
            entity.Property(x => x.Season).HasMaxLength(20);
            entity.Property(x => x.ScoredGames).IsRequired();
            entity.Property(x => x.CorrectWinners).IsRequired();
            entity.Property(x => x.WinnerAccuracy).HasPrecision(6, 2).IsRequired();
            entity.Property(x => x.BrierScore).HasPrecision(8, 4).IsRequired();
            entity.Property(x => x.LogLoss).HasPrecision(8, 4).IsRequired();
            entity.Property(x => x.AverageMarginError).HasPrecision(10, 2).IsRequired();
            entity.Property(x => x.AveragePredictedHomeWinProbability).HasPrecision(6, 2).IsRequired();
            entity.Property(x => x.BaselineScoredGames).IsRequired();
            entity.Property(x => x.BaselineCorrectWinners).IsRequired();
            entity.Property(x => x.BaselineWinnerAccuracy).HasPrecision(6, 2).IsRequired();
            entity.Property(x => x.BaselineBrierScore).HasPrecision(8, 4).IsRequired();
            entity.Property(x => x.BaselineLogLoss).HasPrecision(8, 4).IsRequired();
            entity.Property(x => x.BaselineAverageMarginError).HasPrecision(10, 2).IsRequired();
            entity.Property(x => x.BaselineAveragePredictedHomeWinProbability).HasPrecision(6, 2).IsRequired();
            entity.HasOne(x => x.Run)
                .WithMany(x => x.MetricBreakdowns)
                .HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.OwnerUser)
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Competition)
                .WithMany()
                .HasForeignKey(x => x.CompetitionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.RunId, x.SegmentType, x.SegmentKey }).IsUnique();
            entity.HasIndex(x => new { x.OwnerUserId, x.RunId });
            entity.HasIndex(x => new { x.RunId, x.SegmentType });
            entity.HasIndex(x => new { x.RunId, x.CompetitionId });
            entity.HasIndex(x => new { x.RunId, x.Season });
        });

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
            entity.HasIndex(x => new { x.Source, x.SourceTeamId });
            entity.HasIndex(x => new { x.Source, x.SourceTeamId, x.AliasName }).IsUnique();
            entity.HasIndex(x => new { x.TeamId, x.AliasName });
        });

        modelBuilder.Entity<Competition>(entity =>
        {
            entity.ToTable("competitions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(50).IsRequired();
            entity.Property(x => x.EloPoolKey).HasMaxLength(30);
            entity.Property(x => x.CountryCode).HasMaxLength(3);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => new { x.Name, x.CountryCode }).IsUnique();
            entity.HasIndex(x => x.EloPoolKey);
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
            entity.Property(x => x.SourceUrl).HasMaxLength(1000);
            entity.Property(x => x.SourceSeasonKey).HasMaxLength(100);
            entity.Property(x => x.SourceRevision).HasMaxLength(100);
            entity.Property(x => x.ParserVersion).HasMaxLength(100);
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CompetitionPhase).HasMaxLength(100);
            entity.Property(x => x.CompetitionRound).HasMaxLength(100);
            entity.Property(x => x.EloExclusionReason).HasMaxLength(200);
            entity.Property(x => x.HasManualResultOverride).IsRequired();
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
            entity.HasKey(x => new { x.EloPoolKey, x.TeamId, x.RulesetVersion });
            entity.Property(x => x.EloPoolKey).HasMaxLength(30).IsRequired();
            entity.Property(x => x.RulesetVersion).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Elo).HasPrecision(8, 2).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasOne(x => x.Team)
                .WithMany()
                .HasForeignKey(x => x.TeamId)
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
            entity.Property(x => x.EloPoolKey).HasMaxLength(30).IsRequired();
            entity.Property(x => x.RulesetVersion).HasMaxLength(50).IsRequired();
            entity.Property(x => x.GameDateTimeUtc).IsRequired();
            entity.Property(x => x.PreElo).HasPrecision(8, 2).IsRequired();
            entity.Property(x => x.PostElo).HasPrecision(8, 2).IsRequired();
            entity.Property(x => x.EloDelta).HasPrecision(8, 2).IsRequired();
            entity.Property(x => x.ExpectedScore).HasPrecision(6, 4).IsRequired();
            entity.Property(x => x.ActualScore).HasPrecision(4, 2).IsRequired();
            entity.Property(x => x.MarginMultiplier).HasPrecision(6, 4).IsRequired();
            entity.Property(x => x.CompetitionWeight).HasPrecision(6, 4).IsRequired();
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
            entity.HasIndex(x => new { x.EloPoolKey, x.TeamId, x.RulesetVersion, x.GameDateTimeUtc });
            entity.HasIndex(x => new { x.EloPoolKey, x.GameId, x.TeamId, x.RulesetVersion }).IsUnique();
        });

        modelBuilder.Entity<EloRebuildRun>(entity =>
        {
            entity.ToTable("elo_rebuild_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.QueuedAtUtc).IsRequired();
            entity.Property(x => x.EloPoolKey).HasMaxLength(30);
            entity.Property(x => x.RulesetVersion).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CompetitionName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.GamesProcessed).IsRequired();
            entity.Property(x => x.TeamsRated).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => x.QueuedAtUtc);
            entity.HasIndex(x => new { x.EloPoolKey, x.RulesetVersion })
                .IsUnique()
                .HasFilter("\"Status\" IN ('pending', 'running') AND \"EloPoolKey\" IS NOT NULL");
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

        modelBuilder.Entity<BackfillInspectionDecision>(entity =>
        {
            entity.ToTable("backfill_inspection_decisions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Country).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LeagueName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Season).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(1000);
            entity.Property(x => x.ReviewedBy).HasMaxLength(200);
            entity.Property(x => x.ReviewedAtUtc).IsRequired();
            entity.HasIndex(x => new { x.Provider, x.Country, x.LeagueName, x.Season }).IsUnique();
            entity.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<IdentityHealthCheckRun>(entity =>
        {
            entity.ToTable("identity_health_check_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Source).HasMaxLength(50);
            entity.Property(x => x.Season).HasMaxLength(20);
            entity.Property(x => x.CountryCode).HasMaxLength(3);
            entity.Property(x => x.ScopeKey).HasMaxLength(300).IsRequired();
            entity.Property(x => x.RulesVersion).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CheckedAtUtc).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasOne(x => x.Competition)
                .WithMany()
                .HasForeignKey(x => x.CompetitionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => x.ScopeKey);
            entity.HasIndex(x => new { x.Status, x.CheckedAtUtc });
        });

        modelBuilder.Entity<IdentityHealthCheckFinding>(entity =>
        {
            entity.ToTable("identity_health_check_findings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FindingType).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Severity).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(50);
            entity.Property(x => x.SourceTeamId).HasMaxLength(100);
            entity.Property(x => x.RelatedSource).HasMaxLength(50);
            entity.Property(x => x.RelatedSourceTeamId).HasMaxLength(100);
            entity.Property(x => x.Season).HasMaxLength(20);
            entity.Property(x => x.CountryCode).HasMaxLength(3);
            entity.Property(x => x.Evidence).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.SuggestedAction).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.ResolutionAction).HasMaxLength(60);
            entity.Property(x => x.ResolvedBy).HasMaxLength(200);
            entity.Property(x => x.ResolutionNote).HasMaxLength(1000);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasOne(x => x.Run)
                .WithMany(x => x.Findings)
                .HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.AffectedTeam)
                .WithMany()
                .HasForeignKey(x => x.AffectedTeamId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.RelatedTeam)
                .WithMany()
                .HasForeignKey(x => x.RelatedTeamId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.Competition)
                .WithMany()
                .HasForeignKey(x => x.CompetitionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.Status, x.Severity });
            entity.HasIndex(x => new { x.Source, x.SourceTeamId });
            entity.HasIndex(x => new { x.Season, x.CountryCode });
        });

        modelBuilder.Entity<IdentityReviewDecision>(entity =>
        {
            entity.ToTable("identity_review_decisions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DecisionKey).HasMaxLength(500).IsRequired();
            entity.Property(x => x.FindingType).HasMaxLength(60).IsRequired();
            entity.Property(x => x.ResolutionAction).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(50);
            entity.Property(x => x.SourceTeamId).HasMaxLength(100);
            entity.Property(x => x.RelatedSource).HasMaxLength(50);
            entity.Property(x => x.RelatedSourceTeamId).HasMaxLength(100);
            entity.Property(x => x.Note).HasMaxLength(1000);
            entity.Property(x => x.CreatedBy).HasMaxLength(200);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasOne(x => x.AffectedTeam)
                .WithMany()
                .HasForeignKey(x => x.AffectedTeamId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(x => x.RelatedTeam)
                .WithMany()
                .HasForeignKey(x => x.RelatedTeamId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => x.DecisionKey).IsUnique();
        });
    }
}
