using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Elo;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/elo")]
public class EloController(
    BasketEloDbContext dbContext,
    IEloRebuildNotificationPublisher notificationPublisher,
    IIdentityHealthCheckService identityHealthCheckService) : ControllerBase
{
    [HttpGet("rulesets")]
    public ActionResult<EloRulesetCatalogResponse> GetRulesets()
    {
        return Ok(BuildRulesetCatalog());
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<EloDashboardResponse>> GetDashboard(
        [FromQuery] string? rulesetVersion,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var selectedRuleset = ResolveRulesetOrDefault(rulesetVersion);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }

        limit = Math.Clamp(limit, 1, 50);

        var completedGamesQuery = dbContext.Games
            .AsNoTracking()
            .Where(x => x.HomeScore.HasValue && x.AwayScore.HasValue && x.HomeScore != x.AwayScore);

        var summary = new EloDashboardSummary(
            selectedRuleset,
            await completedGamesQuery.CountAsync(cancellationToken),
            await completedGamesQuery.CountAsync(
                x => !dbContext.RatingHistories.Any(history =>
                    history.GameId == x.Id &&
                    history.RulesetVersion == selectedRuleset),
                cancellationToken),
            await dbContext.TeamRatings
                .AsNoTracking()
                .CountAsync(x => x.RulesetVersion == selectedRuleset, cancellationToken),
            await completedGamesQuery.MaxAsync(x => (DateTime?)x.GameDateTimeUtc, cancellationToken),
            await dbContext.EloRebuildRuns
                .AsNoTracking()
                .Where(x =>
                    x.RulesetVersion == selectedRuleset &&
                    x.Status == EloRebuildRunStatus.Completed)
                .MaxAsync(x => x.FinishedAtUtc, cancellationToken),
            await dbContext.EloRebuildRuns
                .AsNoTracking()
                .Where(x => x.RulesetVersion == selectedRuleset)
                .MaxAsync(x => (DateTime?)x.QueuedAtUtc, cancellationToken));

        var runs = await dbContext.EloRebuildRuns
            .AsNoTracking()
            .OrderByDescending(x => x.QueuedAtUtc)
            .Take(limit)
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

        return Ok(new EloDashboardResponse(
            BuildRulesetCatalog(),
            summary,
            runs));
    }

    [HttpGet("rankings")]
    public async Task<ActionResult<EloRankingsResponse>> GetRankings(
        [FromQuery] string? rulesetVersion,
        [FromQuery] string? country,
        [FromQuery] string? competition,
        [FromQuery] string? season,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int? minGames,
        [FromQuery] string? team,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var selectedRuleset = await ResolveReadableRulesetAsync(rulesetVersion, cancellationToken);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var minimumGames = Math.Max(0, minGames ?? 0);

        var globalRatings = await dbContext.TeamRatings
            .AsNoTracking()
            .Include(x => x.Team)
            .Include(x => x.LastGame)
            .Where(x => x.RulesetVersion == selectedRuleset)
            .OrderByDescending(x => x.Elo)
            .ThenBy(x => x.Team.CanonicalName)
            .ToListAsync(cancellationToken);

        var globalRanks = globalRatings
            .Select((rating, index) => new { rating.TeamId, Rank = index + 1 })
            .ToDictionary(x => x.TeamId, x => x.Rank);

        var filteredTeamIds = globalRatings.Select(x => x.TeamId).ToHashSet();

        if (!string.IsNullOrWhiteSpace(country))
        {
            filteredTeamIds.IntersectWith(globalRatings
                .Where(x => IsCountryMatch(x.Team.CountryCode, country))
                .Select(x => x.TeamId));
        }

        if (!string.IsNullOrWhiteSpace(team))
        {
            filteredTeamIds.IntersectWith(globalRatings
                .Where(x => x.Team.CanonicalName.Contains(team, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.TeamId));
        }

        if (minimumGames > 0)
        {
            filteredTeamIds.IntersectWith(globalRatings
                .Where(x => x.GamesPlayed >= minimumGames)
                .Select(x => x.TeamId));
        }

        if (HasHistoryFilter(competition, season, fromUtc, toUtc))
        {
            var historyTeamIds = await BuildHistoryFilterQuery(selectedRuleset, competition, season, fromUtc, toUtc)
                .Select(x => x.TeamId)
                .Distinct()
                .ToListAsync(cancellationToken);

            filteredTeamIds.IntersectWith(historyTeamIds);
        }

        var filteredRatings = globalRatings
            .Where(x => filteredTeamIds.Contains(x.TeamId))
            .OrderByDescending(x => x.Elo)
            .ThenBy(x => x.Team.CanonicalName)
            .ToList();

        var recentMovement = await GetRecentMovementAsync(
            selectedRuleset,
            filteredRatings.Select(x => x.TeamId).ToList(),
            cancellationToken);

        var filteredCount = filteredRatings.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var pageRatings = filteredRatings
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var rows = pageRatings
            .Select((rating, index) => new EloRankingRow(
                rating.TeamId,
                ((page - 1) * pageSize) + index + 1,
                globalRanks[rating.TeamId],
                rating.Team.CanonicalName,
                DisplayCountryFromCode(rating.Team.CountryCode),
                rating.Elo,
                rating.GamesPlayed,
                recentMovement.GetValueOrDefault(rating.TeamId),
                rating.LastGame?.GameDateTimeUtc))
            .ToList();

        return Ok(new EloRankingsResponse(
            selectedRuleset,
            rows,
            await BuildRankingFilterOptionsAsync(selectedRuleset, cancellationToken),
            new EloRankingSummary(
                globalRatings.Count,
                filteredCount,
                globalRatings.Select(x => x.LastGame?.GameDateTimeUtc).Where(x => x.HasValue).Max(),
                globalRatings.FirstOrDefault()?.Team.CanonicalName,
                globalRatings.FirstOrDefault()?.Elo,
                IsFiltered(country, competition, season, fromUtc, toUtc, minimumGames, team)),
            page,
            pageSize,
            filteredCount,
            totalPages));
    }

    [HttpGet("rankings/evolution")]
    public async Task<ActionResult<EloRankingsEvolutionResponse>> GetRankingEvolution(
        [FromQuery] string? rulesetVersion,
        [FromQuery] string? teamIds,
        [FromQuery] int pointsPerTeam = 60,
        CancellationToken cancellationToken = default)
    {
        var selectedRuleset = await ResolveReadableRulesetAsync(rulesetVersion, cancellationToken);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }

        var selectedTeamIds = ParseTeamIds(teamIds).Take(20).ToList();
        if (selectedTeamIds.Count == 0)
        {
            return Ok(new EloRankingsEvolutionResponse(selectedRuleset, []));
        }

        var includeFullHistory = pointsPerTeam <= 0;
        if (!includeFullHistory)
        {
            pointsPerTeam = Math.Clamp(pointsPerTeam, 2, 120);
        }

        var rows = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.RulesetVersion == selectedRuleset && selectedTeamIds.Contains(x.TeamId))
            .Select(x => new
            {
                x.TeamId,
                x.Team.CanonicalName,
                x.GameDateTimeUtc,
                x.PostElo
            })
            .ToListAsync(cancellationToken);

        var series = rows
            .GroupBy(x => new { x.TeamId, x.CanonicalName })
            .Select(group => new EloTeamEvolutionSeries(
                group.Key.TeamId,
                group.Key.CanonicalName,
                (includeFullHistory
                    ? group.OrderBy(x => x.GameDateTimeUtc).ThenBy(x => x.PostElo)
                    : group
                        .OrderByDescending(x => x.GameDateTimeUtc)
                        .ThenByDescending(x => x.PostElo)
                        .Take(pointsPerTeam)
                        .OrderBy(x => x.GameDateTimeUtc)
                        .ThenBy(x => x.PostElo))
                    .Select(x => new EloTeamEvolutionPoint(x.GameDateTimeUtc, x.PostElo))
                    .ToList()))
            .OrderBy(x => selectedTeamIds.IndexOf(x.TeamId))
            .ToList();

        return Ok(new EloRankingsEvolutionResponse(selectedRuleset, series));
    }

    [HttpGet("games/{gameId:guid}/explanation")]
    public async Task<ActionResult<EloGameExplanationResponse>> GetGameExplanation(
        Guid gameId,
        [FromQuery] string? rulesetVersion,
        CancellationToken cancellationToken = default)
    {
        var selectedRuleset = await ResolveReadableRulesetAsync(rulesetVersion, cancellationToken);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }

        var game = await dbContext.Games
            .AsNoTracking()
            .Include(x => x.Competition)
            .Include(x => x.Season)
            .Include(x => x.HomeTeam)
            .Include(x => x.AwayTeam)
            .SingleOrDefaultAsync(x => x.Id == gameId, cancellationToken);

        if (game is null)
        {
            return NotFound();
        }

        var ruleset = EloCalculator.GetRulesetParameters(selectedRuleset);
        var histories = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.GameId == gameId && x.RulesetVersion == selectedRuleset)
            .Select(x => new GameExplanationHistoryRow(
                x.TeamId,
                x.Team.CanonicalName,
                x.PreElo,
                x.PostElo,
                x.EloDelta,
                x.ExpectedScore,
                x.ActualScore,
                x.KFactorUsed,
                x.MarginMultiplier,
                x.CompetitionWeight,
                x.GamesPlayedBefore,
                x.RatingPositionAfter))
            .ToListAsync(cancellationToken);

        var homeHistory = histories.SingleOrDefault(x => x.TeamId == game.HomeTeamId);
        var awayHistory = histories.SingleOrDefault(x => x.TeamId == game.AwayTeamId);
        var isRated = homeHistory is not null && awayHistory is not null;

        return Ok(new EloGameExplanationResponse(
            game.Id,
            selectedRuleset,
            game.GameDateTimeUtc,
            game.Competition.Name,
            game.Season.Label,
            game.HomeTeam.CanonicalName,
            game.AwayTeam.CanonicalName,
            game.HomeScore,
            game.AwayScore,
            game.Status,
            isRated,
            isRated ? null : "This game does not have rating history for the selected ruleset.",
            homeHistory is null ? null : ToGameTeamExplanation(homeHistory, true),
            awayHistory is null ? null : ToGameTeamExplanation(awayHistory, false),
            new EloGameRulesetExplanation(
                ruleset.BaseRating,
                ruleset.KFactor,
                ruleset.HomeAdvantageElo,
                ruleset.PointsPerEloMargin,
                ruleset.CompetitionWeight,
                ruleset.UsesMarginAdjustment)));
    }

    [HttpGet("teams/{teamId:guid}")]
    public async Task<ActionResult<EloTeamDetailResponse>> GetTeam(
        Guid teamId,
        [FromQuery] string? rulesetVersion,
        [FromQuery] int gamesPage = 1,
        [FromQuery] int gamesPageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var selectedRuleset = await ResolveReadableRulesetAsync(rulesetVersion, cancellationToken);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }

        gamesPage = Math.Max(1, gamesPage);
        gamesPageSize = Math.Clamp(gamesPageSize, 10, 100);

        var rating = await dbContext.TeamRatings
            .AsNoTracking()
            .Include(x => x.Team)
            .Include(x => x.LastGame)
            .SingleOrDefaultAsync(x => x.TeamId == teamId && x.RulesetVersion == selectedRuleset, cancellationToken);

        if (rating is null)
        {
            return NotFound();
        }

        var globalRank = await dbContext.TeamRatings
            .AsNoTracking()
            .CountAsync(x => x.RulesetVersion == selectedRuleset && x.Elo > rating.Elo, cancellationToken) + 1;

        var recentMovement = (await GetRecentMovementAsync(selectedRuleset, [teamId], cancellationToken))
            .GetValueOrDefault(teamId);

        var competitionRows = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.TeamId == teamId && x.RulesetVersion == selectedRuleset)
            .Include(x => x.Game)
            .ThenInclude(x => x.Competition)
            .Select(x => x.Game.Competition.Name)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        var teamGamesQuery = dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.TeamId == teamId && x.RulesetVersion == selectedRuleset);

        var gamesTotalCount = await teamGamesQuery.CountAsync(cancellationToken);
        var gamesTotalPages = Math.Max(1, (int)Math.Ceiling(gamesTotalCount / (double)gamesPageSize));
        gamesPage = Math.Min(gamesPage, gamesTotalPages);

        var recentGames = await teamGamesQuery
            .Include(x => x.Game)
            .ThenInclude(x => x.Competition)
            .Include(x => x.Game)
            .ThenInclude(x => x.Season)
            .Include(x => x.Game)
            .ThenInclude(x => x.HomeTeam)
            .Include(x => x.Game)
            .ThenInclude(x => x.AwayTeam)
            .OrderByDescending(x => x.GameDateTimeUtc)
            .ThenByDescending(x => x.Id)
            .Skip((gamesPage - 1) * gamesPageSize)
            .Take(gamesPageSize)
            .Select(x => new EloTeamGameDto(
                x.GameId,
                x.GameDateTimeUtc,
                x.Game.Competition.Name,
                x.Game.Season.Label,
                x.OpponentTeamId == x.Game.HomeTeamId ? x.Game.HomeTeam.CanonicalName : x.Game.AwayTeam.CanonicalName,
                x.TeamId == x.Game.HomeTeamId,
                x.TeamId == x.Game.HomeTeamId ? x.Game.HomeScore : x.Game.AwayScore,
                x.TeamId == x.Game.HomeTeamId ? x.Game.AwayScore : x.Game.HomeScore,
                x.PreElo,
                x.PostElo,
                x.EloDelta))
            .ToListAsync(cancellationToken);

        var historyRows = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.TeamId == teamId && x.RulesetVersion == selectedRuleset)
            .OrderBy(x => x.GameDateTimeUtc)
            .ThenBy(x => x.Id)
            .Select(x => new EloRatingHistoryPoint(
                x.GameDateTimeUtc,
                x.PostElo,
                x.EloDelta,
                x.RatingPositionAfter))
            .ToListAsync(cancellationToken);

        return Ok(new EloTeamDetailResponse(
            rating.TeamId,
            rating.Team.CanonicalName,
            DisplayCountryFromCode(rating.Team.CountryCode),
            selectedRuleset,
            rating.Elo,
            globalRank,
            rating.GamesPlayed,
            recentMovement,
            rating.LastGame?.GameDateTimeUtc,
            competitionRows,
            recentGames,
            gamesPage,
            gamesPageSize,
            gamesTotalCount,
            gamesTotalPages,
            historyRows));
    }

    [HttpPost("rebuilds")]
    public async Task<ActionResult<IReadOnlyList<EloRebuildRunDto>>> Rebuild(
        [FromBody] EloRebuildRequest? request,
        CancellationToken cancellationToken)
    {
        var requestedRuleset = request?.RulesetVersion;
        IReadOnlyList<string> rulesets;
        if (string.IsNullOrWhiteSpace(requestedRuleset) ||
            string.Equals(requestedRuleset, "all", StringComparison.OrdinalIgnoreCase))
        {
            rulesets = EloRulesetVersions.All;
        }
        else
        {
            var normalized = requestedRuleset.Trim().ToLowerInvariant();
            if (!EloRulesetVersions.All.Contains(normalized))
            {
                return BadRequest($"Unsupported ELO ruleset '{requestedRuleset}'.");
            }

            rulesets = [normalized];
        }

        var activeRulesets = await dbContext.EloRebuildRuns
            .Where(x => rulesets.Contains(x.RulesetVersion) &&
                (x.Status == EloRebuildRunStatus.Pending || x.Status == EloRebuildRunStatus.Running))
            .Select(x => x.RulesetVersion)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (activeRulesets.Count > 0)
        {
            return Conflict($"An ELO rebuild is already queued or running for: {string.Join(", ", activeRulesets)}.");
        }

        var identityGate = await EnsureIdentityHealthAllowsRebuildAsync(cancellationToken);
        if (identityGate is not null)
        {
            return identityGate;
        }

        var queuedAtUtc = DateTime.UtcNow;
        var runs = rulesets.Select(ruleset => new EloRebuildRun
        {
            Id = Guid.NewGuid(),
            RulesetVersion = ruleset,
            Status = EloRebuildRunStatus.Pending,
            QueuedAtUtc = queuedAtUtc,
            CreatedAtUtc = queuedAtUtc
        }).ToList();

        dbContext.EloRebuildRuns.AddRange(runs);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Conflict($"An ELO rebuild is already queued or running for: {string.Join(", ", rulesets)}.");
        }

        return Accepted(runs.Select(ToDto).ToList());
    }

    [HttpPost("rebuilds/{runId:guid}/cancel")]
    public async Task<ActionResult<EloRebuildRunDto>> CancelRebuild(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.EloRebuildRuns.SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        if (run.Status != EloRebuildRunStatus.Pending)
        {
            return Conflict($"Only pending rebuilds can be canceled. Run '{runId}' is {run.Status}.");
        }

        run.Status = EloRebuildRunStatus.Canceled;
        run.FinishedAtUtc = DateTime.UtcNow;
        run.Notes = "Canceled by an internal admin operator before the worker started it.";
        await dbContext.SaveChangesAsync(cancellationToken);
        await PublishNotificationAsync(run, cancellationToken);

        return Ok(ToDto(run));
    }

    [HttpPost("rebuilds/{runId:guid}/retry")]
    public async Task<ActionResult<EloRebuildRunDto>> RetryRebuild(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var sourceRun = await dbContext.EloRebuildRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (sourceRun is null)
        {
            return NotFound();
        }

        if (sourceRun.Status is not (EloRebuildRunStatus.Failed or EloRebuildRunStatus.Canceled))
        {
            return Conflict($"Only failed or canceled rebuilds can be retried. Run '{runId}' is {sourceRun.Status}.");
        }

        var activeExists = await dbContext.EloRebuildRuns.AnyAsync(x =>
            x.RulesetVersion == sourceRun.RulesetVersion &&
            (x.Status == EloRebuildRunStatus.Pending || x.Status == EloRebuildRunStatus.Running),
            cancellationToken);
        if (activeExists)
        {
            return Conflict($"An ELO rebuild is already queued or running for: {sourceRun.RulesetVersion}.");
        }

        var identityGate = await EnsureIdentityHealthAllowsRebuildAsync(cancellationToken);
        if (identityGate is not null)
        {
            return identityGate;
        }

        var queuedAtUtc = DateTime.UtcNow;
        var retryRun = new EloRebuildRun
        {
            Id = Guid.NewGuid(),
            RulesetVersion = sourceRun.RulesetVersion,
            Status = EloRebuildRunStatus.Pending,
            QueuedAtUtc = queuedAtUtc,
            CreatedAtUtc = queuedAtUtc,
            Notes = $"Retry queued from rebuild run {sourceRun.Id}."
        };

        dbContext.EloRebuildRuns.Add(retryRun);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Conflict($"An ELO rebuild is already queued or running for: {sourceRun.RulesetVersion}.");
        }

        return Accepted(ToDto(retryRun));
    }

    private static EloRulesetCatalogResponse BuildRulesetCatalog()
        => new(EloRulesetVersions.Default, EloRulesetVersions.All);

    private async Task<ConflictObjectResult?> EnsureIdentityHealthAllowsRebuildAsync(CancellationToken cancellationToken)
    {
        var identityRun = await identityHealthCheckService.RunAsync(new IdentityHealthCheckRequest(), cancellationToken);
        if (identityRun.UnresolvedBlockersCount == 0)
        {
            return null;
        }

        return Conflict(new
        {
            message = "ELO rebuild is blocked by unresolved identity health blockers.",
            identityRunId = identityRun.Id,
            identityRun.ScopeKey,
            identityRun.UnresolvedBlockersCount
        });
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

    private async Task<string?> ResolveReadableRulesetAsync(string? rulesetVersion, CancellationToken cancellationToken)
    {
        var resolved = ResolveRulesetOrDefault(rulesetVersion);
        if (resolved is null)
        {
            return null;
        }

        if (await dbContext.TeamRatings.AsNoTracking().AnyAsync(x => x.RulesetVersion == resolved, cancellationToken))
        {
            return resolved;
        }

        if (string.IsNullOrWhiteSpace(rulesetVersion) &&
            await dbContext.TeamRatings.AsNoTracking().AnyAsync(x => x.RulesetVersion == EloRulesetVersions.PointMarginEloV1, cancellationToken))
        {
            return EloRulesetVersions.PointMarginEloV1;
        }

        return resolved;
    }

    private static EloRebuildRunDto ToDto(EloRebuildRun run)
        => new(
            run.Id,
            run.RulesetVersion,
            run.Status,
            run.GamesProcessed,
            run.TeamsRated,
            run.QueuedAtUtc,
            run.StartedAtUtc,
            run.FinishedAtUtc,
            run.FromGameDateTimeUtc,
            run.Notes);

    private Task PublishNotificationAsync(EloRebuildRun run, CancellationToken cancellationToken)
        => notificationPublisher.PublishAsync(
            new EloRebuildRunNotification(
                run.Id,
                run.RulesetVersion,
                run.Status,
                DateTime.UtcNow),
            cancellationToken);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static EloGameTeamExplanation ToGameTeamExplanation(GameExplanationHistoryRow history, bool wasHome)
        => new(
            history.TeamId,
            history.TeamName,
            wasHome,
            history.PreElo,
            history.PostElo,
            history.EloDelta,
            history.ExpectedScore,
            history.ActualScore,
            history.KFactorUsed,
            history.MarginMultiplier,
            history.CompetitionWeight,
            history.GamesPlayedBefore,
            history.RatingPositionAfter);

    private sealed record GameExplanationHistoryRow(
        Guid TeamId,
        string TeamName,
        decimal PreElo,
        decimal PostElo,
        decimal EloDelta,
        decimal ExpectedScore,
        decimal ActualScore,
        int KFactorUsed,
        decimal MarginMultiplier,
        decimal CompetitionWeight,
        int GamesPlayedBefore,
        int? RatingPositionAfter);

    private IQueryable<RatingHistory> BuildHistoryFilterQuery(
        string rulesetVersion,
        string? competition,
        string? season,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        var query = dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.RulesetVersion == rulesetVersion);

        if (!string.IsNullOrWhiteSpace(competition))
        {
            query = query.Where(x => x.Game.Competition.Name == competition);
        }

        if (!string.IsNullOrWhiteSpace(season))
        {
            query = query.Where(x => x.Game.Season.Label == season);
        }

        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.GameDateTimeUtc >= DateTime.SpecifyKind(fromUtc.Value.Date, DateTimeKind.Utc));
        }

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.GameDateTimeUtc <= DateTime.SpecifyKind(toUtc.Value.Date, DateTimeKind.Utc).AddDays(1).AddTicks(-1));
        }

        return query;
    }

    private async Task<EloRankingFilterOptions> BuildRankingFilterOptionsAsync(string rulesetVersion, CancellationToken cancellationToken)
    {
        var countries = await dbContext.TeamRatings
            .AsNoTracking()
            .Where(x => x.RulesetVersion == rulesetVersion)
            .Select(x => x.Team.CountryCode)
            .Distinct()
            .ToListAsync(cancellationToken);

        var competitions = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.RulesetVersion == rulesetVersion)
            .Select(x => x.Game.Competition.Name)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        var seasons = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.RulesetVersion == rulesetVersion)
            .Select(x => x.Game.Season.Label)
            .Distinct()
            .OrderByDescending(x => x)
            .ToListAsync(cancellationToken);

        return new EloRankingFilterOptions(
            countries.Select(DisplayCountryFromCode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).ToList(),
            competitions,
            seasons);
    }

    private async Task<Dictionary<Guid, decimal>> GetRecentMovementAsync(
        string rulesetVersion,
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken cancellationToken)
    {
        if (teamIds.Count == 0)
        {
            return [];
        }

        var rows = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.RulesetVersion == rulesetVersion && teamIds.Contains(x.TeamId))
            .OrderByDescending(x => x.GameDateTimeUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.TeamId, x.EloDelta })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.TeamId)
            .ToDictionary(
                x => x.Key,
                x => x.Take(5).Sum(y => y.EloDelta));
    }

    private static bool HasHistoryFilter(string? competition, string? season, DateTime? fromUtc, DateTime? toUtc)
        => !string.IsNullOrWhiteSpace(competition) ||
           !string.IsNullOrWhiteSpace(season) ||
           fromUtc.HasValue ||
           toUtc.HasValue;

    private static bool IsFiltered(
        string? country,
        string? competition,
        string? season,
        DateTime? fromUtc,
        DateTime? toUtc,
        int minGames,
        string? team)
        => !string.IsNullOrWhiteSpace(country) ||
           !string.IsNullOrWhiteSpace(competition) ||
           !string.IsNullOrWhiteSpace(season) ||
           fromUtc.HasValue ||
           toUtc.HasValue ||
           minGames > 0 ||
           !string.IsNullOrWhiteSpace(team);

    private static bool IsCountryMatch(string? countryCode, string country)
        => string.Equals(DisplayCountryFromCode(countryCode), country, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(countryCode, country, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<Guid> ParseTeamIds(string? teamIds)
    {
        if (string.IsNullOrWhiteSpace(teamIds))
        {
            return [];
        }

        return teamIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Guid.TryParse(value, out var teamId) ? teamId : (Guid?)null)
            .Where(teamId => teamId.HasValue)
            .Select(teamId => teamId!.Value)
            .Distinct()
            .ToList();
    }

    private static string DisplayCountryFromCode(string? countryCode)
    {
        return countryCode?.ToUpperInvariant() switch
        {
            "ES" => "Spain",
            "ESP" => "Spain",
            "FR" => "France",
            "FRA" => "France",
            "LT" => "Lithuania",
            "LTU" => "Lithuania",
            "GR" => "Greece",
            "GRC" => "Greece",
            "IT" => "Italy",
            "ITA" => "Italy",
            "TR" => "Turkey",
            "TUR" => "Turkey",
            "LV" => "Latvia",
            "LVA" => "Latvia",
            "BE" => "Belgium",
            "BEL" => "Belgium",
            "DE" => "Germany",
            "DEU" => "Germany",
            "IL" => "Israel",
            "ISR" => "Israel",
            "PL" => "Poland",
            "POL" => "Poland",
            "CZ" => "Czech Republic",
            "CZE" => "Czech Republic",
            "RU" => "Russia",
            "RUS" => "Russia",
            _ => countryCode ?? string.Empty
        };
    }
}
