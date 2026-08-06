using System.Globalization;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Historical Baltic Basketball League schedules from Basketball Database.
/// The archive exposes one schedule table per season page, including regular
/// season, group, challenge-cup, and playoff records where available.
/// </summary>
public sealed class BasketballDatabaseBalticLeagueDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "basketball-database";
    public const string ParserVersion = "basketball-database-baltic-league-v1";
    private const string BaseUrl = "https://basketball-database.com.court-side.com/csgc/leagues/0/";

    private static readonly IReadOnlyDictionary<string, int> SeasonPages =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["2004-2005"] = 1956,
            ["2005-2006"] = 1957,
            ["2006-2007"] = 1958,
            ["2007-2008"] = 2369
        };

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!country.Equals("Europe", StringComparison.OrdinalIgnoreCase) ||
            !leagueName.Equals("Baltic League", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        return Task.FromResult<BasketballProviderLeague?>(
            new BasketballProviderLeague(Source, "baltic", "Baltic League", null, "literal"));
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!SeasonPages.TryGetValue(season, out var pageId))
        {
            return ([], false, [$"Basketball Database historical Baltic League coverage does not include {season}."]);
        }

        if (!context.CanUseRequest())
        {
            return ([], false, ["The request budget was exhausted before loading the Basketball Database Baltic League schedule."]);
        }

        context.ConsumeRequest();
        var pageUrl = BaseUrl + pageId;
        using var response = await httpClient.GetAsync(pageUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var fetchedAtUtc = DateTime.UtcNow;
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var scheduleTable = document.DocumentNode
            .SelectNodes("//table")?
            .FirstOrDefault(table =>
            {
                var headers = table.SelectNodes(".//thead//th")?
                    .Select(header => Normalize(header.InnerText))
                    .ToArray();
                return headers is not null &&
                    headers.SequenceEqual(["date", "type", "home", "visitor", "result"]);
            });

        if (scheduleTable is null)
        {
            return ([], false, [$"Basketball Database did not expose a schedule table for {season}."]);
        }

        var games = new List<BasketballProviderGame>();
        var warnings = new List<string>();
        foreach (var row in scheduleTable.SelectNodes(".//tbody/tr") ?? Enumerable.Empty<HtmlNode>())
        {
            var cells = row.SelectNodes("./td");
            if (cells is null || cells.Count < 5)
            {
                continue;
            }

            var dateText = Normalize(cells[0].InnerText);
            var phase = Normalize(cells[1].InnerText);
            var home = ParseTeam(cells[2]);
            var away = ParseTeam(cells[3]);
            var result = ParseResult(cells[4]);
            var gameLink = cells[4].SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty);

            if (!DateTime.TryParseExact(
                    dateText,
                    "dd MMM yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var gameDate) ||
                home is null ||
                away is null ||
                result is null)
            {
                warnings.Add($"Skipped an incomplete Basketball Database Baltic League row for {season}: {dateText}.");
                continue;
            }

            games.Add(new BasketballProviderGame(
                Source,
                ParseSourceId(gameLink) ?? $"bbl-{season}-{games.Count + 1}",
                gameDate,
                "finished",
                home.Value.SourceId,
                home.Value.Name,
                away.Value.SourceId,
                away.Value.Name,
                result.Value.Home,
                result.Value.Away,
                new BasketballProviderGameProvenance(
                    gameLink ?? pageUrl,
                    season,
                    fetchedAtUtc,
                    ParserVersion,
                    pageId.ToString(CultureInfo.InvariantCulture)),
                CompetitionPhase: phase,
                CompetitionRound: phase));
        }

        if (games.Count == 0)
        {
            warnings.Add($"Basketball Database returned no parseable Baltic League games for {season}.");
        }

        return (games, false, warnings);
    }

    private static (string SourceId, string Name)? ParseTeam(HtmlNode cell)
    {
        var link = cell.SelectSingleNode(".//a");
        var name = Normalize(link?.InnerText ?? cell.InnerText);
        var sourceId = ParseSourceId(link?.GetAttributeValue("href", string.Empty));
        return string.IsNullOrWhiteSpace(name) || sourceId is null ? null : (sourceId, name);
    }

    private static (short Home, short Away)? ParseResult(HtmlNode cell)
    {
        var text = Normalize(cell.InnerText);
        var match = Regex.Match(text, @"(?<home>\d{1,3})\s*-\s*(?<away>\d{1,3})", RegexOptions.CultureInvariant);
        return match.Success &&
            short.TryParse(match.Groups["home"].Value, out var home) &&
            short.TryParse(match.Groups["away"].Value, out var away)
            ? (home, away)
            : null;
    }

    private static string? ParseSourceId(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var match = Regex.Match(href, @"/(?:teams?|games?)/(?:[^/]+/)?(?<id>\d+)(?:$|#|\?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? $"bdb-{match.Groups["id"].Value}" : href.TrimEnd('/').Split('/').LastOrDefault();
    }

    private static string Normalize(string value) =>
        HtmlEntity.DeEntitize(value).Replace('\u00a0', ' ').Trim();
}

