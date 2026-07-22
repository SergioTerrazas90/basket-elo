using BasketElo.Api.Auth;
using BasketElo.Domain.Games;
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
    [HttpGet]
    public async Task<ActionResult<GameBrowseResponse>> GetGames(
        [FromQuery] string? country,
        [FromQuery] string? source,
        [FromQuery] string? leagueName,
        [FromQuery] string? season,
        [FromQuery] string? status,
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
            query = query.Where(x => x.Competition.Name == leagueName);
        }

        if (!string.IsNullOrWhiteSpace(season))
        {
            query = ApplySeasonFilter(query, season);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(team))
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

        var seasons = await baseQuery
            .Select(x => new
            {
                x.Season.Label,
                x.GameDateTimeUtc
            })
            .ToListAsync(cancellationToken);

        var statuses = await baseQuery
            .Select(x => x.Status)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        return new GameFilterOptions(
            countries.Select(DisplayCountryFromCode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).ToList(),
            leagues,
            seasons.Select(x => NormalizeSeasonLabel(x.Label, x.GameDateTimeUtc)).Distinct().OrderByDescending(x => x).ToList(),
            statuses,
            await baseQuery.Select(x => x.Source).Distinct().OrderBy(x => x).ToListAsync(cancellationToken));
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
            "US" => "United States",
            "USA" => "United States",
            _ => countryCode ?? string.Empty
        };
    }

    private static IReadOnlyCollection<string> GetCountryCodes(string displayCountry)
    {
        return displayCountry switch
        {
            "Spain" => ["ES", "ESP"],
            "France" => ["FR", "FRA"],
            "Lithuania" => ["LT", "LTU"],
            "Greece" => ["GR", "GRC"],
            "Italy" => ["IT", "ITA"],
            "Turkey" => ["TR", "TUR"],
            "Latvia" => ["LV", "LVA"],
            "Belgium" => ["BE", "BEL"],
            "Germany" => ["DE", "DEU"],
            "Israel" => ["IL", "ISR"],
            "Poland" => ["PL", "POL"],
            "Czech Republic" => ["CZ", "CZE"],
            "Russia" => ["RU", "RUS"],
            "United States" => ["US", "USA"],
            _ => [displayCountry]
        };
    }

    private static IQueryable<Domain.Entities.Game> ApplySeasonFilter(IQueryable<Domain.Entities.Game> query, string season)
    {
        var normalized = NormalizeSeasonLabel(season);
        if (!TryGetSeasonWindow(normalized, out var seasonStartUtc, out var seasonEndUtc))
        {
            return query.Where(x => x.Season.Label == normalized);
        }

        SetSingleYearSeasonLabels(normalized, out var previousSingleYearSeasonLabel, out var currentSingleYearSeasonLabel);
        return query.Where(x =>
            x.Season.Label == normalized ||
            x.Season.Label == currentSingleYearSeasonLabel ||
            (x.GameDateTimeUtc >= seasonStartUtc &&
             x.GameDateTimeUtc <= seasonEndUtc &&
             x.Season.Label != previousSingleYearSeasonLabel));
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
}
