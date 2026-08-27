using BasketElo.Api.Auth;
using BasketElo.Api.Elo;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Elo;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using BasketElo.Infrastructure.Teams;
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
    EloResponseCache responseCache,
    IConfiguration? configuration = null,
    IWebHostEnvironment? hostingEnvironment = null) : ControllerBase
{
    private const int PublicHistoryWindowDays = 30;

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
        [FromQuery] int? teamLimit,
        CancellationToken cancellationToken = default)
    {
        var poolKey = ResolvePoolOrDefault(pool);
        if (poolKey is null)
        {
            return BadRequest($"Unsupported ELO pool '{pool}'.");
        }

        var cacheKey = EloResponseCache.BrowseKey(poolKey, country, competition, season, teamLimit);
        if (responseCache.TryGet<EloBrowseResponse>(cacheKey, out var cachedResponse) && cachedResponse is not null)
        {
            return Ok(cachedResponse);
        }

        var uncachedResult = await GetBrowseUncached(
            poolKey,
            country,
            competition,
            season,
            teamLimit,
            cancellationToken);
        if (uncachedResult.Result is OkObjectResult { Value: EloBrowseResponse response })
        {
            responseCache.Set(cacheKey, response, poolKey, EloRulesetVersions.Default);
        }

        return uncachedResult;
    }

    private async Task<ActionResult<EloBrowseResponse>> GetBrowseUncached(
        string poolKey,
        string? country,
        string? competition,
        string? season,
        int? teamLimit,
        CancellationToken cancellationToken)
    {
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

        var poolTeamIds = teamIdsByCompetition.Values
            .SelectMany(ids => ids)
            .Distinct()
            .ToList();
        var currentRatingRows = await dbContext.TeamRatings
            .AsNoTracking()
            .Where(x => x.EloPoolKey == poolKey &&
                x.RulesetVersion == EloRulesetVersions.Default &&
                poolTeamIds.Contains(x.TeamId))
            .Select(x => new
            {
                x.TeamId,
                x.Elo,
                x.GamesPlayed,
                Name = x.Team.CanonicalName,
                CountryCode = x.Team.CountryCode
            })
            .ToListAsync(cancellationToken);

        var currentRatingsByTeamId = currentRatingRows.ToDictionary(x => x.TeamId);

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
            .Select(row =>
            {
                var teams = teamIdsByCompetition.GetValueOrDefault(row.Id)?
                    .Where(currentRatingsByTeamId.ContainsKey)
                    .Select(teamId => currentRatingsByTeamId[teamId])
                    .OrderByDescending(team => team.Elo)
                    .ThenBy(team => team.Name)
                    .Take(Math.Clamp(teamLimit ?? 100, 1, 100))
                    .Select((team, index) => new EloBrowseTeam(
                        team.TeamId,
                        team.Name,
                        DisplayCountryFromCode(team.CountryCode),
                        team.Elo,
                        team.GamesPlayed,
                        index + 1))
                    .ToList() ?? [];

                return new EloBrowseCompetition(
                    row.Name,
                    BrowseCountryName(row.CountryCode),
                    row.CountryCode,
                    row.Type,
                    row.Tier,
                    teamIdsByCompetition.GetValueOrDefault(row.Id)?.Count ?? 0,
                    gameStatsByCompetition.GetValueOrDefault(row.Id)?.GameCount ?? 0,
                    gameStatsByCompetition.GetValueOrDefault(row.Id)?.LatestGameUtc,
                    seasonsByCompetition.GetValueOrDefault(row.Id) ?? [],
                    row.SupportPolicy)
                {
                    Teams = teams
                };
            })
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
                .Where(x => x.GameDateTimeUtc >= GetPublicHistoryStartUtc() &&
                    x.GameDateTimeUtc <= DateTime.UtcNow)
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

            var contextTeamRows = await contextQuery
                .Select(x => new { x.HomeTeamId, x.AwayTeamId })
                .ToListAsync(cancellationToken);
            var contextTeamIds = contextTeamRows
                .SelectMany(x => new[] { x.HomeTeamId, x.AwayTeamId })
                .Distinct()
                .ToList();
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
                .Take(Math.Clamp(teamLimit ?? 12, 1, 100))
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
                teamScope))
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
            if (uncachedResult.Result is OkObjectResult { Value: EloRankingsResponse response })
            {
                var responseKey = EloResponseCache.RankingsKey(
                    response.EloPoolKey,
                    response.RulesetVersion,
                    response.TeamScope,
                    country,
                    competition,
                    season,
                    tournamentCycle,
                    fromUtc,
                    toUtc,
                    asOfDate,
                    minimumGames,
                    team,
                    response.Page,
                    response.PageSize);
                responseCache.Set(responseKey, response, response.EloPoolKey, response.RulesetVersion);
            }

            return uncachedResult;
        }

        return await GetRankingsUncached(
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
    }

    [NonAction]
    public async Task WarmRankingCachesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var pool in EloPoolCatalog.All.OrderBy(x => x.DisplayOrder))
        {
            var selectedRuleset = await ResolveReadableRulesetAsync(null, pool.Key, cancellationToken);
            if (selectedRuleset is not null)
            {
                await BuildRankingFilterOptionsAsync(pool.Key, selectedRuleset, cancellationToken);
            }
        }

        foreach (var poolKey in new[] { EloPoolKeys.Default, EloPoolKeys.EuropeClubs })
        {
            await GetRankings(
                rulesetVersion: null,
                pool: poolKey,
                country: null,
                competition: null,
                season: null,
                fromUtc: null,
                toUtc: null,
                asOfDate: null,
                minGames: null,
                team: null,
                teams: EloTeamScopes.Current,
                page: 1,
                pageSize: 50,
                tournamentCycle: null,
                cancellationToken: cancellationToken);
        }
    }

    private async Task<ActionResult<EloRankingsResponse>> GetRankingsUncached(
        [FromQuery] string? rulesetVersion,
        [FromQuery] string? pool,
        [FromQuery] string? country,
        [FromQuery] string? competition,
        [FromQuery] string? season,
        [FromQuery] string? tournamentCycle,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] DateTime? asOfDate,
        [FromQuery] int? minGames,
        [FromQuery] string? team,
        [FromQuery] string? teams = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var poolKey = ResolvePoolOrDefault(pool);
        if (poolKey is null)
        {
            return BadRequest($"Unsupported ELO pool '{pool}'.");
        }
        var teamScope = EloTeamScopes.Normalize(teams);
        var activeTeamsOnly = UsesTeamScope(poolKey) && teamScope == EloTeamScopes.Current;
        if (!await CompetitionBelongsToPoolAsync(competition, poolKey, cancellationToken))
        {
            return BadRequest($"Competition '{competition}' does not belong to ELO pool '{poolKey}'.");
        }
        var selectedRuleset = await ResolveReadableRulesetAsync(rulesetVersion, poolKey, cancellationToken);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var minimumGames = Math.Max(0, minGames ?? 0);
        var currentEuropeanTeamIds = poolKey == EloPoolKeys.EuropeClubs
            ? await GetCurrentEuropeanTeamIdsAsync(competition, cancellationToken)
            : null;
        var currentNationalTeamIds = poolKey == EloPoolKeys.NationalTeams
            ? await GetCurrentNationalTeamIdsAsync(competition, cancellationToken)
            : null;
        var teamFilterIds = await TeamSearchResolver.ResolveTeamIdsAsync(dbContext, team, cancellationToken);

        if (asOfDate.HasValue)
        {
            var requestedAsOfUtc = DateTime.SpecifyKind(asOfDate.Value.Date, DateTimeKind.Utc);
            var cutoffUtc = requestedAsOfUtc.AddDays(1).AddTicks(-1);

            var archiveHistoryRows = await dbContext.RatingHistories
                .AsNoTracking()
                .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == selectedRuleset && x.GameDateTimeUtc <= cutoffUtc)
                .Select(x => new HistoricalRankingRow(
                    x.TeamId,
                    x.Team.CanonicalName,
                    x.Team.CountryCode,
                    x.Team.IsActive,
                    x.GameId,
                    x.GameDateTimeUtc,
                    x.PostElo,
                    x.GamesPlayedBefore + 1))
                .ToListAsync(cancellationToken);

            var globalArchiveRatings = archiveHistoryRows
                .Where(x => !activeTeamsOnly || IsCurrentTeam(poolKey, x.TeamId, currentEuropeanTeamIds, currentNationalTeamIds, x.IsActive))
                .GroupBy(x => x.TeamId)
                .Select(group =>
                {
                    var latest = group
                        .OrderByDescending(x => x.GameDateTimeUtc)
                        .ThenByDescending(x => x.GameId)
                        .First();
                    return latest with
                    {
                        IsActive = IsCurrentTeam(poolKey, latest.TeamId, currentEuropeanTeamIds, currentNationalTeamIds, latest.IsActive)
                    };
                })
                .OrderByDescending(x => x.Elo)
                .ThenBy(x => x.TeamName)
                .ToList();

            var archiveGlobalRanks = globalArchiveRatings
                .Select((rating, index) => new { rating.TeamId, Rank = index + 1 })
                .ToDictionary(x => x.TeamId, x => x.Rank);

            var archiveFilteredTeamIds = globalArchiveRatings.Select(x => x.TeamId).ToHashSet();

            if (!string.IsNullOrWhiteSpace(country))
            {
                archiveFilteredTeamIds.IntersectWith(globalArchiveRatings
                    .Where(x => IsCountryMatch(x.CountryCode, country))
                    .Select(x => x.TeamId));
            }

            if (!string.IsNullOrWhiteSpace(team))
            {
                archiveFilteredTeamIds.IntersectWith(teamFilterIds);
            }

            if (minimumGames > 0)
            {
                archiveFilteredTeamIds.IntersectWith(globalArchiveRatings
                    .Where(x => x.GamesPlayed >= minimumGames)
                    .Select(x => x.TeamId));
            }

            if (HasHistoryFilter(competition, season, tournamentCycle, fromUtc, toUtc))
            {
                var historyTeamIds = await BuildHistoryFilterQuery(poolKey, selectedRuleset, competition, season, tournamentCycle, fromUtc, toUtc)
                    .Where(x => x.GameDateTimeUtc <= cutoffUtc)
                    .Select(x => x.TeamId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                archiveFilteredTeamIds.IntersectWith(historyTeamIds);
            }

            var archiveFilteredRatings = globalArchiveRatings
                .Where(x => archiveFilteredTeamIds.Contains(x.TeamId))
                .OrderByDescending(x => x.Elo)
                .ThenBy(x => x.TeamName)
                .ToList();

            var archiveFilteredCount = archiveFilteredRatings.Count;
            var archiveTotalPages = Math.Max(1, (int)Math.Ceiling(archiveFilteredCount / (double)pageSize));
            page = Math.Min(page, archiveTotalPages);

            var archivePageRatings = archiveFilteredRatings
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var archiveRecentMovement = await GetRecentMovementAsync(
                poolKey,
                selectedRuleset,
                archivePageRatings.Select(x => x.TeamId).ToList(),
                cutoffUtc,
                cancellationToken);

            var archiveRecentForm = await GetRecentFormAsync(
                poolKey,
                selectedRuleset,
                archivePageRatings.Select(x => x.TeamId).ToList(),
                cutoffUtc,
                cancellationToken);

            var archiveRows = archivePageRatings
                .Select((rating, index) => new EloRankingRow(
                    rating.TeamId,
                    ((page - 1) * pageSize) + index + 1,
                    archiveGlobalRanks[rating.TeamId],
                    rating.TeamName,
                    DisplayCountryFromCode(rating.CountryCode),
                    rating.Elo,
                    rating.GamesPlayed,
                    archiveRecentMovement.GetValueOrDefault(rating.TeamId),
                    rating.GameDateTimeUtc,
                    rating.IsActive,
                    archiveRecentForm.GetValueOrDefault(rating.TeamId)))
                .ToList();

            var latestRatedGameUtc = globalArchiveRatings.Count == 0
                ? null
                : globalArchiveRatings.Max(x => (DateTime?)x.GameDateTimeUtc);

            return Ok(new EloRankingsResponse(
                poolKey,
                EloPoolKeys.DisplayName(poolKey),
                selectedRuleset,
                teamScope,
                archiveRows,
                await BuildRankingFilterOptionsAsync(poolKey, selectedRuleset, cancellationToken),
                new EloRankingSummary(
                    globalArchiveRatings.Count,
                    archiveFilteredCount,
                    latestRatedGameUtc,
                    globalArchiveRatings.FirstOrDefault()?.TeamName,
                    globalArchiveRatings.FirstOrDefault()?.Elo,
                    IsFiltered(country, competition, season, tournamentCycle, fromUtc, toUtc, minimumGames, team)),
                new EloRankingArchiveMetadata(
                    "archive",
                    requestedAsOfUtc,
                    latestRatedGameUtc,
                    globalArchiveRatings.Count == 0 ? "No rating history exists at or before the selected date." : null),
                page,
                pageSize,
                archiveFilteredCount,
                archiveTotalPages));
        }

        var globalRatingsQuery = dbContext.TeamRatings
            .AsNoTracking()
            .Where(x =>
                x.EloPoolKey == poolKey &&
                x.RulesetVersion == selectedRuleset);
        if (activeTeamsOnly)
        {
            globalRatingsQuery = poolKey == EloPoolKeys.Nba
                ? globalRatingsQuery.Where(x => x.Team.IsActive)
                : poolKey == EloPoolKeys.EuropeClubs
                    ? globalRatingsQuery.Where(x => currentEuropeanTeamIds!.Contains(x.TeamId))
                    : globalRatingsQuery.Where(x => currentNationalTeamIds!.Contains(x.TeamId));
        }

        var globalRatings = await globalRatingsQuery
            .OrderByDescending(x => x.Elo)
            .ThenBy(x => x.Team.CanonicalName)
            .Select(x => new CurrentRankingRow(
                x.TeamId,
                x.Team.CanonicalName,
                x.Team.CountryCode,
                x.Team.IsActive,
                x.Elo,
                x.GamesPlayed,
                x.LastGame == null ? null : x.LastGame.GameDateTimeUtc))
            .ToListAsync(cancellationToken);

        var globalRanks = globalRatings
            .Select((rating, index) => new { rating.TeamId, Rank = index + 1 })
            .ToDictionary(x => x.TeamId, x => x.Rank);

        var filteredTeamIds = globalRatings.Select(x => x.TeamId).ToHashSet();

        if (!string.IsNullOrWhiteSpace(country))
        {
            filteredTeamIds.IntersectWith(globalRatings
                .Where(x => IsCountryMatch(x.CountryCode, country))
                .Select(x => x.TeamId));
        }

        if (!string.IsNullOrWhiteSpace(team))
        {
            filteredTeamIds.IntersectWith(teamFilterIds);
        }

        if (minimumGames > 0)
        {
            filteredTeamIds.IntersectWith(globalRatings
                .Where(x => x.GamesPlayed >= minimumGames)
                .Select(x => x.TeamId));
        }

        if (HasHistoryFilter(competition, season, tournamentCycle, fromUtc, toUtc))
        {
            var historyTeamIds = await BuildHistoryFilterQuery(poolKey, selectedRuleset, competition, season, tournamentCycle, fromUtc, toUtc)
                .Select(x => x.TeamId)
                .Distinct()
                .ToListAsync(cancellationToken);

            filteredTeamIds.IntersectWith(historyTeamIds);
        }

        var filteredRatings = globalRatings
            .Where(x => filteredTeamIds.Contains(x.TeamId))
            .OrderByDescending(x => x.Elo)
            .ThenBy(x => x.TeamName)
            .ToList();

        var filteredCount = filteredRatings.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var pageRatings = filteredRatings
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var recentMovement = await GetRecentMovementAsync(
            poolKey,
            selectedRuleset,
            pageRatings.Select(x => x.TeamId).ToList(),
            null,
            cancellationToken);

        var recentForm = await GetRecentFormAsync(
            poolKey,
            selectedRuleset,
            pageRatings.Select(x => x.TeamId).ToList(),
            null,
            cancellationToken);

        var rows = pageRatings
            .Select((rating, index) => new EloRankingRow(
                rating.TeamId,
                ((page - 1) * pageSize) + index + 1,
                globalRanks[rating.TeamId],
                rating.TeamName,
                DisplayCountryFromCode(rating.CountryCode),
                rating.Elo,
                rating.GamesPlayed,
                recentMovement.GetValueOrDefault(rating.TeamId),
                rating.LastGameUtc,
                IsCurrentTeam(poolKey, rating.TeamId, currentEuropeanTeamIds, currentNationalTeamIds, rating.IsActive),
                recentForm.GetValueOrDefault(rating.TeamId)))
            .ToList();

        return Ok(new EloRankingsResponse(
            poolKey,
            EloPoolKeys.DisplayName(poolKey),
            selectedRuleset,
            teamScope,
            rows,
            await BuildRankingFilterOptionsAsync(poolKey, selectedRuleset, cancellationToken),
            new EloRankingSummary(
                globalRatings.Count,
                filteredCount,
                globalRatings.Select(x => x.LastGameUtc).Where(x => x.HasValue).Max(),
                globalRatings.FirstOrDefault()?.TeamName,
                globalRatings.FirstOrDefault()?.Elo,
                IsFiltered(country, competition, season, tournamentCycle, fromUtc, toUtc, minimumGames, team)),
            new EloRankingArchiveMetadata("current", null, null, null),
            page,
            pageSize,
            filteredCount,
            totalPages));
    }

    [HttpGet("rankings/evolution")]
    public async Task<ActionResult<EloRankingsEvolutionResponse>> GetRankingEvolution(
        [FromQuery] string? rulesetVersion,
        [FromQuery] string? pool,
        [FromQuery] string? teamIds,
        [FromQuery] string? country,
        [FromQuery] string? competition,
        [FromQuery] string? season,
        [FromQuery] string? tournamentCycle,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] string? excludeTeamIds,
        [FromQuery] int pointsPerTeam = EloEvolutionLimits.DefaultPointsPerTeam,
        CancellationToken cancellationToken = default)
    {
        season = NormalizeSeasonFilterInput(season);
        var poolKey = ResolvePoolOrDefault(pool);
        if (poolKey is null)
        {
            return BadRequest($"Unsupported ELO pool '{pool}'.");
        }
        if (!await CompetitionBelongsToPoolAsync(competition, poolKey, cancellationToken))
        {
            return BadRequest($"Competition '{competition}' does not belong to ELO pool '{poolKey}'.");
        }
        var selectedRuleset = await ResolveReadableRulesetAsync(rulesetVersion, poolKey, cancellationToken);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }

        var selectedTeamIds = ParseTeamIds(teamIds).ToList();
        if (selectedTeamIds.Count == 0 &&
            (!string.IsNullOrWhiteSpace(country) || !string.IsNullOrWhiteSpace(competition)))
        {
            selectedTeamIds = await GetEvolutionTeamIdsAsync(
                poolKey,
                selectedRuleset,
                country,
                competition,
                cancellationToken);
        }

        var excludedTeamIds = ParseTeamIds(excludeTeamIds);
        if (excludedTeamIds.Count > 0)
        {
            selectedTeamIds = selectedTeamIds
                .Except(excludedTeamIds)
                .ToList();
        }
        if (selectedTeamIds.Count == 0)
        {
            return Ok(new EloRankingsEvolutionResponse(poolKey, selectedRuleset, []));
        }

        var includeDiagnostics = IsAdminDiagnosticsRequest();
        pointsPerTeam = EloEvolutionLimits.NormalizePointsPerTeam(pointsPerTeam);
        var canCacheDefaultEvolution = !includeDiagnostics &&
            IsDefaultResponseCachePool(poolKey) &&
            selectedRuleset == EloRulesetVersions.Default &&
            !string.IsNullOrWhiteSpace(teamIds) &&
            string.IsNullOrWhiteSpace(country) &&
            string.IsNullOrWhiteSpace(competition) &&
            string.IsNullOrWhiteSpace(season) &&
            string.IsNullOrWhiteSpace(tournamentCycle) &&
            !fromUtc.HasValue &&
            !toUtc.HasValue &&
            pointsPerTeam == EloEvolutionLimits.DefaultPointsPerTeam &&
            await IsDefaultEvolutionTeamSetAsync(poolKey, selectedRuleset, selectedTeamIds, cancellationToken);
        var evolutionCacheKey = canCacheDefaultEvolution
            ? EloResponseCache.EvolutionKey(poolKey, selectedRuleset, selectedTeamIds, competition, season, tournamentCycle, fromUtc, toUtc, pointsPerTeam)
            : null;
        if (evolutionCacheKey is not null &&
            responseCache.TryGet<EloRankingsEvolutionResponse>(evolutionCacheKey, out var cachedEvolution) &&
            cachedEvolution is not null)
        {
            return Ok(cachedEvolution);
        }

        var cutoffUtc = toUtc.HasValue
            ? DateTime.SpecifyKind(toUtc.Value.Date, DateTimeKind.Utc).AddDays(1).AddTicks(-1)
            : DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        var startUtc = fromUtc.HasValue
            ? DateTime.SpecifyKind(fromUtc.Value.Date, DateTimeKind.Utc)
            : new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var hasCompetitionFilter = !string.IsNullOrWhiteSpace(competition);
        var hasSeasonFilter = !string.IsNullOrWhiteSpace(season);
        var hasCycleFilter = !string.IsNullOrWhiteSpace(tournamentCycle);
        var evolutionFilterJoins = hasCompetitionFilter || hasSeasonFilter || hasCycleFilter
            ? "                INNER JOIN games g ON g.\"Id\" = rh.\"GameId\"\n"
            : string.Empty;
        if (hasSeasonFilter)
        {
            evolutionFilterJoins += "                INNER JOIN seasons s ON s.\"Id\" = g.\"SeasonId\"\n";
        }

        if (hasCompetitionFilter)
        {
            evolutionFilterJoins += "                INNER JOIN competitions c ON c.\"Id\" = g.\"CompetitionId\"\n";
        }

        if (hasCycleFilter)
        {
            evolutionFilterJoins += "                LEFT JOIN tournament_cycles tc ON tc.\"Id\" = g.\"TournamentCycleId\"\n";
        }

        var evolutionFilterPredicates = string.Concat(
            hasCompetitionFilter ? "                      AND c.\"Name\" = @competition\n" : string.Empty,
            hasSeasonFilter ? "                      AND (s.\"Label\" = @season OR s.\"Label\" = split_part(@season, '-', 1))\n" : string.Empty,
            hasCycleFilter ? "                      AND tc.\"Key\" = @tournamentCycle\n" : string.Empty);
        var evolutionParameters = new List<object>
        {
            new NpgsqlParameter("poolKey", poolKey),
            new NpgsqlParameter("rulesetVersion", selectedRuleset),
            new NpgsqlParameter("teamIds", selectedTeamIds.ToArray()) { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid },
            new NpgsqlParameter("startUtc", startUtc),
            new NpgsqlParameter("cutoffUtc", cutoffUtc),
            new NpgsqlParameter("pointsPerTeam", pointsPerTeam)
        };
        if (hasCompetitionFilter)
        {
            evolutionParameters.Add(new NpgsqlParameter("competition", competition!));
        }

        if (hasSeasonFilter)
        {
            evolutionParameters.Add(new NpgsqlParameter("season", season!.Trim()));
        }

        if (hasCycleFilter)
        {
            evolutionParameters.Add(new NpgsqlParameter("tournamentCycle", tournamentCycle!.Trim()));
        }

        var evolutionSql = $"""
                WITH ranked AS (
                    SELECT
                        rh."TeamId",
                        t."CanonicalName" AS "TeamName",
                        rh."GameId",
                        rh."GameDateTimeUtc",
                        rh."PostElo" AS "Elo",
                        rh."EloDelta",
                        rh."RatingPositionAfter" AS "Rank",
                        row_number() OVER (
                            PARTITION BY rh."TeamId"
                            ORDER BY rh."GameDateTimeUtc", rh."PostElo"
                        ) AS "RowNumber",
                        count(*) OVER (PARTITION BY rh."TeamId") AS "TotalRows"
                    FROM rating_history rh
                    INNER JOIN teams t ON t."Id" = rh."TeamId"
                {evolutionFilterJoins}
                    WHERE rh."EloPoolKey" = @poolKey
                      AND rh."RulesetVersion" = @rulesetVersion
                      AND rh."TeamId" = ANY(@teamIds)
                      AND rh."GameDateTimeUtc" >= @startUtc
                      AND rh."GameDateTimeUtc" <= @cutoffUtc
                {evolutionFilterPredicates}
                ), bucketed AS (
                    SELECT
                        *,
                        CASE
                            WHEN "TotalRows" <= @pointsPerTeam THEN "RowNumber"
                            ELSE round(
                                ("RowNumber" - 1) * (@pointsPerTeam - 1)::numeric /
                                ("TotalRows" - 1)
                            )::bigint
                        END AS "SampleBucket"
                    FROM ranked
                ), sampled AS (
                    SELECT DISTINCT ON ("TeamId", "SampleBucket")
                        "TeamId", "TeamName", "GameId", "GameDateTimeUtc", "Elo", "EloDelta", "Rank", "TotalRows"
                    FROM bucketed
                    ORDER BY
                        "TeamId",
                        "SampleBucket",
                        abs(
                            ("RowNumber" - 1)::numeric -
                            "SampleBucket" * ("TotalRows" - 1)::numeric /
                            greatest(@pointsPerTeam - 1, 1)
                        )
                )
                SELECT "TeamId", "TeamName", "GameId", "GameDateTimeUtc", "Elo", "EloDelta", "Rank", "TotalRows"
                FROM sampled
                ORDER BY "TeamId", "GameDateTimeUtc", "Elo"
                """;
        var rows = await dbContext.Database
            .SqlQueryRaw<EvolutionHistorySqlRow>(evolutionSql, evolutionParameters.ToArray())
            .ToListAsync(cancellationToken);

        var series = rows
            .GroupBy(x => new { x.TeamId, x.TeamName })
            .Select(group => new EloTeamEvolutionSeries(
                group.Key.TeamId,
                group.Key.TeamName,
                group
                    .OrderBy(x => x.GameDateTimeUtc)
                    .ThenBy(x => x.Elo)
                    .Select(x => new EloTeamEvolutionPoint(x.GameDateTimeUtc, x.Elo, x.EloDelta, x.Rank, x.GameId))
                    .ToList(),
                includeDiagnostics ? checked((int)group.First().TotalRows) : 0))
            .OrderBy(x => selectedTeamIds.IndexOf(x.TeamId))
            .ToList();

        var response = new EloRankingsEvolutionResponse(poolKey, selectedRuleset, series);
        if (evolutionCacheKey is not null)
        {
            responseCache.Set(evolutionCacheKey, response, poolKey, selectedRuleset);
        }

        return Ok(response);
    }

    private bool IsAdminDiagnosticsRequest()
    {
        var expectedSecret = configuration?["InternalAuth:SharedSecret"];
        if (string.IsNullOrWhiteSpace(expectedSecret))
        {
            if (hostingEnvironment?.IsDevelopment() != true)
            {
                return false;
            }
        }
        else if (!string.Equals(
            Request.Headers[InternalAuthHeaders.SharedSecret].ToString(),
            expectedSecret,
            StringComparison.Ordinal))
        {
            return false;
        }

        var roles = Request.Headers[InternalAuthHeaders.Roles]
            .ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return roles.Contains(ApplicationRoleKeys.Admin, StringComparer.OrdinalIgnoreCase);
    }

    [HttpGet("rankings/movers")]
    public async Task<ActionResult<EloMoversResponse>> GetMovers(
        [FromQuery] string? rulesetVersion,
        [FromQuery] string? pool,
        [FromQuery] string? direction,
        [FromQuery] int windowDays = 30,
        [FromQuery] string? country = null,
        [FromQuery] string? competition = null,
        [FromQuery] string? season = null,
        [FromQuery] string? tournamentCycle = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int? minGames = null,
        [FromQuery] string? team = null,
        [FromQuery] string? teams = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        season = NormalizeSeasonFilterInput(season);
        var poolKey = ResolvePoolOrDefault(pool);
        if (poolKey is null)
        {
            return BadRequest($"Unsupported ELO pool '{pool}'.");
        }
        var teamScope = EloTeamScopes.Normalize(teams);
        var activeTeamsOnly = UsesTeamScope(poolKey) && teamScope == EloTeamScopes.Current;
        if (!await CompetitionBelongsToPoolAsync(competition, poolKey, cancellationToken))
        {
            return BadRequest($"Competition '{competition}' does not belong to ELO pool '{poolKey}'.");
        }
        var selectedRuleset = await ResolveReadableRulesetAsync(rulesetVersion, poolKey, cancellationToken);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }

        var normalizedDirection = string.Equals(direction, "fallers", StringComparison.OrdinalIgnoreCase)
            ? "fallers"
            : "risers";
        windowDays = Math.Clamp(windowDays, 7, 3650);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var minimumGames = Math.Max(0, minGames ?? 0);
        var currentEuropeanTeamIds = poolKey == EloPoolKeys.EuropeClubs
            ? await GetCurrentEuropeanTeamIdsAsync(competition, cancellationToken)
            : null;
        var currentNationalTeamIds = poolKey == EloPoolKeys.NationalTeams
            ? await GetCurrentNationalTeamIdsAsync(competition, cancellationToken)
            : null;
        var teamFilterIds = await TeamSearchResolver.ResolveTeamIdsAsync(dbContext, team, cancellationToken);

        var latestGameUtc = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == selectedRuleset)
            .MaxAsync(x => (DateTime?)x.GameDateTimeUtc, cancellationToken);

        if (latestGameUtc is null)
        {
            var now = DateTime.UtcNow;
            return Ok(new EloMoversResponse(
                poolKey,
                selectedRuleset,
                teamScope,
                normalizedDirection,
                now.AddDays(-windowDays),
                now,
                [],
                new EloMoversSummary(0, 0, 0, IsFiltered(country, competition, season, tournamentCycle, fromUtc, toUtc, minimumGames, team)),
                1,
                pageSize,
                0,
                1));
        }

        var windowEnd = toUtc.HasValue
            ? DateTime.SpecifyKind(toUtc.Value.Date, DateTimeKind.Utc).AddDays(1).AddTicks(-1)
            : latestGameUtc.Value;
        var windowStart = fromUtc.HasValue
            ? DateTime.SpecifyKind(fromUtc.Value.Date, DateTimeKind.Utc)
            : windowEnd.AddDays(-windowDays);

        if (windowStart > windowEnd)
        {
            return BadRequest("fromUtc must be before toUtc.");
        }

        var currentRatingsQuery = dbContext.TeamRatings
            .AsNoTracking()
            .Include(x => x.Team)
            .Where(x =>
                x.EloPoolKey == poolKey &&
                x.RulesetVersion == selectedRuleset);
        if (activeTeamsOnly)
        {
            currentRatingsQuery = poolKey == EloPoolKeys.Nba
                ? currentRatingsQuery.Where(x => x.Team.IsActive)
                : currentRatingsQuery.Where(x => (poolKey == EloPoolKeys.EuropeClubs
                    ? currentEuropeanTeamIds!.Contains(x.TeamId)
                    : currentNationalTeamIds!.Contains(x.TeamId)));
        }

        var currentRatings = await currentRatingsQuery
            .ToListAsync(cancellationToken);

        var filteredTeamIds = currentRatings.Select(x => x.TeamId).ToHashSet();

        if (!string.IsNullOrWhiteSpace(country))
        {
            filteredTeamIds.IntersectWith(currentRatings
                .Where(x => IsCountryMatch(x.Team.CountryCode, country))
                .Select(x => x.TeamId));
        }

        if (!string.IsNullOrWhiteSpace(team))
        {
            filteredTeamIds.IntersectWith(teamFilterIds);
        }

        if (minimumGames > 0)
        {
            filteredTeamIds.IntersectWith(currentRatings
                .Where(x => x.GamesPlayed >= minimumGames)
                .Select(x => x.TeamId));
        }

        var ratingByTeam = currentRatings.ToDictionary(x => x.TeamId);
        var movementQuery = dbContext.RatingHistories
            .AsNoTracking()
            .Where(x =>
                x.EloPoolKey == poolKey &&
                x.RulesetVersion == selectedRuleset &&
                filteredTeamIds.Contains(x.TeamId) &&
                x.GameDateTimeUtc >= windowStart &&
                x.GameDateTimeUtc <= windowEnd);

        if (!string.IsNullOrWhiteSpace(competition))
        {
            movementQuery = movementQuery.Where(x => x.Game.Competition.Name == competition);
        }

        if (!string.IsNullOrWhiteSpace(season))
        {
            movementQuery = ApplySeasonFilter(movementQuery, season);
        }

        if (!string.IsNullOrWhiteSpace(tournamentCycle))
        {
            movementQuery = movementQuery.Where(x => x.Game.TournamentCycle != null && x.Game.TournamentCycle.Key == tournamentCycle.Trim());
        }

        var movementRows = await movementQuery
            .Select(x => new MoverHistoryRow(
                x.TeamId,
                x.Team.CanonicalName,
                x.Team.CountryCode,
                x.GameId,
                x.GameDateTimeUtc,
                x.PreElo,
                x.PostElo,
                x.EloDelta))
            .ToListAsync(cancellationToken);

        var movers = movementRows
            .GroupBy(x => new { x.TeamId, x.TeamName, x.CountryCode })
            .Select(group =>
            {
                var ordered = group.OrderBy(x => x.GameDateTimeUtc).ThenBy(x => x.GameId).ToList();
                var latest = ordered[^1];
                var currentElo = ratingByTeam.TryGetValue(group.Key.TeamId, out var currentRating)
                    ? currentRating.Elo
                    : latest.PostElo;

                return new
                {
                    group.Key.TeamId,
                    group.Key.TeamName,
                    Country = DisplayCountryFromCode(group.Key.CountryCode),
                    CurrentElo = currentElo,
                    StartElo = ordered[0].PreElo,
                    EndElo = latest.PostElo,
                    EloChange = ordered.Sum(x => x.EloDelta),
                    GamesInWindow = ordered.Count,
                    FirstGameUtc = ordered[0].GameDateTimeUtc,
                    LastGameUtc = latest.GameDateTimeUtc
                };
            })
            .Where(x => x.GamesInWindow > 0)
            .ToList();

        movers = normalizedDirection == "fallers"
            ? movers.OrderBy(x => x.EloChange).ThenBy(x => x.TeamName).ToList()
            : movers.OrderByDescending(x => x.EloChange).ThenBy(x => x.TeamName).ToList();

        var totalCount = movers.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);
        var pageRows = movers
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select((row, index) => new EloMoverRow(
                row.TeamId,
                ((page - 1) * pageSize) + index + 1,
                row.TeamName,
                row.Country,
                row.CurrentElo,
                row.StartElo,
                row.EndElo,
                row.EloChange,
                row.GamesInWindow,
                row.FirstGameUtc,
                row.LastGameUtc,
                IsCurrentTeam(
                    poolKey,
                    row.TeamId,
                    currentEuropeanTeamIds,
                    currentNationalTeamIds,
                    ratingByTeam.GetValueOrDefault(row.TeamId)?.Team.IsActive ?? true)))
            .ToList();

        return Ok(new EloMoversResponse(
            poolKey,
            selectedRuleset,
            teamScope,
            normalizedDirection,
            windowStart,
            windowEnd,
            pageRows,
            new EloMoversSummary(
                movers.Count,
                filteredTeamIds.Count,
                movementRows.Select(x => x.GameId).Distinct().Count(),
                IsFiltered(country, competition, season, tournamentCycle, fromUtc, toUtc, minimumGames, team)),
            page,
            pageSize,
            totalCount,
            totalPages));
    }

    [HttpGet("results")]
    public async Task<ActionResult<EloResultsResponse>> GetResults(
        [FromQuery] string? rulesetVersion,
        [FromQuery] string? pool,
        [FromQuery] string? country,
        [FromQuery] string? competition,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] string? team,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var poolKey = ResolvePoolOrDefault(pool);
        if (poolKey is null)
        {
            return BadRequest($"Unsupported ELO pool '{pool}'.");
        }

        var selectedRuleset = await ResolveReadableRulesetAsync(rulesetVersion, poolKey, cancellationToken);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }

        // Results mirror the bounded public history surface used by the graph:
        // newest first, with one capped response and no paging loophole.
        page = 1;
        pageSize = EloPublicDataLimits.ResultsPerResponse;

        var competitionRows = await dbContext.Competitions
            .AsNoTracking()
            .Where(x => x.IsActive && x.EloPoolKey == poolKey)
            .Select(x => new { x.Id, x.Name, x.CountryCode })
            .ToListAsync(cancellationToken);

        var competitionIds = competitionRows
            .Where(x => string.IsNullOrWhiteSpace(competition) ||
                string.Equals(x.Name, competition.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(country) || IsCountryMatch(x.CountryCode, country!))
            .Select(x => x.Id)
            .ToList();

        if (competitionIds.Count == 0)
        {
            return Ok(new EloResultsResponse(poolKey, selectedRuleset, [], page, pageSize, 0, 1));
        }

        var query = dbContext.Games
            .AsNoTracking()
            .Where(x => competitionIds.Contains(x.CompetitionId) &&
                x.HomeScore.HasValue && x.AwayScore.HasValue &&
                x.GameDateTimeUtc <= DateTime.UtcNow);

        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.GameDateTimeUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.GameDateTimeUtc <= toUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(team))
        {
            var teamFilterIds = await TeamSearchResolver.ResolveTeamIdsAsync(dbContext, team, cancellationToken);
            query = teamFilterIds.Count == 0
                ? query.Where(_ => false)
                : query.Where(x => teamFilterIds.Contains(x.HomeTeamId) || teamFilterIds.Contains(x.AwayTeamId));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = 1;

        var resultRows = await query
            .OrderByDescending(x => x.GameDateTimeUtc)
            .ThenByDescending(x => x.Id)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.GameDateTimeUtc,
                Competition = x.Competition.Name,
                CountryCode = x.Competition.CountryCode,
                Season = x.Season.Label,
                x.CompetitionPhase,
                x.HomeTeamId,
                x.AwayTeamId,
                HomeTeam = x.HomeTeam.CanonicalName,
                AwayTeam = x.AwayTeam.CanonicalName,
                x.HomeScore,
                x.AwayScore
            })
            .ToListAsync(cancellationToken);

        var resultGameIds = resultRows.Select(x => x.Id).ToList();
        var ratingRows = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.EloPoolKey == poolKey &&
                x.RulesetVersion == selectedRuleset &&
                resultGameIds.Contains(x.GameId))
            .Select(x => new
            {
                x.GameId,
                x.TeamId,
                x.PreElo,
                x.PostElo,
                x.EloDelta,
                x.RatingPositionAfter
            })
            .ToListAsync(cancellationToken);

        var ratingsByGameAndTeam = ratingRows.ToDictionary(
            x => (x.GameId, x.TeamId),
            x => new EloResultTeamRating(x.PreElo, x.PostElo, x.EloDelta, x.RatingPositionAfter));

        var rows = resultRows
            .Select(x => new EloResultRow(
                x.Id,
                x.GameDateTimeUtc,
                x.Competition,
                DisplayCountryFromCode(x.CountryCode),
                x.Season,
                x.CompetitionPhase,
                x.HomeTeam,
                x.AwayTeam,
                x.HomeScore,
                x.AwayScore,
                ratingsByGameAndTeam.GetValueOrDefault((x.Id, x.HomeTeamId)),
                ratingsByGameAndTeam.GetValueOrDefault((x.Id, x.AwayTeamId))))
            .ToList();

        return Ok(new EloResultsResponse(poolKey, selectedRuleset, rows, page, pageSize, totalCount, totalPages));
    }

    [HttpGet("games/{gameId:guid}/explanation")]
    [RequireInternalUser]
    public async Task<ActionResult<EloGameExplanationResponse>> GetGameExplanation(
        Guid gameId,
        [FromQuery] string? rulesetVersion,
        CancellationToken cancellationToken = default)
    {
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

        var poolKey = game.Competition.EloPoolKey;
        if (string.IsNullOrWhiteSpace(poolKey))
        {
            return Conflict("The game's competition is not assigned to an ELO pool.");
        }
        var selectedRuleset = await ResolveReadableRulesetAsync(rulesetVersion, poolKey, cancellationToken);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }

        var ruleset = EloCalculator.GetRulesetParameters(selectedRuleset);
        var gameRuleset = HomeAdvantagePolicy.Apply(
            ruleset,
            game.IsNeutralSite,
            game.Competition.HomeAdvantagePolicy,
            game.Competition.Name,
            game.Competition.Type,
            game.CompetitionPhase,
            game.CompetitionRound);
        var histories = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.GameId == gameId && x.EloPoolKey == poolKey && x.RulesetVersion == selectedRuleset)
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
            poolKey,
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
                gameRuleset.BaseRating,
                gameRuleset.KFactor,
                gameRuleset.HomeAdvantageElo,
                gameRuleset.PointsPerEloMargin,
                gameRuleset.CompetitionWeight,
                gameRuleset.UsesMarginAdjustment)));
    }

    [HttpGet("teams/{teamId:guid}")]
    [RequireInternalUser]
    public async Task<ActionResult<EloTeamDetailResponse>> GetTeam(
        Guid teamId,
        [FromQuery] string? rulesetVersion,
        [FromQuery] string? pool,
        [FromQuery] int gamesPage = 1,
        [FromQuery] int gamesPageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var poolKey = ResolvePoolOrDefault(pool);
        if (poolKey is null)
        {
            return BadRequest($"Unsupported ELO pool '{pool}'.");
        }
        var selectedRuleset = await ResolveReadableRulesetAsync(rulesetVersion, poolKey, cancellationToken);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }
        var currentEuropeanTeamIds = poolKey == EloPoolKeys.EuropeClubs
            ? await GetCurrentEuropeanTeamIdsAsync(null, cancellationToken)
            : null;
        var currentNationalTeamIds = poolKey == EloPoolKeys.NationalTeams
            ? await GetCurrentNationalTeamIdsAsync(null, cancellationToken)
            : null;

        var rating = await dbContext.TeamRatings
            .AsNoTracking()
            .Include(x => x.Team)
            .Include(x => x.LastGame)
            .SingleOrDefaultAsync(x => x.TeamId == teamId && x.EloPoolKey == poolKey && x.RulesetVersion == selectedRuleset, cancellationToken);

        if (rating is null)
        {
            return NotFound();
        }

        var globalRankQuery = dbContext.TeamRatings
            .AsNoTracking()
            .Where(x =>
                x.EloPoolKey == poolKey &&
                x.RulesetVersion == selectedRuleset &&
                x.Elo > rating.Elo);
        if (UsesTeamScope(poolKey))
        {
            globalRankQuery = poolKey == EloPoolKeys.Nba
                ? globalRankQuery.Where(x => x.Team.IsActive)
                : globalRankQuery.Where(x => (poolKey == EloPoolKeys.EuropeClubs
                    ? currentEuropeanTeamIds!.Contains(x.TeamId)
                    : currentNationalTeamIds!.Contains(x.TeamId)));
        }

        var globalRank = await globalRankQuery.CountAsync(cancellationToken) + 1;

        var recentMovement = (await GetRecentMovementAsync(poolKey, selectedRuleset, [teamId], null, cancellationToken))
            .GetValueOrDefault(teamId);
        var recentMovementOpponent = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.TeamId == teamId &&
                x.EloPoolKey == poolKey &&
                x.RulesetVersion == selectedRuleset &&
                x.GameDateTimeUtc <= DateTime.UtcNow)
            .OrderByDescending(x => x.GameDateTimeUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => x.OpponentTeam.CanonicalName)
            .FirstOrDefaultAsync(cancellationToken);

        var competitionRows = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.TeamId == teamId && x.EloPoolKey == poolKey && x.RulesetVersion == selectedRuleset)
            .Include(x => x.Game)
            .ThenInclude(x => x.Competition)
            .Select(x => x.Game.Competition.Name)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        var historyRows = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.TeamId == teamId && x.EloPoolKey == poolKey && x.RulesetVersion == selectedRuleset)
            .OrderBy(x => x.GameDateTimeUtc)
            .ThenBy(x => x.Id)
            .Select(x => new TeamHistoryRow(
                x.GameId,
                x.GameDateTimeUtc,
                x.PreElo,
                x.PostElo,
                x.EloDelta,
                x.RatingPositionAfter))
            .ToListAsync(cancellationToken);

        var rankChanges = new Dictionary<Guid, int?>();
        int? previousRank = null;
        foreach (var historyRow in historyRows)
        {
            rankChanges[historyRow.GameId] = historyRow.Rank.HasValue && previousRank.HasValue
                ? previousRank.Value - historyRow.Rank.Value
                : null;
            if (historyRow.Rank.HasValue)
            {
                previousRank = historyRow.Rank.Value;
            }
        }

        var bestEloRow = historyRows
            .OrderByDescending(x => x.Elo)
            .ThenBy(x => x.GameDateTimeUtc)
            .FirstOrDefault();
        var bestRankRow = historyRows
            .Where(x => x.Rank.HasValue)
            .OrderBy(x => x.Rank)
            .ThenByDescending(x => x.GameDateTimeUtc)
            .ThenByDescending(x => x.GameId)
            .FirstOrDefault();
        var historyPoints = historyRows
            .Select(x => new EloRatingHistoryPoint(x.GameDateTimeUtc, x.Elo, x.EloDelta, x.Rank, x.GameId))
            .ToList();
        var sampledHistoryRows = EloEvolutionLimits.EvenlySample(historyPoints);
        var sampledHistoryIds = sampledHistoryRows
            .Select(x => x.GameId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToHashSet();
        var recentGames = await BuildTeamGameDtosAsync(
            teamId,
            historyRows.Where(x => sampledHistoryIds.Contains(x.GameId)).ToList(),
            rankChanges,
            cancellationToken);
        var gamesPageResult = 1;
        var gamesPageSizeResult = Math.Max(1, recentGames.Count);
        var gamesTotalCount = historyRows.Count;
        var gamesTotalPages = 1;
        var gamesWereSampled = recentGames.Count < gamesTotalCount;

        var formRows = await GetTeamFormRowsAsync(teamId, poolKey, selectedRuleset, cancellationToken);
        var historicalFormRows = await GetTeamFormRowsAsync(teamId, poolKey, selectedRuleset, cancellationToken, null);

        return Ok(new EloTeamDetailResponse(
            rating.TeamId,
            rating.Team.CanonicalName,
            DisplayCountryFromCode(rating.Team.CountryCode),
            poolKey,
            EloPoolKeys.DisplayName(poolKey),
            selectedRuleset,
            rating.Elo,
            globalRank,
            rating.GamesPlayed,
            recentMovement,
            rating.LastGame?.GameDateTimeUtc,
            bestEloRow?.Elo,
            bestEloRow?.GameDateTimeUtc,
            bestRankRow?.Rank,
            bestRankRow?.GameDateTimeUtc,
            IsCurrentTeam(poolKey, rating.TeamId, currentEuropeanTeamIds, currentNationalTeamIds, rating.Team.IsActive),
            GetFranchiseIdentityEvents(poolKey, rating.Team.CanonicalName),
            competitionRows,
            recentGames,
            gamesPageResult,
            gamesPageSizeResult,
            gamesTotalCount,
            gamesTotalPages,
            BuildTeamFormSummaries(formRows),
            sampledHistoryRows,
            recentGames.Count,
            gamesWereSampled,
            recentMovementOpponent,
            BuildHistoricalHighlights(historicalFormRows)));
    }

    [HttpGet("teams/{teamId:guid}/history-games")]
    [RequireInternalUser]
    public async Task<ActionResult<EloTeamHistoryGamesResponse>> GetTeamHistoryGames(
        Guid teamId,
        [FromQuery] string? rulesetVersion,
        [FromQuery] string? pool,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int pointsPerTeam = EloEvolutionLimits.DefaultPointsPerTeam,
        CancellationToken cancellationToken = default)
    {
        var poolKey = ResolvePoolOrDefault(pool);
        if (poolKey is null)
        {
            return BadRequest($"Unsupported ELO pool '{pool}'.");
        }

        var selectedRuleset = await ResolveReadableRulesetAsync(rulesetVersion, poolKey, cancellationToken);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }

        pointsPerTeam = EloEvolutionLimits.NormalizePointsPerTeam(pointsPerTeam);
        var query = dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.TeamId == teamId &&
                x.EloPoolKey == poolKey &&
                x.RulesetVersion == selectedRuleset &&
                x.GameDateTimeUtc <= DateTime.UtcNow);

        if (fromUtc.HasValue)
        {
            var startUtc = DateTime.SpecifyKind(fromUtc.Value.Date, DateTimeKind.Utc);
            query = query.Where(x => x.GameDateTimeUtc >= startUtc);
        }

        if (toUtc.HasValue)
        {
            var endUtc = DateTime.SpecifyKind(toUtc.Value.Date, DateTimeKind.Utc).AddDays(1).AddTicks(-1);
            query = query.Where(x => x.GameDateTimeUtc <= endUtc);
        }

        var historyRows = await query
            .OrderBy(x => x.GameDateTimeUtc)
            .ThenBy(x => x.Id)
            .Select(x => new TeamHistoryRow(
                x.GameId,
                x.GameDateTimeUtc,
                x.PreElo,
                x.PostElo,
                x.EloDelta,
                x.RatingPositionAfter))
            .ToListAsync(cancellationToken);

        var sampledPoints = EloEvolutionLimits.EvenlySample(
            historyRows.Select(x => new EloRatingHistoryPoint(x.GameDateTimeUtc, x.Elo, x.EloDelta, x.Rank, x.GameId)).ToList(),
            pointsPerTeam);
        var sampledIds = sampledPoints
            .Select(x => x.GameId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToHashSet();
        var rankChanges = BuildRankChanges(historyRows);
        var games = await BuildTeamGameDtosAsync(
            teamId,
            historyRows.Where(x => sampledIds.Contains(x.GameId)).ToList(),
            rankChanges,
            cancellationToken);

        return Ok(new EloTeamHistoryGamesResponse(
            teamId,
            historyRows.Count,
            games.Count,
            games.Count < historyRows.Count,
            games));
    }

    [HttpPost("rebuilds")]
    [RequireInternalAdmin]
    public async Task<ActionResult<IReadOnlyList<EloRebuildRunDto>>> Rebuild(
        [FromBody] EloRebuildRequest? request,
        CancellationToken cancellationToken)
    {
        var requestedRuleset = request?.RulesetVersion;
        var poolKey = ResolvePoolOrDefault(request?.PoolKey);
        if (poolKey is null)
        {
            return BadRequest($"Unsupported ELO pool '{request?.PoolKey}'.");
        }

        var poolHasCompetitions = await dbContext.Competitions
            .AnyAsync(x => x.EloPoolKey == poolKey, cancellationToken);
        if (!poolHasCompetitions && poolKey != EloPoolKeys.NationalTeams)
        {
            return BadRequest($"ELO pool '{poolKey}' has no assigned competitions.");
        }

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
                x.EloPoolKey == poolKey &&
                (x.Status == EloRebuildRunStatus.Pending || x.Status == EloRebuildRunStatus.Running))
            .Select(x => x.RulesetVersion)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (activeRulesets.Count > 0)
        {
            return Conflict($"An ELO rebuild is already queued or running for: {string.Join(", ", activeRulesets)}.");
        }

        var identityGate = await EnsureIdentityHealthAllowsRebuildAsync(poolKey, cancellationToken);
        if (identityGate is not null)
        {
            return identityGate;
        }

        var queuedAtUtc = DateTime.UtcNow;
        var runs = rulesets.Select(ruleset => new EloRebuildRun
        {
            Id = Guid.NewGuid(),
            EloPoolKey = poolKey,
            RulesetVersion = ruleset,
            CompetitionName = string.Empty,
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
            return Conflict($"An ELO rebuild is already queued or running in '{poolKey}' for: {string.Join(", ", rulesets)}.");
        }

        return Accepted(runs.Select(ToDto).ToList());
    }

    [HttpPost("rebuilds/{runId:guid}/cancel")]
    [RequireInternalAdmin]
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
    [RequireInternalAdmin]
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

        if (sourceRun.Status is not (EloRebuildRunStatus.Failed or EloRebuildRunStatus.Canceled or EloRebuildRunStatus.Blocked))
        {
            return Conflict($"Only failed, canceled, or blocked rebuilds can be retried. Run '{runId}' is {sourceRun.Status}.");
        }

        var activeExists = await dbContext.EloRebuildRuns.AnyAsync(x =>
            x.EloPoolKey == sourceRun.EloPoolKey &&
            x.RulesetVersion == sourceRun.RulesetVersion &&
            (x.Status == EloRebuildRunStatus.Pending || x.Status == EloRebuildRunStatus.Running),
            cancellationToken);
        if (activeExists)
        {
            return Conflict($"An ELO rebuild is already queued or running for: {sourceRun.RulesetVersion}.");
        }

        if (string.IsNullOrWhiteSpace(sourceRun.EloPoolKey))
        {
            return Conflict("Legacy rebuild runs without an ELO pool cannot be retried. Queue a new pool rebuild instead.");
        }

        var identityGate = await EnsureIdentityHealthAllowsRebuildAsync(sourceRun.EloPoolKey, cancellationToken);
        if (identityGate is not null)
        {
            return identityGate;
        }

        var queuedAtUtc = DateTime.UtcNow;
        var retryRun = new EloRebuildRun
        {
            Id = Guid.NewGuid(),
            EloPoolKey = sourceRun.EloPoolKey,
            RulesetVersion = sourceRun.RulesetVersion,
            CompetitionName = string.Empty,
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

    private static DateTime GetPublicHistoryStartUtc()
        => DateTime.UtcNow.AddDays(-PublicHistoryWindowDays);

    private async Task<ConflictObjectResult?> EnsureIdentityHealthAllowsRebuildAsync(
        string poolKey,
        CancellationToken cancellationToken)
    {
        var identityRun = await identityHealthCheckService.RunAsync(
            new IdentityHealthCheckRequest { EloPoolKey = poolKey },
            cancellationToken);
        if (identityRun.UnresolvedBlockersCount > 0)
        {
            return Conflict(new
            {
                message = "ELO rebuild is blocked by unresolved identity health blockers.",
                identityRunId = identityRun.Id,
                identityRun.ScopeKey,
                identityRun.UnresolvedBlockersCount
            });
        }

        return null;
    }

    private static string? ResolveRulesetOrDefault(string? rulesetVersion)
    {
        if (string.IsNullOrWhiteSpace(rulesetVersion))
        {
            return EloRulesetVersions.Default;
        }

        var normalized = rulesetVersion.Trim().ToLowerInvariant();
        return EloRulesetVersions.Known.Contains(normalized) ? normalized : null;
    }

    private static bool IsDefaultRankingsRequest(
        string? rulesetVersion,
        string? pool,
        string? country,
        string? competition,
        string? season,
        string? tournamentCycle,
        DateTime? fromUtc,
        DateTime? toUtc,
        DateTime? asOfDate,
        int minimumGames,
        string? team,
        string teamScope)
        => string.IsNullOrWhiteSpace(rulesetVersion) &&
            IsDefaultResponseCachePool(ResolvePoolOrDefault(pool)) &&
            string.IsNullOrWhiteSpace(country) &&
            string.IsNullOrWhiteSpace(competition) &&
            string.IsNullOrWhiteSpace(season) &&
            string.IsNullOrWhiteSpace(tournamentCycle) &&
            !fromUtc.HasValue &&
            !toUtc.HasValue &&
            !asOfDate.HasValue &&
            minimumGames == 0 &&
            string.IsNullOrWhiteSpace(team) &&
            teamScope == EloTeamScopes.Current;

    private static bool IsDefaultResponseCachePool(string? poolKey)
        => poolKey is EloPoolKeys.Default or EloPoolKeys.EuropeClubs;

    private static bool UsesTeamScope(string poolKey)
        => poolKey is EloPoolKeys.Nba or EloPoolKeys.EuropeClubs or EloPoolKeys.NationalTeams;

    private async Task<HashSet<Guid>> GetCurrentEuropeanTeamIdsAsync(
        string? competitionName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(competitionName) &&
            cache.TryGetValue<HashSet<Guid>>(EloResponseCache.CurrentEuropeanTeamIdsCacheKey, out var cachedTeamIds) &&
            cachedTeamIds is not null)
        {
            return cachedTeamIds;
        }

        var latestGameUtc = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x =>
                x.EloPoolKey == EloPoolKeys.EuropeClubs &&
                (string.IsNullOrWhiteSpace(competitionName) || x.Game.Competition.Name == competitionName))
            .Select(x => (DateTime?)x.GameDateTimeUtc)
            .MaxAsync(cancellationToken);

        if (latestGameUtc is null)
        {
            var fallbackTeamIds = await GetCurrentEuropeanTeamIdsFromGamesAsync(competitionName, cancellationToken);
            CacheCurrentEuropeanTeamIds(competitionName, fallbackTeamIds);
            return fallbackTeamIds;
        }

        var seasonStartYear = latestGameUtc.Value.Month >= 7
            ? latestGameUtc.Value.Year
            : latestGameUtc.Value.Year - 1;
        var latestSeasonStartUtc = new DateTime(seasonStartYear, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var latestSeasonTeamIds = await dbContext.RatingHistories
            .AsNoTracking()
            .Where(x =>
                x.EloPoolKey == EloPoolKeys.EuropeClubs &&
                (string.IsNullOrWhiteSpace(competitionName) || x.Game.Competition.Name == competitionName) &&
                x.GameDateTimeUtc >= latestSeasonStartUtc &&
                x.GameDateTimeUtc <= latestGameUtc.Value)
            .Select(x => x.TeamId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var teamIds = latestSeasonTeamIds.ToHashSet();
        CacheCurrentEuropeanTeamIds(competitionName, teamIds);
        return teamIds;
    }

    private void CacheCurrentEuropeanTeamIds(string? competitionName, HashSet<Guid> teamIds)
    {
        if (!string.IsNullOrWhiteSpace(competitionName))
        {
            return;
        }

        cache.Set(
            EloResponseCache.CurrentEuropeanTeamIdsCacheKey,
            teamIds,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                SlidingExpiration = TimeSpan.FromMinutes(5)
            });
    }

    private async Task<HashSet<Guid>> GetCurrentEuropeanTeamIdsFromGamesAsync(
        string? competitionName,
        CancellationToken cancellationToken)
    {
        var latestGameUtc = await dbContext.Games
            .AsNoTracking()
            .Where(x =>
                x.Competition.EloPoolKey == EloPoolKeys.EuropeClubs &&
                (string.IsNullOrWhiteSpace(competitionName) || x.Competition.Name == competitionName))
            .Select(x => (DateTime?)x.GameDateTimeUtc)
            .MaxAsync(cancellationToken);

        if (latestGameUtc is null)
        {
            return [];
        }

        var seasonStartYear = latestGameUtc.Value.Month >= 7
            ? latestGameUtc.Value.Year
            : latestGameUtc.Value.Year - 1;
        var latestSeasonStartUtc = new DateTime(seasonStartYear, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var latestSeasonGames = dbContext.Games
            .AsNoTracking()
            .Where(x =>
                x.Competition.EloPoolKey == EloPoolKeys.EuropeClubs &&
                (string.IsNullOrWhiteSpace(competitionName) || x.Competition.Name == competitionName) &&
                x.GameDateTimeUtc >= latestSeasonStartUtc &&
                x.GameDateTimeUtc <= latestGameUtc.Value);
        var homeTeamIds = await latestSeasonGames
            .Select(x => x.HomeTeamId)
            .ToListAsync(cancellationToken);
        var awayTeamIds = await latestSeasonGames
            .Select(x => x.AwayTeamId)
            .ToListAsync(cancellationToken);

        return homeTeamIds
            .Concat(awayTeamIds)
            .ToHashSet();
    }

    private async Task<HashSet<Guid>> GetCurrentNationalTeamIdsAsync(
        string? competitionName,
        CancellationToken cancellationToken)
    {
        _ = competitionName;
        var ratedTeams = await dbContext.TeamRatings
            .AsNoTracking()
            .Where(x => x.EloPoolKey == EloPoolKeys.NationalTeams)
            .Select(x => new { x.TeamId, x.Team.CanonicalName, x.Team.CountryCode })
            .ToListAsync(cancellationToken);

        return ratedTeams
            .Where(x => !InternationalTeamCatalog.IsHistoricalIdentity(x.CanonicalName, x.CountryCode))
            .Select(x => x.TeamId)
            .ToHashSet();
    }

    private static bool IsCurrentTeam(
        string poolKey,
        Guid teamId,
        IReadOnlySet<Guid>? currentEuropeanTeamIds,
        IReadOnlySet<Guid>? currentNationalTeamIds,
        bool teamIsActive)
        => poolKey == EloPoolKeys.EuropeClubs
            ? currentEuropeanTeamIds?.Contains(teamId) == true
            : poolKey == EloPoolKeys.NationalTeams
                ? currentNationalTeamIds?.Contains(teamId) == true
            : teamIsActive;

    private async Task<bool> IsDefaultEvolutionTeamSetAsync(
        string poolKey,
        string rulesetVersion,
        IReadOnlyList<Guid> selectedTeamIds,
        CancellationToken cancellationToken)
    {
        var defaultTeamRatingsQuery = dbContext.TeamRatings
            .AsNoTracking()
            .Where(x =>
                x.EloPoolKey == poolKey &&
                x.RulesetVersion == rulesetVersion);
        if (poolKey == EloPoolKeys.Nba)
        {
            defaultTeamRatingsQuery = defaultTeamRatingsQuery.Where(x => x.Team.IsActive);
        }
        else if (poolKey == EloPoolKeys.EuropeClubs)
        {
            var currentEuropeanTeamIds = await GetCurrentEuropeanTeamIdsAsync(null, cancellationToken);
            defaultTeamRatingsQuery = defaultTeamRatingsQuery.Where(x => currentEuropeanTeamIds.Contains(x.TeamId));
        }
        else if (poolKey == EloPoolKeys.NationalTeams)
        {
            var currentNationalTeamIds = await GetCurrentNationalTeamIdsAsync(null, cancellationToken);
            defaultTeamRatingsQuery = defaultTeamRatingsQuery.Where(x => currentNationalTeamIds.Contains(x.TeamId));
        }

        var defaultTeamIds = await defaultTeamRatingsQuery
            .OrderByDescending(x => x.Elo)
            .ThenBy(x => x.Team.CanonicalName)
            .Take(20)
            .Select(x => x.TeamId)
            .ToListAsync(cancellationToken);

        return defaultTeamIds.SequenceEqual(selectedTeamIds);
    }

    private async Task<List<Guid>> GetEvolutionTeamIdsAsync(
        string poolKey,
        string rulesetVersion,
        string? country,
        string? competition,
        CancellationToken cancellationToken)
    {
        var ratings = dbContext.TeamRatings
            .AsNoTracking()
            .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == rulesetVersion);

        var ratingRows = await ratings
            .Select(x => new { x.TeamId, x.Team.CountryCode })
            .ToListAsync(cancellationToken);
        var ratingTeamIds = string.IsNullOrWhiteSpace(country)
            ? ratingRows.Select(x => x.TeamId).ToList()
            : ratingRows
                .Where(x => IsCountryMatch(x.CountryCode, country))
                .Select(x => x.TeamId)
                .ToList();

        if (!string.IsNullOrWhiteSpace(competition))
        {
            var competitionTeamIds = await dbContext.RatingHistories
                .AsNoTracking()
                .Where(x => x.EloPoolKey == poolKey &&
                    x.RulesetVersion == rulesetVersion &&
                    x.Game.Competition.Name == competition)
                .Select(x => x.TeamId)
                .Distinct()
                .ToListAsync(cancellationToken);

            ratingTeamIds = ratingTeamIds
                .Intersect(competitionTeamIds)
                .ToList();
        }

        return ratingTeamIds;
    }

    private static string? ResolvePoolOrDefault(string? poolKey)
    {
        var resolved = string.IsNullOrWhiteSpace(poolKey)
            ? EloPoolKeys.Default
            : poolKey.Trim().ToLowerInvariant();
        return EloPoolKeys.IsSupported(resolved) ? resolved : null;
    }

    private async Task<bool> CompetitionBelongsToPoolAsync(
        string? competition,
        string poolKey,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(competition) ||
            await dbContext.Competitions.AsNoTracking().AnyAsync(
                x => x.Name == competition && x.EloPoolKey == poolKey,
                cancellationToken);
    }

    private async Task<string?> ResolveReadableRulesetAsync(
        string? rulesetVersion,
        string poolKey,
        CancellationToken cancellationToken)
    {
        var resolved = ResolveRulesetOrDefault(rulesetVersion);
        if (resolved is null)
        {
            return null;
        }

        if (await dbContext.TeamRatings.AsNoTracking().AnyAsync(
            x => x.EloPoolKey == poolKey && x.RulesetVersion == resolved,
            cancellationToken))
        {
            return resolved;
        }

        return resolved;
    }

    private static EloRebuildRunDto ToDto(EloRebuildRun run)
        => new(
            run.Id,
            run.EloPoolKey,
            run.RulesetVersion,
            run.CompetitionName,
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
                run.EloPoolKey,
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

    private async Task<IReadOnlyList<TeamFormHistoryRow>> GetTeamFormRowsAsync(
        Guid teamId,
        string poolKey,
        string rulesetVersion,
        CancellationToken cancellationToken,
        int? take = 10)
    {
        IQueryable<RatingHistory> query = dbContext.RatingHistories
            .AsNoTracking()
            .Where(x => x.TeamId == teamId && x.EloPoolKey == poolKey && x.RulesetVersion == rulesetVersion &&
                x.GameDateTimeUtc <= DateTime.UtcNow)
            .OrderByDescending(x => x.GameDateTimeUtc)
            .ThenByDescending(x => x.Id);
        if (take.HasValue)
        {
            query = query.Take(Math.Max(0, take.Value));
        }

        var rows = await query
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

    private static EloTeamHistoricalHighlights? BuildHistoricalHighlights(IReadOnlyList<TeamFormHistoryRow> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        return new EloTeamHistoricalHighlights(
            rows.Count,
            rows
                .Where(x => x.ActualScore == 1m)
                .OrderByDescending(x => x.EloDelta)
                .ThenByDescending(x => x.OpponentPreElo)
                .Take(3)
                .Select(ToTeamFormGame)
                .ToList(),
            rows
                .Where(x => x.ActualScore == 0m)
                .OrderBy(x => x.EloDelta)
                .ThenByDescending(x => x.OpponentPreElo)
                .Take(3)
                .Select(ToTeamFormGame)
                .ToList());
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

    private async Task<IReadOnlyList<EloTeamGameDto>> BuildTeamGameDtosAsync(
        Guid teamId,
        IReadOnlyCollection<TeamHistoryRow> historyRows,
        IReadOnlyDictionary<Guid, int?> rankChanges,
        CancellationToken cancellationToken)
    {
        if (historyRows.Count == 0)
        {
            return [];
        }

        var gameIds = historyRows.Select(x => x.GameId).ToList();
        var gameRows = await dbContext.Games
            .AsNoTracking()
            .Where(x => gameIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.GameDateTimeUtc,
                Competition = x.Competition.Name,
                Season = x.Season.Label,
                Opponent = x.HomeTeamId == teamId ? x.AwayTeam.CanonicalName : x.HomeTeam.CanonicalName,
                WasHome = x.HomeTeamId == teamId,
                TeamScore = x.HomeTeamId == teamId ? x.HomeScore : x.AwayScore,
                OpponentScore = x.HomeTeamId == teamId ? x.AwayScore : x.HomeScore,
                x.Status
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return historyRows
            .OrderByDescending(x => x.GameDateTimeUtc)
            .ThenByDescending(x => x.GameId)
            .Where(x => gameRows.ContainsKey(x.GameId))
            .Select(x =>
            {
                var game = gameRows[x.GameId];
                return new EloTeamGameDto(
                    x.GameId,
                    game.GameDateTimeUtc,
                    game.Competition,
                    game.Season,
                    game.Opponent,
                    game.WasHome,
                    game.TeamScore,
                    game.OpponentScore,
                    x.PreElo,
                    x.Elo,
                    x.EloDelta,
                    true,
                    game.Status,
                    x.Rank,
                    rankChanges.GetValueOrDefault(x.GameId));
            })
            .ToList();
    }

    private static IReadOnlyDictionary<Guid, int?> BuildRankChanges(IReadOnlyCollection<TeamHistoryRow> historyRows)
    {
        var rankChanges = new Dictionary<Guid, int?>();
        int? previousRank = null;
        foreach (var historyRow in historyRows.OrderBy(x => x.GameDateTimeUtc).ThenBy(x => x.GameId))
        {
            rankChanges[historyRow.GameId] = historyRow.Rank.HasValue && previousRank.HasValue
                ? previousRank.Value - historyRow.Rank.Value
                : null;
            if (historyRow.Rank.HasValue)
            {
                previousRank = historyRow.Rank.Value;
            }
        }

        return rankChanges;
    }

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

    private sealed record TeamHistoryRow(
        Guid GameId,
        DateTime GameDateTimeUtc,
        decimal PreElo,
        decimal Elo,
        decimal EloDelta,
        int? Rank);

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

        public Guid GameId { get; set; }

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

    private sealed record CurrentRankingRow(
        Guid TeamId,
        string TeamName,
        string? CountryCode,
        bool IsActive,
        decimal Elo,
        int GamesPlayed,
        DateTime? LastGameUtc);

    private sealed record RecentFormRow(
        Guid Id,
        Guid TeamId,
        DateTime GameDateTimeUtc,
        string Opponent,
        bool IsWin,
        decimal EloDelta);

    private sealed class RecentFormSqlRow
    {
        public Guid Id { get; set; }

        public Guid TeamId { get; set; }

        public DateTime GameDateTimeUtc { get; set; }

        public string Opponent { get; set; } = string.Empty;

        public bool IsWin { get; set; }

        public decimal EloDelta { get; set; }
    }

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
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                entry.SlidingExpiration = TimeSpan.FromMinutes(15);

                var countries = await dbContext.TeamRatings
                    .AsNoTracking()
                    .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == rulesetVersion)
                    .Select(x => x.Team.CountryCode)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                var historyFilterRows = await dbContext.RatingHistories
                    .AsNoTracking()
                    .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == rulesetVersion)
                    .Select(x => new
                    {
                        x.Game.Competition.Name,
                        x.Game.Competition.CountryCode,
                        SeasonLabel = x.Game.Season.Label
                    })
                    .Distinct()
                    .ToListAsync(cancellationToken);

                var competitionRows = historyFilterRows
                    .Select(x => new { x.Name, x.CountryCode })
                    .Distinct()
                    .OrderBy(x => x.Name)
                    .ToList();
                var seasons = historyFilterRows
                    .Select(x => x.SeasonLabel)
                    .Distinct()
                    .ToList();

                return new EloRankingFilterOptions(
                    countries.Select(DisplayCountryFromCode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).ToList(),
                    competitionRows
                        .Select(x => new EloRankingCompetitionOption(x.Name, DisplayCountryFromCode(x.CountryCode)))
                        .ToList(),
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
            .GroupBy(x => x.TeamId)
            .Select(group => group
                .OrderByDescending(x => x.GameDateTimeUtc)
                .ThenByDescending(x => x.Id)
                .Select(x => new { x.TeamId, x.EloDelta })
                .First())
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.TeamId, x => x.EloDelta);
    }

    private async Task<Dictionary<Guid, IReadOnlyCollection<EloRecentFormGame>>> GetRecentFormAsync(
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
            .Where(x => x.EloPoolKey == poolKey &&
                x.RulesetVersion == rulesetVersion &&
                teamIds.Contains(x.TeamId));

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.GameDateTimeUtc <= toUtc.Value);
        }

        var rows = dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
            ? await query
                .OrderByDescending(x => x.GameDateTimeUtc)
                .ThenByDescending(x => x.Id)
                .Select(x => new RecentFormRow(
                    x.Id,
                    x.TeamId,
                    x.GameDateTimeUtc,
                    x.OpponentTeam.CanonicalName,
                    x.ActualScore > 0.5m,
                    x.EloDelta))
                .ToListAsync(cancellationToken)
            : await GetRecentFormRowsFromPostgresAsync(poolKey, rulesetVersion, teamIds, toUtc, cancellationToken);

        return rows
            .GroupBy(x => x.TeamId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<EloRecentFormGame>)group
                    .OrderByDescending(x => x.GameDateTimeUtc)
                    .ThenByDescending(x => x.Id)
                    .Reverse()
                    .Select(x => new EloRecentFormGame(x.GameDateTimeUtc, x.Opponent, x.IsWin, x.EloDelta))
                    .ToList());
    }

    private async Task<IReadOnlyList<RecentFormRow>> GetRecentFormRowsFromPostgresAsync(
        string poolKey,
        string rulesetVersion,
        IReadOnlyCollection<Guid> teamIds,
        DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        var parameters = new List<object>
        {
            new NpgsqlParameter("poolKey", poolKey),
            new NpgsqlParameter("rulesetVersion", rulesetVersion),
            new NpgsqlParameter("teamIds", teamIds.ToArray())
            {
                NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid
            }
        };
        var cutoffPredicate = string.Empty;
        if (toUtc.HasValue)
        {
            parameters.Add(new NpgsqlParameter("toUtc", NpgsqlDbType.TimestampTz) { Value = toUtc.Value });
            cutoffPredicate = " AND r.\"GameDateTimeUtc\" <= @toUtc";
        }

        var sql = $"""
            SELECT ranked."Id",
                   ranked."TeamId",
                   ranked."GameDateTimeUtc",
                   ranked."Opponent",
                   ranked."IsWin",
                   ranked."EloDelta"
            FROM (
                SELECT r."Id",
                       r."TeamId",
                       r."GameDateTimeUtc",
                       opponent."CanonicalName" AS "Opponent",
                       (r."ActualScore" > 0.5) AS "IsWin",
                       r."EloDelta",
                       ROW_NUMBER() OVER (
                           PARTITION BY r."TeamId"
                           ORDER BY r."GameDateTimeUtc" DESC, r."Id" DESC) AS "RowNumber"
                FROM rating_history AS r
                INNER JOIN teams AS opponent ON r."OpponentTeamId" = opponent."Id"
                WHERE r."EloPoolKey" = @poolKey
                  AND r."RulesetVersion" = @rulesetVersion
                  AND r."TeamId" = ANY (@teamIds){cutoffPredicate}
            ) AS ranked
            WHERE ranked."RowNumber" <= 5
            """;

        var sqlRows = await dbContext.Database
            .SqlQueryRaw<RecentFormSqlRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);

        return sqlRows
            .Select(x => new RecentFormRow(x.Id, x.TeamId, x.GameDateTimeUtc, x.Opponent, x.IsWin, x.EloDelta))
            .ToList();
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
