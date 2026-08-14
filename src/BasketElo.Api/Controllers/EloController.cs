using BasketElo.Api.Auth;
using BasketElo.Api.Elo;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Elo;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using NpgsqlTypes;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/elo")]
public class EloController(
    BasketEloDbContext dbContext,
    IEloRebuildNotificationPublisher notificationPublisher,
    IIdentityHealthCheckService identityHealthCheckService,
    IMemoryCache cache,
    EloResponseCache responseCache) : ControllerBase
{
    [HttpGet("rulesets")]
    public ActionResult<EloRulesetCatalogResponse> GetRulesets()
    {
        return Ok(BuildRulesetCatalog());
    }

    [HttpGet("pools")]
    public async Task<ActionResult<EloPoolCatalogResponse>> GetPools(CancellationToken cancellationToken)
    {
        var options = new List<EloPoolOption>();
        foreach (var descriptor in EloPoolCatalog.All.OrderBy(x => x.DisplayOrder))
        {
            var ratings = dbContext.TeamRatings.AsNoTracking()
                .Where(x => x.EloPoolKey == descriptor.Key && x.RulesetVersion == EloRulesetVersions.Default);
            options.Add(new EloPoolOption(
                descriptor.Key,
                descriptor.DisplayName,
                await ratings.AnyAsync(cancellationToken),
                await ratings.CountAsync(cancellationToken),
                await ratings.Select(x => x.LastGame!.GameDateTimeUtc).MaxAsync(x => (DateTime?)x, cancellationToken),
                await dbContext.EloRebuildRuns.AsNoTracking()
                    .Where(x => x.EloPoolKey == descriptor.Key &&
                        x.RulesetVersion == EloRulesetVersions.Default &&
                        x.Status == EloRebuildRunStatus.Completed)
                    .MaxAsync(x => x.FinishedAtUtc, cancellationToken)));
        }

        return Ok(new EloPoolCatalogResponse(EloPoolKeys.Default, options));
    }

    [HttpGet("browse")]
    public async Task<ActionResult<EloBrowseResponse>> GetBrowse(
        [FromQuery] string? pool,
        [FromQuery] string? country,
        [FromQuery] string? competition,
        [FromQuery] string? season,
        CancellationToken cancellationToken = default)
    {
        var poolKey = ResolvePoolOrDefault(pool);
        if (poolKey is null)
        {
            return BadRequest($"Unsupported ELO pool '{pool}'.");
        }

        var competitionRows = await dbContext.Competitions
            .AsNoTracking()
            .Where(x => x.IsActive && x.EloPoolKey == poolKey)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Type,
                x.CountryCode,
                x.Tier,
                x.SupportPolicy
            })
            .OrderBy(x => x.CountryCode)
            .ThenBy(x => x.Tier)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var competitionIds = competitionRows.Select(x => x.Id).ToList();
        var gameStats = await dbContext.Games
            .AsNoTracking()
            .Where(x => competitionIds.Contains(x.CompetitionId))
            .GroupBy(x => x.CompetitionId)
            .Select(group => new
            {
                CompetitionId = group.Key,
                GameCount = group.Count(),
                LatestGameUtc = group.Max(x => (DateTime?)x.GameDateTimeUtc)
            })
            .ToListAsync(cancellationToken);
        var gameStatsByCompetition = gameStats.ToDictionary(x => x.CompetitionId);

        var teamRows = await dbContext.Games
            .AsNoTracking()
            .Where(x => competitionIds.Contains(x.CompetitionId))
            .Select(x => new { x.CompetitionId, x.HomeTeamId, x.AwayTeamId })
            .ToListAsync(cancellationToken);
        var teamIdsByCompetition = teamRows
            .GroupBy(x => x.CompetitionId)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(x => new[] { x.HomeTeamId, x.AwayTeamId }).ToHashSet());

        var seasonRows = await dbContext.Seasons
            .AsNoTracking()
            .Where(x => competitionIds.Contains(x.CompetitionId))
            .Select(x => new { x.CompetitionId, x.Label })
            .ToListAsync(cancellationToken);
        var seasonsByCompetition = seasonRows
            .GroupBy(x => x.CompetitionId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.Label).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x).ToList());

        var competitionCatalog = competitionRows
            .Select(row => new EloBrowseCompetition(
                row.Name,
                BrowseCountryName(row.CountryCode),
                row.CountryCode,
                row.Type,
                row.Tier,
                teamIdsByCompetition.GetValueOrDefault(row.Id)?.Count ?? 0,
                gameStatsByCompetition.GetValueOrDefault(row.Id)?.GameCount ?? 0,
                gameStatsByCompetition.GetValueOrDefault(row.Id)?.LatestGameUtc,
                seasonsByCompetition.GetValueOrDefault(row.Id) ?? [],
                row.SupportPolicy))
            .ToList();

        var countries = competitionRows
            .GroupBy(row => row.CountryCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group => new EloBrowseCountry(
                BrowseCountryName(group.Key),
                string.IsNullOrWhiteSpace(group.Key) ? null : group.Key,
                group.Count(),
                group.SelectMany(row => teamIdsByCompetition.GetValueOrDefault(row.Id) ?? []).Distinct().Count(),
                group.Sum(row => gameStatsByCompetition.GetValueOrDefault(row.Id)?.GameCount ?? 0),
                group.Max(row => gameStatsByCompetition.GetValueOrDefault(row.Id)?.LatestGameUtc)))
            .OrderBy(row => row.Name)
            .ToList();

        var selectedCompetitionRow = competitionRows.FirstOrDefault(row =>
            !string.IsNullOrWhiteSpace(competition) &&
            string.Equals(row.Name, competition.Trim(), StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(country) || IsBrowseCountryMatch(row.CountryCode, country)));
        var contextRows = selectedCompetitionRow is not null
            ? competitionRows.Where(row => row.Id == selectedCompetitionRow.Id).ToList()
            : !string.IsNullOrWhiteSpace(competition)
                ? []
                : competitionRows.Where(row => IsBrowseCountryMatch(row.CountryCode, country)).ToList();

        EloBrowseContext? context = null;
        if (!string.IsNullOrWhiteSpace(country) || !string.IsNullOrWhiteSpace(competition))
        {
            var contextCompetitionIds = contextRows.Select(row => row.Id).ToList();
            var contextQuery = dbContext.Games
                .AsNoTracking()
                .Where(x => contextCompetitionIds.Contains(x.CompetitionId));
            if (!string.IsNullOrWhiteSpace(season))
            {
                contextQuery = contextQuery.Where(x => x.Season.Label == season.Trim());
            }

            var contextAggregate = await contextQuery
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    GameCount = group.Count(),
                    FinishedGameCount = group.Count(x => x.HomeScore.HasValue && x.AwayScore.HasValue),
                    FirstGameUtc = group.Min(x => (DateTime?)x.GameDateTimeUtc),
                    LatestGameUtc = group.Max(x => (DateTime?)x.GameDateTimeUtc)
                })
                .FirstOrDefaultAsync(cancellationToken);

            var contextSeasonGames = await contextQuery
                .Select(x => new
                {
                    SeasonLabel = x.Season.Label,
                    x.GameDateTimeUtc
                })
                .ToListAsync(cancellationToken);
            var contextSeasonRows = contextSeasonGames
                .GroupBy(x => x.SeasonLabel, StringComparer.OrdinalIgnoreCase)
                .Select(group => new EloBrowseSeason(
                    group.Key,
                    group.Count(),
                    group.Max(x => (DateTime?)x.GameDateTimeUtc)))
                .OrderByDescending(x => x.Label)
                .ToList();

            var contextGameRows = await contextQuery
                .OrderByDescending(x => x.GameDateTimeUtc)
                .ThenByDescending(x => x.Id)
                .Take(12)
                .Select(x => new EloBrowseGame(
                    x.Id,
                    x.GameDateTimeUtc,
                    x.Season.Label,
                    x.CompetitionPhase,
                    x.HomeTeam.CanonicalName,
                    x.AwayTeam.CanonicalName,
                    x.HomeScore,
                    x.AwayScore,
                    x.Status))
                .ToListAsync(cancellationToken);

            var contextTeamIds = await contextQuery
                .SelectMany(x => new[] { x.HomeTeamId, x.AwayTeamId })
                .Distinct()
                .ToListAsync(cancellationToken);
            var contextRatings = await dbContext.TeamRatings
                .AsNoTracking()
                .Where(x => x.EloPoolKey == poolKey &&
                    x.RulesetVersion == EloRulesetVersions.Default &&
                    contextTeamIds.Contains(x.TeamId))
                .Select(x => new
                {
                    x.TeamId,
                    x.Elo,
                    x.GamesPlayed,
                    Name = x.Team.CanonicalName,
                    CountryCode = x.Team.CountryCode
                })
                .OrderByDescending(x => x.Elo)
                .ThenBy(x => x.Name)
                .ToListAsync(cancellationToken);
            var topTeams = contextRatings
                .Select((row, index) => new EloBrowseTeam(
                    row.TeamId,
                    row.Name,
                    DisplayCountryFromCode(row.CountryCode),
                    row.Elo,
                    row.GamesPlayed,
                    index + 1))
                .Take(12)
                .ToList();

            var tierSummaries = contextRows
                .GroupBy(row => row.Tier)
                .Select(group =>
                {
                    var tierCompetitionIds = group.Select(row => row.Id).ToHashSet();
                    var tierTeamIds = teamRows
                        .Where(row => tierCompetitionIds.Contains(row.CompetitionId))
                        .SelectMany(row => new[] { row.HomeTeamId, row.AwayTeamId })
                        .Distinct()
                        .ToHashSet();
                    var tierRatings = contextRatings.Where(row => tierTeamIds.Contains(row.TeamId)).Select(row => row.Elo).ToList();
                    return new EloBrowseTierSummary(
                        group.Key,
                        group.Count(),
                        tierTeamIds.Count,
                        group.Sum(row => gameStatsByCompetition.GetValueOrDefault(row.Id)?.GameCount ?? 0),
                        tierRatings.Count == 0 ? null : tierRatings.Average());
                })
                .OrderBy(row => row.Tier)
                .ToList();

            var contextCountry = selectedCompetitionRow is not null
                ? BrowseCountryName(selectedCompetitionRow.CountryCode)
                : country?.Trim();
            var contextCountryCode = selectedCompetitionRow?.CountryCode ??
                contextRows.Select(row => row.CountryCode).FirstOrDefault(code => !string.IsNullOrWhiteSpace(code));
            var contextCompetition = selectedCompetitionRow?.Name;
            var contextSupportPolicy = contextRows.Count switch
            {
                0 => CompetitionSupportPolicies.Unsupported,
                1 => contextRows[0].SupportPolicy,
                _ when contextRows.All(row => row.SupportPolicy == CompetitionSupportPolicies.Supported) => CompetitionSupportPolicies.Supported,
                _ => "mixed"
            };
            var coverageMessage = contextRows.Count == 0
                ? "No imported competition coverage matches this context yet. Try another country or competition."
                : $"{contextRows.Count} competition{(contextRows.Count == 1 ? string.Empty : "s")} in the {poolKey} rating pool; ratings use {EloRulesetVersions.Default}.";

            context = new EloBrowseContext(
                contextCountry,
                contextCompetition,
                contextCountryCode,
                selectedCompetitionRow?.Type,
                selectedCompetitionRow?.Tier,
                contextSupportPolicy,
                coverageMessage,
                contextTeamIds.Count,
                contextAggregate?.GameCount ?? 0,
                contextAggregate?.FinishedGameCount ?? 0,
                contextAggregate?.FirstGameUtc,
                contextAggregate?.LatestGameUtc,
                contextSeasonRows,
                tierSummaries,
                topTeams,
                contextGameRows);
        }

        return Ok(new EloBrowseResponse(
            poolKey,
            EloPoolKeys.DisplayName(poolKey),
            countries,
            competitionCatalog,
            context));
    }

    [HttpGet("dashboard")]
    [RequireInternalAdmin]
    public async Task<ActionResult<EloDashboardResponse>> GetDashboard(
        [FromQuery] string? rulesetVersion,
        [FromQuery] string? pool,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var selectedRuleset = ResolveRulesetOrDefault(rulesetVersion);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }
        var poolKey = ResolvePoolOrDefault(pool);
        if (poolKey is null)
        {
            return BadRequest($"Unsupported ELO pool '{pool}'.");
        }

        limit = Math.Clamp(limit, 1, 50);

        var completedGamesQuery = dbContext.Games
            .AsNoTracking()
            .Where(x => x.Competition.EloPoolKey == poolKey &&
                x.HomeScore.HasValue && x.AwayScore.HasValue && x.HomeScore != x.AwayScore);

        var summary = new EloDashboardSummary(
            poolKey,
            selectedRuleset,
            await completedGamesQuery.CountAsync(cancellationToken),
            await completedGamesQuery.CountAsync(
                x => !dbContext.RatingHistories.Any(history =>
                    history.GameId == x.Id &&
                    history.EloPoolKey == poolKey &&
                    history.RulesetVersion == selectedRuleset),
                cancellationToken),
            await dbContext.TeamRatings
                .AsNoTracking()
                .CountAsync(x => x.EloPoolKey == poolKey && x.RulesetVersion == selectedRuleset, cancellationToken),
            await completedGamesQuery.MaxAsync(x => (DateTime?)x.GameDateTimeUtc, cancellationToken),
            await dbContext.EloRebuildRuns
                .AsNoTracking()
                .Where(x =>
                    x.RulesetVersion == selectedRuleset &&
                    x.EloPoolKey == poolKey &&
                    x.Status == EloRebuildRunStatus.Completed)
                .MaxAsync(x => x.FinishedAtUtc, cancellationToken),
            await dbContext.EloRebuildRuns
                .AsNoTracking()
                .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == selectedRuleset)
                .MaxAsync(x => (DateTime?)x.QueuedAtUtc, cancellationToken));

        var runs = await dbContext.EloRebuildRuns
            .AsNoTracking()
            .Where(x => x.EloPoolKey == poolKey)
            .OrderByDescending(x => x.QueuedAtUtc)
            .Take(limit)
            .Select(x => new EloRebuildRunDto(
                x.Id,
                x.EloPoolKey,
                x.RulesetVersion,
                x.CompetitionName,
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
        [FromQuery] string? pool,
        [FromQuery] string? country,
        [FromQuery] string? competition,
        [FromQuery] string? season,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] DateTime? asOfDate,
        [FromQuery] int? minGames,
        [FromQuery] string? team,
        [FromQuery] string? teams = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? tournamentCycle = null,
        CancellationToken cancellationToken = default)
    {
        season = NormalizeSeasonFilterInput(season);
        var poolKey = ResolvePoolOrDefault(pool);
        var teamScope = EloTeamScopes.Normalize(teams);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var minimumGames = Math.Max(0, minGames ?? 0);

        if (poolKey is not null && IsDefaultResponseCachePool(poolKey) && IsDefaultRankingsRequest(
                rulesetVersion,
                pool,
                country,
                competition,
                season,
                tournamentCycle,
                fromUtc,
                toUtc,
                asOfDate,
                minimumGames,
                team,
                teamScope,
                page,
                pageSize))
        {
            var selectedRuleset = await ResolveReadableRulesetAsync(rulesetVersion, poolKey, cancellationToken);
            if (selectedRuleset is null)
            {
                return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
            }

            var cacheKey = EloResponseCache.RankingsKey(
                poolKey,
                selectedRuleset,
                teamScope,
                country,
                competition,
                season,
                tournamentCycle,
                fromUtc,
                toUtc,
                asOfDate,
                minimumGames,
                team,
                page,
                pageSize);
            if (responseCache.TryGet<EloRankingsResponse>(cacheKey, out var cachedResponse) && cachedResponse is not null)
            {
                return Ok(cachedResponse);
            }

            var uncachedResult = await GetRankingsUncached(
                rulesetVersion,
                pool,
                country,
                competition,
                season,
                tournamentCycle,
                fromUtc,
                toUtc,
                asOfDate,
                minGames,
                team,
                teams,
                page,
                pageSize,
                cancellationToken);
            if (uncachedResult.Result is OkObjectResult { Value: …15793 tokens truncated…Violation };

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

    private async Task<IReadOnlyList<TeamFormHistoryRow>> GetTeamFormRowsAsync(
        Guid teamId,
        string poolKey,
        string rulesetVersion,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.TeamId == teamId && x.EloPoolKey == poolKey && x.RulesetVersion == rulesetVersion)
            .OrderByDescending(x => x.GameDateTimeUtc)
            .ThenByDescending(x => x.Id)
            .Take(10)
            .Select(x => new TeamFormHistoryRow(
                x.GameId,
                x.GameDateTimeUtc,
                x.OpponentTeam.CanonicalName,
                x.TeamId == x.Game.HomeTeamId,
                x.TeamId == x.Game.HomeTeamId ? x.Game.HomeScore : x.Game.AwayScore,
                x.TeamId == x.Game.HomeTeamId ? x.Game.AwayScore : x.Game.HomeScore,
                x.ActualScore,
                x.EloDelta,
                x.OpponentTeamId))
            .ToListAsync(cancellationToken);

        var gameIds = rows.Select(x => x.GameId).Distinct().ToList();
        var opponentIds = rows.Select(x => x.OpponentTeamId).Distinct().ToList();
        var opponentPreElo = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x =>
                x.EloPoolKey == poolKey &&
                x.RulesetVersion == rulesetVersion &&
                gameIds.Contains(x.GameId) &&
                opponentIds.Contains(x.TeamId))
            .Select(x => new { x.GameId, x.TeamId, x.PreElo })
            .ToListAsync(cancellationToken);

        var opponentPreEloByGameAndTeam = opponentPreElo.ToDictionary(x => (x.GameId, x.TeamId), x => x.PreElo);

        return rows
            .Select(x => x with
            {
                OpponentPreElo = opponentPreEloByGameAndTeam.GetValueOrDefault((x.GameId, x.OpponentTeamId))
            })
            .ToList();
    }

    private static IReadOnlyCollection<EloTeamFormSummary> BuildTeamFormSummaries(IReadOnlyList<TeamFormHistoryRow> rows)
    {
        return new[] { 5, 10 }
            .Select(window =>
            {
                var windowRows = rows.Take(window).ToList();
                var wins = windowRows.Count(x => x.ActualScore == 1m);
                var losses = windowRows.Count(x => x.ActualScore == 0m);
                var bestWin = windowRows
                    .Where(x => x.ActualScore == 1m)
                    .OrderByDescending(x => x.EloDelta)
                    .ThenByDescending(x => x.OpponentPreElo)
                    .FirstOrDefault();
                var worstLoss = windowRows
                    .Where(x => x.ActualScore == 0m)
                    .OrderBy(x => x.EloDelta)
                    .ThenByDescending(x => x.OpponentPreElo)
                    .FirstOrDefault();

                return new EloTeamFormSummary(
                    window,
                    windowRows.Count,
                    wins,
                    losses,
                    windowRows.Sum(x => x.EloDelta),
                    windowRows.Count == 0 ? 0 : Math.Round(windowRows.Average(x => x.OpponentPreElo), 2, MidpointRounding.AwayFromZero),
                    bestWin is null ? null : ToTeamFormGame(bestWin),
                    worstLoss is null ? null : ToTeamFormGame(worstLoss));
            })
            .ToList();
    }

    private static EloTeamFormGame ToTeamFormGame(TeamFormHistoryRow row)
        => new(
            row.GameId,
            row.GameDateTimeUtc,
            row.Opponent,
            row.WasHome,
            row.TeamScore,
            row.OpponentScore,
            row.EloDelta,
            row.OpponentPreElo);

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

    private sealed record MoverHistoryRow(
        Guid TeamId,
        string TeamName,
        string? CountryCode,
        Guid GameId,
        DateTime GameDateTimeUtc,
        decimal PreElo,
        decimal PostElo,
        decimal EloDelta);

    private sealed record TeamFormHistoryRow(
        Guid GameId,
        DateTime GameDateTimeUtc,
        string Opponent,
        bool WasHome,
        short? TeamScore,
        short? OpponentScore,
        decimal ActualScore,
        decimal EloDelta,
        Guid OpponentTeamId)
    {
        public decimal OpponentPreElo { get; init; }
    }

    private sealed class EvolutionHistorySqlRow
    {
        public Guid TeamId { get; set; }

        public string TeamName { get; set; } = string.Empty;

        public DateTime GameDateTimeUtc { get; set; }

        public decimal Elo { get; set; }

        public decimal? EloDelta { get; set; }

        public int? Rank { get; set; }

        public long TotalRows { get; set; }
    }

    private sealed record HistoricalRankingRow(
        Guid TeamId,
        string TeamName,
        string? CountryCode,
        bool IsActive,
        Guid GameId,
        DateTime GameDateTimeUtc,
        decimal Elo,
        int GamesPlayed);

    private IQueryable<RatingHistory> BuildHistoryFilterQuery(
        string poolKey,
        string rulesetVersion,
        string? competition,
        string? season,
        string? tournamentCycle,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        var query = dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == rulesetVersion);

        if (!string.IsNullOrWhiteSpace(competition))
        {
            query = query.Where(x => x.Game.Competition.Name == competition);
        }

        if (!string.IsNullOrWhiteSpace(season))
        {
            query = ApplySeasonFilter(query, season);
        }

        if (!string.IsNullOrWhiteSpace(tournamentCycle))
        {
            query = query.Where(x => x.Game.TournamentCycle != null && x.Game.TournamentCycle.Key == tournamentCycle.Trim());
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

    private async Task<EloRankingFilterOptions> BuildRankingFilterOptionsAsync(
        string poolKey,
        string rulesetVersion,
        CancellationToken cancellationToken)
    {
        return await cache.GetOrCreateAsync(
            $"elo:ranking-filter-options:{poolKey}:{rulesetVersion}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
                entry.SlidingExpiration = TimeSpan.FromMinutes(5);

                var countries = await dbContext.TeamRatings
                    .AsNoTracking()
                    .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == rulesetVersion)
                    .Select(x => x.Team.CountryCode)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                var competitions = await dbContext.RatingHistories
                    .AsNoTracking()
                    .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == rulesetVersion)
                    .Select(x => x.Game.Competition.Name)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToListAsync(cancellationToken);

                var seasons = await dbContext.RatingHistories
                    .AsNoTracking()
                    .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == rulesetVersion)
                    .Select(x => x.Game.Season.Label)
                    .ToListAsync(cancellationToken);

                return new EloRankingFilterOptions(
                    countries.Select(DisplayCountryFromCode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).ToList(),
                    competitions,
                    seasons
                        .Select(NormalizeSeasonFilterInput)
                        .OfType<string>()
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(x => x, StringComparer.Ordinal)
                        .ToList());
            }) ?? new EloRankingFilterOptions([], [], []);
    }

    private async Task<Dictionary<Guid, decimal>> GetRecentMovementAsync(
        string poolKey,
        string rulesetVersion,
        IReadOnlyCollection<Guid> teamIds,
        DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        if (teamIds.Count == 0)
        {
            return [];
        }

        var query = dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == rulesetVersion && teamIds.Contains(x.TeamId));

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.GameDateTimeUtc <= toUtc.Value);
        }

        var rows = await query
            .OrderByDescending(x => x.GameDateTimeUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.TeamId, x.EloDelta })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.TeamId)
            .ToDictionary(
                x => x.Key,
                x => x.First().EloDelta);
    }

    private static bool HasHistoryFilter(string? competition, string? season, string? tournamentCycle, DateTime? fromUtc, DateTime? toUtc)
        => !string.IsNullOrWhiteSpace(competition) ||
           !string.IsNullOrWhiteSpace(season) ||
           !string.IsNullOrWhiteSpace(tournamentCycle) ||
           fromUtc.HasValue ||
           toUtc.HasValue;

    private static bool IsFiltered(
        string? country,
        string? competition,
        string? season,
        string? tournamentCycle,
        DateTime? fromUtc,
        DateTime? toUtc,
        int minGames,
        string? team)
        => !string.IsNullOrWhiteSpace(country) ||
           !string.IsNullOrWhiteSpace(competition) ||
           !string.IsNullOrWhiteSpace(season) ||
           !string.IsNullOrWhiteSpace(tournamentCycle) ||
           fromUtc.HasValue ||
           toUtc.HasValue ||
           minGames > 0 ||
           !string.IsNullOrWhiteSpace(team);

    private static IReadOnlyCollection<EloFranchiseIdentityEventDto> GetFranchiseIdentityEvents(
        string poolKey,
        string canonicalTeamName)
    {
        if (poolKey != EloPoolKeys.Nba)
        {
            return [];
        }

        return NbaFranchiseCatalog.FindByCanonicalName(canonicalTeamName)?.IdentityEvents
            .Select(identityEvent => new EloFranchiseIdentityEventDto(
                identityEvent.Year,
                identityEvent.FromName,
                identityEvent.ToName,
                identityEvent.Type switch
                {
                    NbaFranchiseIdentityEventType.Rename => EloFranchiseIdentityEventTypes.Rename,
                    NbaFranchiseIdentityEventType.TemporaryRelocation => EloFranchiseIdentityEventTypes.TemporaryRelocation,
                    _ => EloFranchiseIdentityEventTypes.Relocation
                }))
            .ToList() ?? [];
    }

    private static bool IsCountryMatch(string? countryCode, string country)
        => string.Equals(DisplayCountryFromCode(countryCode), country, StringComparison.OrdinalIgnoreCase) ||
           CountryCodeCatalog.AreEquivalent(countryCode, country);

    private static bool IsBrowseCountryMatch(string? countryCode, string? country)
        => string.IsNullOrWhiteSpace(country) ||
           string.Equals(BrowseCountryName(countryCode), country.Trim(), StringComparison.OrdinalIgnoreCase) ||
           CountryCodeCatalog.AreEquivalent(countryCode, country);

    private static string BrowseCountryName(string? countryCode)
        => string.IsNullOrWhiteSpace(countryCode)
            ? "International / regional"
            : DisplayCountryFromCode(countryCode);

    private static IQueryable<RatingHistory> ApplySeasonFilter(IQueryable<RatingHistory> query, string season)
    {
        var normalized = NormalizeSeasonFilterInput(season) ?? season.Trim();
        var separatorIndex = normalized.IndexOf('-', StringComparison.Ordinal);
        var legacyLabel = separatorIndex > 0 ? normalized[..separatorIndex] : normalized;

        return query.Where(x => x.Game.Season.Label == normalized || x.Game.Season.Label == legacyLabel);
    }

    private static string? NormalizeSeasonFilterInput(string? season)
    {
        if (string.IsNullOrWhiteSpace(season))
        {
            return null;
        }

        var normalized = NormalizeSeasonLabel(season);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeSeasonLabel(string season, DateTime? gameDateTimeUtc = null)
    {
        var trimmed = season.Trim();
        if (trimmed.Contains('-', StringComparison.Ordinal))
        {
            if (!gameDateTimeUtc.HasValue ||
                (TryGetSeasonWindow(trimmed, out var seasonStartUtc, out var seasonEndUtc) &&
                 gameDateTimeUtc.Value >= seasonStartUtc &&
                 gameDateTimeUtc.Value <= seasonEndUtc))
            {
                return trimmed;
            }

            return GetSeasonLabelForDate(gameDateTimeUtc.Value);
        }

        if (!int.TryParse(trimmed, out var year))
        {
            return trimmed;
        }

        if (gameDateTimeUtc.HasValue)
        {
            var dateSeason = GetSeasonLabelForDate(gameDateTimeUtc.Value);
            var previousSeason = $"{year - 1}-{year}";
            var currentSeason = $"{year}-{year + 1}";
            return dateSeason == previousSeason || dateSeason == currentSeason
                ? dateSeason
                : currentSeason;
        }

        return $"{year}-{year + 1}";
    }

    private static bool TryGetSeasonWindow(string season, out DateTime seasonStartUtc, out DateTime seasonEndUtc)
    {
        seasonStartUtc = default;
        seasonEndUtc = default;

        var parts = season.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var startYear) ||
            !int.TryParse(parts[1], out var endYear) ||
            endYear != startYear + 1)
        {
            return false;
        }

        seasonStartUtc = new DateTime(startYear, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        seasonEndUtc = new DateTime(endYear, 7, 31, 23, 59, 59, 999, DateTimeKind.Utc).AddTicks(9999);
        return true;
    }

    private static string GetSeasonLabelForDate(DateTime gameDateTimeUtc)
        => gameDateTimeUtc.Month >= 8
            ? $"{gameDateTimeUtc.Year}-{gameDateTimeUtc.Year + 1}"
            : $"{gameDateTimeUtc.Year - 1}-{gameDateTimeUtc.Year}";

    private static void SetSingleYearSeasonLabels(
        string normalizedSeason,
        out string previousSingleYearSeasonLabel,
        out string currentSingleYearSeasonLabel)
    {
        previousSingleYearSeasonLabel = string.Empty;
        currentSingleYearSeasonLabel = string.Empty;

        var parts = normalizedSeason.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var startYear))
        {
            return;
        }

        previousSingleYearSeasonLabel = (startYear - 1).ToString(CultureInfo.InvariantCulture);
        currentSingleYearSeasonLabel = startYear.ToString(CultureInfo.InvariantCulture);
    }

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
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return string.Empty;
        }

        var normalized = CountryCodeCatalog.Normalize(countryCode)!;
        if (InternationalTeamCatalog.TryGetCanonicalName(countryCode, out var internationalCountryName))
        {
            return internationalCountryName;
        }

        if (CountryNameOverrides.TryGetValue(normalized, out var countryName))
        {
            return countryName;
        }

        return CountryNames.TryGetValue(normalized, out countryName)
            ? countryName
            : countryCode.Trim();
    }

    private static readonly IReadOnlyDictionary<string, string> CountryNameOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CZ"] = "Czech Republic",
        ["CZE"] = "Czech Republic",
        ["EL"] = "Greece",
        ["GR"] = "Greece",
        ["GRC"] = "Greece",
        ["RU"] = "Russia",
        ["RUS"] = "Russia",
        ["SCT"] = "Scotland",
        ["UK"] = "United Kingdom",
        ["GB"] = "United Kingdom",
        ["GBR"] = "United Kingdom",
        ["USA"] = "United States",
        ["US"] = "United States"
    };

    private static readonly IReadOnlyDictionary<string, string> CountryNames = BuildCountryNameLookup();

    private static IReadOnlyDictionary<string, string> BuildCountryNameLookup()
    {
        var countries = new Dictionary<string, string>(CountryNameOverrides, StringComparer.OrdinalIgnoreCase);

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            RegionInfo region;
            try
            {
                region = new RegionInfo(culture.Name);
            }
            catch (ArgumentException)
            {
                continue;
            }

            countries.TryAdd(region.TwoLetterISORegionName, region.EnglishName);
            countries.TryAdd(region.ThreeLetterISORegionName, region.EnglishName);
            countries.TryAdd(region.EnglishName.ToUpperInvariant(), region.EnglishName);
        }

        foreach (var overrideEntry in CountryNameOverrides)
        {
            countries[overrideEntry.Key] = overrideEntry.Value;
        }

        return countries;
    }
}

