using BasketElo.Domain.Games;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/games")]
public class GamesController(BasketEloDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<GameBrowseResponse>> GetGames(
        [FromQuery] string? country,
        [FromQuery] string? leagueName,
        [FromQuery] string? season,
        [FromQuery] string? status,
        [FromQuery] string? team,
        [FromQuery] string? search,
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

        if (!string.IsNullOrWhiteSpace(leagueName))
        {
            query = query.Where(x => x.Competition.Name == leagueName);
        }

        if (!string.IsNullOrWhiteSpace(season))
        {
            query = query.Where(x => x.Season.Label == season);
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
                x.GameDateTimeUtc,
                x.Competition.CountryCode,
                LeagueName = x.Competition.Name,
                Season = x.Season.Label,
                HomeTeam = x.HomeTeam.CanonicalName,
                AwayTeam = x.AwayTeam.CanonicalName,
                x.HomeScore,
                x.AwayScore,
                x.Status
            })
            .ToListAsync(cancellationToken);

        var projected = projectedRows
            .Select(x => new GameListItem(
                x.Id,
                x.Source,
                x.GameDateTimeUtc,
                DisplayCountryFromCode(x.CountryCode),
                x.LeagueName,
                x.Season,
                x.HomeTeam,
                x.AwayTeam,
                x.HomeScore,
                x.AwayScore,
                x.Status))
            .ToList();

        var filteredSummaryQuery = query.Select(x => new { x.Status, x.GameDateTimeUtc });
        var summaryRows = await filteredSummaryQuery.ToListAsync(cancellationToken);

        var response = new GameBrowseResponse(
            projected,
            filterOptions,
            new GameBrowseSummary(
                totalCount,
                filteredCount,
                summaryRows.Count(x => IsFinishedStatus(x.Status)),
                summaryRows.Count(x => !IsFinishedStatus(x.Status)),
                summaryRows.Count == 0 ? null : summaryRows.Min(x => x.GameDateTimeUtc),
                summaryRows.Count == 0 ? null : summaryRows.Max(x => x.GameDateTimeUtc)),
            page,
            pageSize,
            filteredCount,
            totalPages);

        return Ok(response);
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
            .Select(x => x.Season.Label)
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
            seasons,
            statuses);
    }

    private static bool IsFinishedStatus(string status)
        => status.Contains("finished", StringComparison.OrdinalIgnoreCase) ||
           status.Contains("after overtime", StringComparison.OrdinalIgnoreCase);

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
            _ => [displayCountry]
        };
    }
}
