using BasketElo.Api.Auth;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Games;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/games")]
[RequireInternalAdmin]
public class GamesController(BasketEloDbContext dbContext) : ControllerBase
{
    [HttpGet("upcoming")]
    public async Task<ActionResult<UpcomingGamesResponse>> GetUpcomingGames(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] string? poolKey,
        [FromQuery] string? rulesetVersion,
        [FromQuery] decimal? minElo,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var from = fromUtc ?? DateTime.UtcNow;
        var to = toUtc ?? from.AddDays(7);
        var ruleset = string.IsNullOrWhiteSpace(rulesetVersion) ? EloRulesetVersions.Default : rulesetVersion.Trim().ToLowerInvariant();
        if (!EloRulesetVersions.All.Contains(ruleset, StringComparer.Ordinal))
        {
            return BadRequest(new ProblemDetails { Detail = $"Unknown ELO ruleset '{ruleset}'." });
        }

        var query = dbContext.Games
            .AsNoTracking()
            .Include(x => x.Competition)
            .Include(x => x.HomeTeam)
            .Include(x => x.AwayTeam)
            .Where(x => x.GameDateTimeUtc >= from && x.GameDateTimeUtc <= to &&
                x.Status != "finished" && x.Status != "cancelled" && x.Status != "postponed");
        if (!string.IsNullOrWhiteSpace(poolKey))
        {
            var normalizedPool = EloPoolKeys.Normalize(poolKey);
            query = query.Where(x => x.Competition.EloPoolKey == normalizedPool);
        }

        var games = await query
            .OrderBy(x => x.GameDateTimeUtc)
            .ThenBy(x => x.Competition.Name)
            .Take(5000)
            .Select(x => new
            {
                x.Id,
                x.GameDateTimeUtc,
                CountryCode = x.Competition.CountryCode,
                Competition = x.Competition.Name,
                PoolKey = x.Competition.EloPoolKey,
                HomeTeamId = x.HomeTeamId,
                AwayTeamId = x.AwayTeamId,
                HomeTeam = x.HomeTeam.CanonicalName,
                AwayTeam = x.AwayTeam.CanonicalName,
                x.Status,
                x.SourceUrl
            })
            .ToListAsync(cancellationToken);

        var teamIds = games.SelectMany(x => new[] { x.HomeTeamId, x.AwayTeamId }).Distinct().ToList();
        var ratings = await dbContext.TeamRatings
            .AsNoTracking()
            .Where(x => teamIds.Contains(x.TeamId) && x.RulesetVersion == ruleset)
            .ToDictionaryAsync(x => (x.EloPoolKey, x.TeamId), x => x.Elo, cancellationToken);

        var projected = games
            .Select(game =>
            {
                decimal? homeElo = game.PoolKey is not null && ratings.TryGetValue((game.PoolKey, game.HomeTeamId), out var home) ? home : null;
                decimal? awayElo = game.PoolKey is not null && ratings.TryGetValue((game.PoolKey, game.AwayTeamId), out var away) ? away : null;
                decimal? difference = homeElo.HasValue && awayElo.HasValue ? Math.Abs(homeElo.Value - awayElo.Value) : null;
                decimal? minimum = homeElo.HasValue && awayElo.HasValue ? Math.Min(homeElo.Value, awayElo.Value) : null;
                return new UpcomingGameListItem(game.Id, game.GameDateTimeUtc, DisplayCountryFromCode(game.CountryCode), game.Competition, game.HomeTeam, game.AwayTeam, game.Status, homeElo, awayElo, difference, minimum, homeElo.HasValue && awayElo.HasValue, game.SourceUrl);
            })
            .Where(x => !minElo.HasValue || (x.HomeElo >= minElo.Value && x.AwayElo >= minElo.Value))
            .Take(Math.Clamp(limit, 1, 500))
            .ToList();

        return Ok(new UpcomingGamesResponse(projected, from, to, ruleset, projected.Count));
    }

    [HttpGet]
    public async Task<ActionResult<GameBrowseResponse>> GetGames(
        [FromQuery] string? country,
        [FromQuery] string? source,
        [FromQuery] string? leagueName,
        [FromQuery] string? season,
        [FromQuery] string? tournamentCycle,
        [FromQuery] int? playedYear,
        [FromQuery] string? status,
        [FromQuery] Guid? teamId,
        [FromQuery] string? team,
        [FromQuery] string? search,
        [FromQuery] string? review,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);

        var baseQuery = dbContext.Games
            .AsNoTracking()
            .Include(x => x.Competition)
            .Include(x => x.Season)
            .Include(x => x.TournamentCycle)
            .Include(x => x.TournamentCycleLinks)
                .ThenInclude(x => x.TournamentCycle)
            .Include(x => x.HomeTeam)
            .Include(x => x.AwayTeam)
            .AsQueryable();

        var filterOptions = await BuildFilterOptionsAsync(baseQuery, cancellationToken);

        var query = baseQuery;

        if (!string.IsNullOrWhiteSpace(country))
        {
            var countryCodes = GetCountryCodes(country);
            query = query.Where(x => countryCodes.Contains(x.Competition.CountryCode ?? string.Empty));
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(x => x.Source == source);
        }

        if (!string.IsNullOrWhiteSpace(leagueName))
        {
            var requestedLeague = leagueName.Trim();
            var isWorldCupQualifierAlias = requestedLeague.Equals(
                "FIBA Basketball World Cup Qualifiers",
                StringComparison.OrdinalIgnoreCase);
            query = isWorldCupQualifierAlias
                ? query.Where(x =>
                    x.Competition.Name == requestedLeague ||
                    x.TournamentCycleLinks.Any(link =>
                        link.Stage == "qualifier" &&
                        link.TournamentCycle.Family == "FIBA Basketball World Cup"))
                : query.Where(x => x.Competition.Name == requestedLeague);
        }

        if (!string.IsNullOrWhiteSpace(season))
        {
            query = ApplySeasonFilter(query, season);
        }

        if (!string.IsNullOrWhiteSpace(tournamentCycle))
        {
            var cycleKey = tournamentCycle.Trim();
            query = query.Where(x =>
                (x.TournamentCycle != null && x.TournamentCycle.Key == cycleKey) ||
                x.TournamentCycleLinks.Any(link => link.TournamentCycle.Key == cycleKey));
        }

        if (playedYear is >= 1 and <= 9999)
        {
            var startUtc = new DateTime(playedYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var endUtc = startUtc.AddYears(1);
            query = query.Where(x => x.GameDateTimeUtc >= startUtc && x.GameDateTimeUtc < endUtc);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        if (teamId.HasValue)
        {
            query = query.Where(x => x.HomeTeamId == teamId.Value || x.AwayTeamId == teamId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(team))
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.HomeTeam.CanonicalName, $"%{team}%") ||
                EF.Functions.ILike(x.AwayTeam.CanonicalName, $"%{team}%"));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.HomeTeam.CanonicalName, $"%{search}%") ||
                EF.Functions.ILike(x.AwayTeam.CanonicalName, $"%{search}%") ||
                EF.Functions.ILike(x.Competition.Name, $"%{search}%") ||
                EF.Functions.ILike(x.Season.Label, $"%{search}%") ||
                EF.Functions.ILike(x.Status, $"%{search}%"));
        }

        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.GameDateTimeUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.GameDateTimeUtc <= toUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(review))
        {
            query = ApplyReviewFilter(query, review, DateTime.UtcNow);
        }

        var filteredCount = await query.CountAsync(cancellationToken);
        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var projectedRows = await query
            .OrderByDescending(x => x.GameDateTimeUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.Source,
                x.SourceGameId,
                x.SourceUrl,
                x.GameDateTimeUtc,
                x.Competition.CountryCode,
                LeagueName = x.Competition.Name,
                Season = x.Season.Label,
                TournamentCycle = x.TournamentCycleLinks
                    .OrderBy(link => link.TournamentCycle.DisplayName)
                    .Select(link => link.TournamentCycle.DisplayName)
                    .FirstOrDefault() ?? (x.TournamentCycle == null ? null : x.TournamentCycle.DisplayName),
                x.CompetitionPhase,
                x.CompetitionRound,
                HomeTeam = x.HomeTeam.CanonicalName,
                AwayTeam = x.AwayTeam.CanonicalName,
                x.HomeScore,
                x.AwayScore,
                x.Status,
                x.EloEligible,
                x.EloExclusionReason
            })
            .ToListAsync(cancellationToken);

        var projected = projectedRows
            .Select(x => new GameListItem(
                x.Id,
                x.Source,
                x.SourceGameId,
                x.SourceUrl,
                x.GameDateTimeUtc,
                DisplayCountryFromCode(x.CountryCode),
                x.LeagueName,
                x.Season,
                x.TournamentCycle,
                x.CompetitionPhase,
                x.CompetitionRound,
                x.HomeTeam,
                x.AwayTeam,
                x.HomeScore,
                x.AwayScore,
                x.Status,
                x.EloEligible,
                x.EloExclusionReason,
                GetReviewReasons(x.Status, x.GameDateTimeUtc, x.HomeScore, x.AwayScore, x.EloEligible, x.EloExclusionReason).Count > 0,
                GetReviewReasons(x.Status, x.GameDateTimeUtc, x.HomeScore, x.AwayScore, x.EloEligible, x.EloExclusionReason)))
            .ToList();

        var filteredSummaryQuery = query.Select(x => new
        {
            x.Status,
            x.GameDateTimeUtc,
            x.HomeScore,
            x.AwayScore,
            x.EloEligible,
            x.EloExclusionReason
        });
        var summaryRows = await filteredSummaryQuery.ToListAsync(cancellationToken);

        var response = new GameBrowseResponse(
            projected,
            filterOptions,
            new GameBrowseSummary(
                totalCount,
                filteredCount,
                summaryRows.Count(x => IsFinishedStatus(x.Status)),
                summaryRows.Count(x => !IsFinishedStatus(x.Status)),
                summaryRows.Count(x => GetReviewReasons(x.Status, x.GameDateTimeUtc, x.HomeScore, x.AwayScore, x.EloEligible, x.EloExclusionReason).Count > 0),
                summaryRows.Count == 0 ? null : summaryRows.Min(x => x.GameDateTimeUtc),
                summaryRows.Count == 0 ? null : summaryRows.Max(x => x.GameDateTimeUtc)),
            page,
            pageSize,
            filteredCount,
            totalPages);

        return Ok(response);
    }

    [HttpPatch("{id:guid}/result")]
    public async Task<IActionResult> UpdateResult(
        Guid id,
        [FromBody] UpdateGameResultRequest request,
        CancellationToken cancellationToken)
    {
        var game = await dbContext.Games.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (game is null)
        {
            return NotFound();
        }

        var status = string.IsNullOrWhiteSpace(request.Status)
            ? "finished"
            : request.Status.Trim().ToLowerInvariant();
        var supportedStatuses = new[] { "finished", "score_pending", "postponed", "cancelled" };
        if (!supportedStatuses.Contains(status, StringComparer.Ordinal))
        {
            return BadRequest(new ProblemDetails { Detail = "Status must be finished, score_pending, postponed, or cancelled." });
        }

        if (request.HomeScore.HasValue != request.AwayScore.HasValue)
        {
            return BadRequest(new ProblemDetails { Detail = "Both scores are required together." });
        }

        if ((request.HomeScore is < 0) || (request.AwayScore is < 0))
        {
            return BadRequest(new ProblemDetails { Detail = "Scores cannot be negative." });
        }

        if (status == "finished" && (!request.HomeScore.HasValue || !request.AwayScore.HasValue))
        {
            return BadRequest(new ProblemDetails { Detail = "A finished game requires both scores." });
        }

        game.HomeScore = request.HomeScore;
        game.AwayScore = request.AwayScore;
        game.Status = status;
        game.HasManualResultOverride = true;
        game.EloEligible = status == "finished" && request.HomeScore.HasValue && request.AwayScore.HasValue;
        game.EloExclusionReason = game.EloEligible ? null : "manual_result_not_eligible";
        game.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static async Task<GameFilterOptions> BuildFilterOptionsAsync(IQueryable<Domain.Entities.Game> baseQuery, CancellationToken cancellationToken)
    {
        var countries = await baseQuery
            .Select(x => x.Competition.CountryCode)
            .Distinct()
            .ToListAsync(cancellationToken);

        var leagues = await baseQuery
            .Select(x => x.Competition.Name)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
        var hasHistoricalWorldCupQualifierLinks = await baseQuery
            .AnyAsync(x => x.TournamentCycleLinks.Any(link =>
                link.Stage == "qualifier" &&
                link.TournamentCycle.Family == "FIBA Basketball World Cup"), cancellationToken);
        if (hasHistoricalWorldCupQualifierLinks &&
            !leagues.Contains("FIBA Basketball World Cup Qualifiers", StringComparer.OrdinalIgnoreCase))
        {
            leagues.Add("FIBA Basketball World Cup Qualifiers");
            leagues.Sort(StringComparer.Ordinal);
        }

        var seasons = await baseQuery
            .Select(x => x.Season.Label)
            .ToListAsync(cancellationToken);

        var primaryTournamentCycleRows = baseQuery
            .Where(x => x.TournamentCycle != null)
            .Select(x => new { x.TournamentCycle!.Key, x.TournamentCycle.DisplayName })
            .Distinct();
        var linkedTournamentCycleRows = baseQuery
            .SelectMany(x => x.TournamentCycleLinks.Select(link => new
            {
                link.TournamentCycle.Key,
                link.TournamentCycle.DisplayName
            }));
        var tournamentCycleRows = await primaryTournamentCycleRows
            .Concat(linkedTournamentCycleRows)
            .Distinct()
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        var tournamentCycles = tournamentCycleRows
            .Select(x => new TournamentCycleOption(x.Key, x.DisplayName))
            .ToList();

        var playedYears = await baseQuery
            .Where(x => x.GameDateTimeUtc != DateTime.MinValue && x.GameDateTimeUtc != DateTime.MaxValue)
            .Select(x => x.GameDateTimeUtc.Year)
            .Distinct()
            .OrderByDescending(x => x)
            .ToListAsync(cancellationToken);

        var statuses = await baseQuery
            .Select(x => x.Status)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        return new GameFilterOptions(
            countries.Select(DisplayCountryFromCode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).ToList(),
            leagues,
            seasons.Distinct().OrderByDescending(x => x).ToList(),
            statuses,
            await baseQuery.Select(x => x.Source).Distinct().OrderBy(x => x).ToListAsync(cancellationToken),
            tournamentCycles,
            playedYears);
    }

    private static IQueryable<Domain.Entities.Game> ApplyReviewFilter(
        IQueryable<Domain.Entities.Game> query,
        string review,
        DateTime nowUtc)
    {
        var cutoff = nowUtc.AddDays(-2);

        return review.Trim().ToLowerInvariant() switch
        {
            "needs_review" => query.Where(x =>
                !x.EloEligible ||
                ((x.Status.Contains("finished") || x.Status.Contains("after overtime") || x.Status.Contains("after over time") || x.Status.Contains("final")) && (!x.HomeScore.HasValue || !x.AwayScore.HasValue)) ||
                (!(x.Status.Contains("finished") || x.Status.Contains("after overtime") || x.Status.Contains("after over time") || x.Status.Contains("final")) && x.GameDateTimeUtc < cutoff)),
            "excluded_from_elo" => query.Where(x => !x.EloEligible),
            "missing_score" => query.Where(x => (x.Status.Contains("finished") || x.Status.Contains("after overtime") || x.Status.Contains("after over time") || x.Status.Contains("final")) && (!x.HomeScore.HasValue || !x.AwayScore.HasValue)),
            "stale_status" => query.Where(x => !(x.Status.Contains("finished") || x.Status.Contains("after overtime") || x.Status.Contains("after over time") || x.Status.Contains("final")) && x.GameDateTimeUtc < cutoff),
            _ => query
        };
    }

    private static IReadOnlyCollection<string> GetReviewReasons(
        string status,
        DateTime gameDateTimeUtc,
        short? homeScore,
        short? awayScore,
        bool eloEligible,
        string? eloExclusionReason)
    {
        var reasons = new List<string>();
        if (!eloEligible)
        {
            reasons.Add(string.IsNullOrWhiteSpace(eloExclusionReason)
                ? "Excluded from ELO"
                : $"Excluded from ELO: {eloExclusionReason}");
        }

        if (IsFinishedStatus(status) && (!homeScore.HasValue || !awayScore.HasValue))
        {
            reasons.Add("Finished game is missing a score");
        }

        if (!IsFinishedStatus(status) && gameDateTimeUtc < DateTime.UtcNow.AddDays(-2))
        {
            reasons.Add("Game date is more than two days old but status is not finished");
        }

        return reasons;
    }

    private static bool IsFinishedStatus(string status)
    {
        var normalized = status.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized.Contains("finished", StringComparison.Ordinal) ||
            normalized.Contains("afterovertime", StringComparison.Ordinal) ||
            normalized is "final" or "completed";
    }

    private static string DisplayCountryFromCode(string? countryCode)
        => CountryCodeCatalog.DisplayName(countryCode);

    private static IReadOnlyCollection<string> GetCountryCodes(string displayCountry)
    {
        return displayCountry switch
        {
            "Spain" => ["ES"],
            "France" => ["FR"],
            "Lithuania" => ["LT"],
            "Greece" => ["GR"],
            "Italy" => ["IT"],
            "Turkey" => ["TR"],
            "Latvia" => ["LV"],
            "Belgium" => ["BE"],
            "Germany" => ["DE"],
            "Israel" => ["IL"],
            "Poland" => ["PL"],
            "Czech Republic" => ["CZ"],
            "Russia" => ["RU"],
            "United States" => ["US"],
            _ => [displayCountry]
        };
    }

    private static IQueryable<Domain.Entities.Game> ApplySeasonFilter(IQueryable<Domain.Entities.Game> query, string season)
        => query.Where(x => x.Season.Label == season.Trim());

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
}
