using BasketElo.Api.Auth;
using BasketElo.Domain.Admin;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/admin")]
[RequireInternalAdmin]
public class AdminController(BasketEloDbContext dbContext) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<AdminDashboardResponse>> GetDashboard(
        [FromQuery] string? rulesetVersion,
        CancellationToken cancellationToken)
    {
        var selectedRuleset = ResolveRulesetOrDefault(rulesetVersion);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }

        var databaseCanConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        var pendingMigrations = databaseCanConnect
            ? await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)
            : [];

        var completedGamesQuery = dbContext.Games
            .AsNoTracking()
            .Where(x => x.HomeScore.HasValue && x.AwayScore.HasValue && x.HomeScore != x.AwayScore);

        var completedGames = await completedGamesQuery.CountAsync(cancellationToken);
        var unratedCompletedGames = await completedGamesQuery.CountAsync(
            x => !dbContext.RatingHistories.Any(history =>
                history.GameId == x.Id &&
                history.RulesetVersion == selectedRuleset),
            cancellationToken);

        var latestSuccessfulRebuildUtc = await dbContext.EloRebuildRuns
            .AsNoTracking()
            .Where(x =>
                x.RulesetVersion == selectedRuleset &&
                x.Status == EloRebuildRunStatus.Completed)
            .MaxAsync(x => x.FinishedAtUtc, cancellationToken);

        var recentRuns = await dbContext.EloRebuildRuns
            .AsNoTracking()
            .OrderByDescending(x => x.QueuedAtUtc)
            .Take(10)
            .Select(x => new EloRebuildRunDto(
                x.Id,
                x.RulesetVersion,
                x.Status,
                x.GamesProcessed,
                x.TeamsRated,
                x.QueuedAtUtc,
                x.StartedAtUtc,
                x.FinishedAtUtc,
                x.FromGameDateTimeUtc,
                x.Notes))
            .ToListAsync(cancellationToken);

        var elo = new EloDashboardResponse(
            new EloRulesetCatalogResponse(EloRulesetVersions.Default, EloRulesetVersions.All),
            new EloDashboardSummary(
                selectedRuleset,
                completedGames,
                unratedCompletedGames,
                await dbContext.TeamRatings
                    .AsNoTracking()
                    .CountAsync(x => x.RulesetVersion == selectedRuleset, cancellationToken),
                await completedGamesQuery.MaxAsync(x => (DateTime?)x.GameDateTimeUtc, cancellationToken),
                latestSuccessfulRebuildUtc,
                await dbContext.EloRebuildRuns
                    .AsNoTracking()
                    .Where(x => x.RulesetVersion == selectedRuleset)
                    .MaxAsync(x => (DateTime?)x.QueuedAtUtc, cancellationToken)),
            recentRuns);

        var recentBackfillJobs = await dbContext.BackfillJobs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(12)
            .Select(x => new AdminBackfillJobRow(
                x.Id,
                x.Provider,
                x.Country,
                x.LeagueName,
                x.Season,
                x.DryRun,
                x.Status,
                x.RequestsUsed,
                x.WarningCount,
                x.CreatedAtUtc,
                x.StartedAtUtc,
                x.FinishedAtUtc,
                x.SummaryJson,
                x.ErrorMessage))
            .ToListAsync(cancellationToken);

        var ratedGameIds = (await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.RulesetVersion == selectedRuleset)
            .Select(x => x.GameId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var gameCoverageRows = await dbContext.Games
            .AsNoTracking()
            .Select(x => new
            {
                x.Id,
                x.HomeScore,
                x.AwayScore,
                x.GameDateTimeUtc,
                CompetitionName = x.Competition.Name,
                SeasonLabel = x.Season.Label,
                x.Competition.CountryCode
            })
            .ToListAsync(cancellationToken);

        var gameCoverage = gameCoverageRows
            .GroupBy(x => new { x.CompetitionName, x.SeasonLabel, x.CountryCode })
            .Select(group =>
            {
                var completedGroupGames = group
                    .Where(x => x.HomeScore.HasValue && x.AwayScore.HasValue && x.HomeScore != x.AwayScore)
                    .ToList();

                return new AdminGameCoverageRow(
                    group.Key.CompetitionName,
                    group.Key.SeasonLabel,
                    group.Key.CountryCode ?? string.Empty,
                    group.Count(),
                    completedGroupGames.Count,
                    completedGroupGames.Count(x => !ratedGameIds.Contains(x.Id)),
                    group.Max(x => (DateTime?)x.GameDateTimeUtc));
            })
            .OrderByDescending(x => x.LatestGameUtc)
            .ThenBy(x => x.Competition)
            .Take(25)
            .ToList();

        var teamsMissingCountry = await dbContext.Teams
            .AsNoTracking()
            .CountAsync(x => x.CountryCode == "" || x.CountryCode == "UNK", cancellationToken);

        var aliasRows = await dbContext.TeamAliases
            .AsNoTracking()
            .Select(x => new
            {
                NormalizedName = x.AliasName.ToUpper(),
                x.Team.CountryCode,
                x.TeamId
            })
            .ToListAsync(cancellationToken);

        var possibleDuplicateAliasGroups = aliasRows
            .GroupBy(x => new { x.NormalizedName, x.CountryCode })
            .Count(group => group.Select(x => x.TeamId).Distinct().Skip(1).Any());

        var openIdentityWarnings = await dbContext.IdentityHealthCheckFindings
            .AsNoTracking()
            .CountAsync(x => x.Status == "open" && x.Severity == "warning", cancellationToken);

        var openIdentityBlockers = await dbContext.IdentityHealthCheckFindings
            .AsNoTracking()
            .CountAsync(x => x.Status == "open" && x.Severity == "blocker", cancellationToken);

        var latestBackfillActivityUtc = recentBackfillJobs
            .Select(x => x.FinishedAtUtc ?? x.StartedAtUtc ?? x.CreatedAtUtc)
            .DefaultIfEmpty()
            .Max();
        var latestRebuildActivityUtc = recentRuns
            .Select(x => x.FinishedAtUtc ?? x.StartedAtUtc ?? x.QueuedAtUtc)
            .DefaultIfEmpty()
            .Max();
        var latestWorkerActivityUtc = new[] { latestBackfillActivityUtc, latestRebuildActivityUtc }
            .Where(x => x != default)
            .DefaultIfEmpty()
            .Max();

        var system = new AdminSystemStatus(
            databaseCanConnect,
            pendingMigrations.Count(),
            DateTime.UtcNow,
            ResolveWorkerStatus(recentBackfillJobs, recentRuns),
            latestWorkerActivityUtc == default ? null : latestWorkerActivityUtc);

        var dataHealth = new AdminDataHealthSummary(
            completedGames,
            unratedCompletedGames,
            teamsMissingCountry,
            possibleDuplicateAliasGroups,
            openIdentityWarnings,
            openIdentityBlockers,
            latestSuccessfulRebuildUtc);

        return Ok(new AdminDashboardResponse(
            elo,
            system,
            recentBackfillJobs,
            gameCoverage,
            dataHealth));
    }

    private static string? ResolveRulesetOrDefault(string? rulesetVersion)
    {
        if (string.IsNullOrWhiteSpace(rulesetVersion))
        {
            return EloRulesetVersions.Default;
        }

        var normalized = rulesetVersion.Trim().ToLowerInvariant();
        return EloRulesetVersions.All.Contains(normalized) ? normalized : null;
    }

    private static string ResolveWorkerStatus(
        IReadOnlyCollection<AdminBackfillJobRow> backfillJobs,
        IReadOnlyCollection<EloRebuildRunDto> rebuildRuns)
    {
        if (backfillJobs.Any(x => x.Status == BackfillJobStatus.Running) ||
            rebuildRuns.Any(x => x.Status == EloRebuildRunStatus.Running))
        {
            return "running";
        }

        if (backfillJobs.Any(x => x.Status == BackfillJobStatus.Pending) ||
            rebuildRuns.Any(x => x.Status == EloRebuildRunStatus.Pending))
        {
            return "pending";
        }

        if (backfillJobs.Any(x => x.Status == BackfillJobStatus.Failed) ||
            rebuildRuns.Any(x => x.Status == EloRebuildRunStatus.Failed))
        {
            return "attention";
        }

        return "idle";
    }
}
