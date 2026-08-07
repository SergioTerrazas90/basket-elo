using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Imports the Israeli Super League archive from the official basket.co.il
/// results page. The archive exposes each season's competitions through the
/// Board dropdown, so league playoff boards are merged into the league while
/// Winner Cup and Supercup remain separate competitions.
/// </summary>
public sealed class OfficialIsraelBasketballDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "official-israel-basket";
    public const string ParserVersion = "official-israel-basket-results-v1";

    private const int FirstSeason = 1953;
    private const int LastSeason = 2007;
    private static readonly Regex DatePattern = new(@"(?<day>\d{1,2})/(?<month>\d{1,2})/(?<year>\d{4})", RegexOptions.Compiled);
    private static readonly Regex TimePattern = new(@"(?<hour>\d{1,2}):(?<minute>\d{2})", RegexOptions.Compiled);
    private static readonly Regex ScorePattern = new(@"(?<home>\d+)\s*-\s*(?<away>\d+)", RegexOptions.Compiled);
    private static readonly Regex QueryPattern = new(@"(?:^|[?&])(?<key>[A-Za-z]+)=(?<value>[^&#]+)", RegexOptions.Compiled);

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!country.Equals("Israel", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        var kind = leagueName.Trim().ToLowerInvariant() switch
        {
            "super league" or "winner league" or "bsl" => "league",
            "israel cup" or "winner cup" or "winner league cup" => "cup",
            "super cup" or "supercup" => "supercup",
            _ => null
        };

        return Task.FromResult<BasketballProviderLeague?>(kind is null
            ? null
            : new BasketballProviderLeague(Source, kind, kind switch
            {
                "league" => "Super League",
                "cup" => "Israel Cup",
                _ => "Super Cup"
            }, "IL", "literal"));
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        if (!TryParseSeason(season, out var startYear) || startYear is < FirstSeason or > LastSeason)
        {
            warnings.Add($"Official Israeli archive coverage is configured for 1953-1954 through 2007-2008; {season} is outside that range.");
            return ([], false, warnings);
        }

        if (!context.CanUseRequest())
        {
            return ([], true, ["The request budget was exhausted before loading the official Israeli archive page."]);
        }

        context.ConsumeRequest();
        var cYear = startYear + 1;
        var initialPath = $"/results.asp?cYear={cYear}&lang=en";
        using var initialResponse = await httpClient.GetAsync(initialPath, cancellationToken);
        initialResponse.EnsureSuccessStatusCode();
        var initialHtml = await initialResponse.Content.ReadAsStringAsync(cancellationToken);
        var fetchedAtUtc = DateTime.UtcNow;
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(initialHtml)))[..16];

        var initialDocument = new HtmlDocument();
        initialDocument.LoadHtml(initialHtml);
        var boards = ParseBoardOptions(initialDocument)
            .Where(board => IsRequestedBoard(board.Text, league.SourceLeagueId))
            .ToArray();
        if (boards.Length == 0)
        {
            warnings.Add($"The official Israeli archive exposed no {Describe(league.SourceLeagueId)} board for {season}.");
            return ([], false, warnings);
        }

        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.OrdinalIgnoreCase);
        foreach (var board in boards)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string html;
            string sourceUrl;
            if (board.Value == "5")
            {
                html = initialHtml;
                sourceUrl = new Uri(httpClient.BaseAddress!, initialPath).ToString();
            }
            else
            {
                if (!context.CanUseRequest())
                {
                    warnings.Add($"The request budget stopped the official Israeli archive at {board.Text} for {season}.");
                    return (games.Values.ToArray(), true, warnings);
                }

                context.ConsumeRequest();
                var boardPath = $"/results.asp?Board={Uri.EscapeDataString(board.Value)}&RoundNumber=0&TeamId=0&cYear={cYear}&lang=en";
                using var boardResponse = await httpClient.GetAsync(boardPath, cancellationToken);
                boardResponse.EnsureSuccessStatusCode();
                html = await boardResponse.Content.ReadAsStringAsync(cancellationToken);
                sourceUrl = new Uri(httpClient.BaseAddress!, boardPath).ToString();
            }

            var boardGames = ParseGames(html, season, sourceUrl, fetchedAtUtc, revision, league.SourceLeagueId, board);
            foreach (var game in boardGames)
            {
                games[game.SourceGameId] = game;
            }
        }

        if (games.Count == 0)
        {
            warnings.Add($"The official Israeli archive returned no completed {Describe(league.SourceLeagueId)} games for {season}.");
        }

        return (games.Values
            .OrderBy(game => game.GameDateTimeUtc)
            .ThenBy(game => game.SourceGameId, StringComparer.Ordinal)
            .ToArray(), false, warnings);
    }

    internal static IReadOnlyCollection<IsraeliBoardOption> ParseBoardOptions(HtmlDocument document)
        => (document.DocumentNode.SelectNodes("//select[@id='Board']/option")?.Cast<HtmlNode>() ?? Enumerable.Empty<HtmlNode>())
            .Select(option => new IsraeliBoardOption(
                option.GetAttributeValue("value", ""),
                HtmlEntity.DeEntitize(option.InnerText).Trim()))
            .Where(option => !string.IsNullOrWhiteSpace(option.Value) && option.Value != "0")
            .ToArray();

    internal static IReadOnlyCollection<BasketballProviderGame> ParseGames(
        string html,
        string season,
        string sourceUrl,
        DateTime fetchedAtUtc,
        string revision,
        string leagueKind,
        IsraeliBoardOption board)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in document.DocumentNode.SelectNodes("//table[contains(concat(' ', normalize-space(@class), ' '), ' stats_tbl ') and contains(concat(' ', normalize-space(@class), ' '), ' results ')]//tr[contains(concat(' ', normalize-space(@class), ' '), ' row ')]")?.Cast<HtmlNode>() ?? Enumerable.Empty<HtmlNode>())
        {
            var cells = row.SelectNodes("./td");
            if (cells is null || cells.Count < 9)
            {
                continue;
            }

            var scoreMatch = ScorePattern.Match(HtmlEntity.DeEntitize(cells[8].InnerText));
            if (!scoreMatch.Success)
            {
                continue;
            }

            var home = ParseTeam(cells[5]);
            var away = ParseTeam(cells[6]);
            var gameId = ParseGameId(row);
            var date = ParseDate(cells[0], cells[1]);
            if (home is null || away is null || string.IsNullOrWhiteSpace(gameId) || date is null)
            {
                continue;
            }

            games[gameId] = new BasketballProviderGame(
                Source,
                gameId,
                date.Value,
                "finished",
                $"official-israel-team:{home.Value.Id}",
                home.Value.Name,
                $"official-israel-team:{away.Value.Id}",
                away.Value.Name,
                short.Parse(scoreMatch.Groups["home"].Value, CultureInfo.InvariantCulture),
                short.Parse(scoreMatch.Groups["away"].Value, CultureInfo.InvariantCulture),
                new BasketballProviderGameProvenance(sourceUrl, season, fetchedAtUtc, ParserVersion, revision),
                CompetitionPhase: GetPhase(leagueKind, board.Text),
                CompetitionRound: board.Text,
                SourceHomeTeamCountryCode: "IL",
                SourceAwayTeamCountryCode: "IL");
        }

        return games.Values.ToArray();
    }

    private static bool IsRequestedBoard(string text, string leagueKind)
    {
        var normalized = text.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        return leagueKind switch
        {
            "league" => !normalized.Contains("cup", StringComparison.Ordinal) && !normalized.Contains("supercup", StringComparison.Ordinal),
            "cup" => normalized.Contains("cup", StringComparison.Ordinal) && !normalized.Contains("supercup", StringComparison.Ordinal),
            "supercup" => normalized.Contains("supercup", StringComparison.Ordinal) || normalized.Contains("supercup", StringComparison.Ordinal),
            _ => false
        };
    }

    private static string Describe(string leagueKind) => leagueKind switch
    {
        "league" => "Winner League",
        "cup" => "Winner Cup",
        _ => "Supercup"
    };

    private static string GetPhase(string leagueKind, string boardText)
    {
        if (leagueKind == "cup")
        {
            return "Winner Cup";
        }

        if (leagueKind == "supercup")
        {
            return "Supercup";
        }

        return boardText.Equals("Winner League", StringComparison.OrdinalIgnoreCase)
            ? "Regular Season"
            : "Playoffs";
    }

    private static (string Id, string Name)? ParseTeam(HtmlNode cell)
    {
        var link = cell.SelectSingleNode(".//a[contains(@href,'TeamId=')]");
        if (link is null)
        {
            return null;
        }

        var id = QueryPattern.Matches(link.GetAttributeValue("href", ""))
            .FirstOrDefault(match => match.Groups["key"].Value.Equals("TeamId", StringComparison.OrdinalIgnoreCase))?
            .Groups["value"].Value;
        var name = cell.SelectSingleNode(".//div[contains(concat(' ', normalize-space(@class), ' '), ' mid ')]")?.InnerText;
        name = HtmlEntity.DeEntitize(name ?? link.InnerText).Trim();
        return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) ? null : (id, name);
    }

    private static string? ParseGameId(HtmlNode row)
    {
        var href = row.SelectSingleNode(".//a[contains(@href,'GameId=')]")?.GetAttributeValue("href", "");
        return QueryPattern.Matches(href ?? "")
            .FirstOrDefault(match => match.Groups["key"].Value.Equals("GameId", StringComparison.OrdinalIgnoreCase))?
            .Groups["value"].Value;
    }

    private static DateTime? ParseDate(HtmlNode dateCell, HtmlNode timeCell)
    {
        var dateMatch = DatePattern.Match(HtmlEntity.DeEntitize(dateCell.InnerText));
        if (!dateMatch.Success ||
            !int.TryParse(dateMatch.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day) ||
            !int.TryParse(dateMatch.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(dateMatch.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
        {
            return null;
        }

        var hour = 12;
        var minute = 0;
        var timeMatch = TimePattern.Match(HtmlEntity.DeEntitize(timeCell.InnerText));
        if (timeMatch.Success)
        {
            _ = int.TryParse(timeMatch.Groups["hour"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out hour);
            _ = int.TryParse(timeMatch.Groups["minute"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minute);
        }

        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    private static bool TryParseSeason(string season, out int startYear)
    {
        startYear = 0;
        var parts = season.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 &&
               int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out startYear) &&
               int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var endYear) &&
               endYear == startYear + 1;
    }
}

internal sealed record IsraeliBoardOption(string Value, string Text);
