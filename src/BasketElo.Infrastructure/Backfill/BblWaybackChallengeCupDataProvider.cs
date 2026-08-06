using System.Globalization;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// The archived official BBL site contains the complete 2007-2008 Challenge
/// Cup game pages.  Basketball Database exposes the Elite Division for that
/// season, but not this separate ten/eleven-team division.
/// </summary>
public sealed class BblWaybackChallengeCupDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "bbl-wayback";
    public const string ParserVersion = "bbl-wayback-challenge-cup-v2";

    private const string BaseUrl =
        "https://web.archive.org/web/20090301id_/http://www.bbl.net:80/index.php/";

    private const string ResultsUrl =
        "https://web.archive.org/web/20090116113802id_/http://www.bbl.net:80/index.php/b19sYW5nPWVuJm9fc2Vhcz0xOSZvX2xlYWc9OSZmdXNlYWN0aW9uPWdhbWVzLnJlc3VsdHM=";

    private static readonly IReadOnlyCollection<int> GameIds =
        Enumerable.Range(1137, 110).Concat([1258, 1259, 1260, 1261]).ToArray();

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!country.Equals("Europe", StringComparison.OrdinalIgnoreCase) ||
            !leagueName.Equals("Baltic League Challenge Cup", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        return Task.FromResult<BasketballProviderLeague?>(
            new BasketballProviderLeague(Source, "bbl-challenge-cup", "Baltic League Challenge Cup", null, "literal"));
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!season.Equals("2007-2008", StringComparison.OrdinalIgnoreCase))
        {
            return ([], false, [$"The archived official BBL Challenge Cup coverage does not include {season}."]);
        }

        var games = new List<BasketballProviderGame>(GameIds.Count);
        var warnings = new List<string>();
        var fetchedAtUtc = DateTime.UtcNow;
        var hasMorePages = false;
        var parsedGameIds = new HashSet<int>();

        if (context.CanUseRequest())
        {
            context.ConsumeRequest();
            using var resultsResponse = await httpClient.GetAsync(ResultsUrl, cancellationToken);
            resultsResponse.EnsureSuccessStatusCode();
            var resultsHtml = await resultsResponse.Content.ReadAsStringAsync(cancellationToken);
            foreach (var game in ParseResults(resultsHtml, season, fetchedAtUtc, parsedGameIds))
            {
                games.Add(game);
            }
        }
        else
        {
            hasMorePages = true;
            warnings.Add("The request budget stopped the archived official BBL Challenge Cup import before the consolidated results page could be fetched.");
        }

        foreach (var gameId in GameIds)
        {
            if (parsedGameIds.Contains(gameId))
            {
                continue;
            }

            if (!context.CanUseRequest())
            {
                hasMorePages = true;
                warnings.Add($"The request budget stopped the archived official BBL Challenge Cup import after {games.Count} games.");
                break;
            }

            context.ConsumeRequest();
            var query = $"o_lang=en&o_seas=19&o_leag=9&fuseaction=games.main&g_id={gameId.ToString(CultureInfo.InvariantCulture)}";
            var encodedQuery = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(query));
            var sourceUrl = BaseUrl + encodedQuery;

            using var response = await httpClient.GetAsync(sourceUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = ParseGame(html, gameId, sourceUrl, season, fetchedAtUtc);
            if (parsed is null)
            {
                warnings.Add($"Skipped an incomplete archived official BBL Challenge Cup game page for game {gameId}.");
                continue;
            }

            games.Add(parsed);
            parsedGameIds.Add(gameId);
        }

        return (games, hasMorePages, warnings);
    }

    private static IReadOnlyCollection<BasketballProviderGame> ParseResults(
        string html,
        string season,
        DateTime fetchedAtUtc,
        ISet<int> parsedGameIds)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var teamNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var detail in document.DocumentNode.SelectNodes("//div[contains(@class, 'team_details')]") ?? Enumerable.Empty<HtmlNode>())
        {
            var anchor = detail.SelectSingleNode(".//a[@href]");
            var sourceTeamId = ParseTeamId(anchor?.GetAttributeValue("href", string.Empty));
            var name = Normalize(anchor?.InnerText ?? detail.InnerText);
            if (sourceTeamId is not null && !string.IsNullOrWhiteSpace(name))
            {
                teamNames[sourceTeamId] = name;
            }
        }

        var games = new List<BasketballProviderGame>();
        foreach (var card in document.DocumentNode.SelectNodes("//div[contains(@class, 'result_card')]") ?? Enumerable.Empty<HtmlNode>())
        {
            var dateText = Normalize(card.SelectSingleNode("./div[contains(@class, 'std_header8')]")?.InnerText ?? string.Empty);
            if (!DateTime.TryParseExact(
                    dateText,
                    "yyyy.MM.dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var gameDateTime))
            {
                continue;
            }

            var resultDivs = card.SelectNodes("./div[contains(@class, 'result')]")?.ToArray() ?? [];
            if (resultDivs.Length < 2)
            {
                continue;
            }

            var sides = resultDivs.Take(2).Select(result =>
            {
                var scoreLink = result.SelectSingleNode("./a[contains(@class, 'number')]");
                var teamLink = result.SelectSingleNode("./a[not(contains(@class, 'number'))]");
                var teamId = ParseTeamId(teamLink?.GetAttributeValue("href", string.Empty));
                var name = teamId is not null && teamNames.TryGetValue(teamId, out var mappedName)
                    ? mappedName
                    : Normalize(teamLink?.InnerText ?? string.Empty);
                return (
                    GameId: ParseGameId(scoreLink?.GetAttributeValue("href", string.Empty)),
                    TeamId: teamId,
                    Name: name,
                    Score: short.TryParse(Normalize(scoreLink?.InnerText ?? string.Empty), out var score) ? score : (short?)null);
            }).ToArray();

            var gameId = sides[0].GameId;
            if (gameId is null || gameId != sides[1].GameId ||
                sides[0].TeamId is null || sides[1].TeamId is null ||
                string.IsNullOrWhiteSpace(sides[0].Name) || string.IsNullOrWhiteSpace(sides[1].Name) ||
                sides[0].Score is null || sides[1].Score is null ||
                !GameIds.Contains(gameId.Value) || !parsedGameIds.Add(gameId.Value))
            {
                continue;
            }

            var (phase, round) = gameId switch
            {
                1258 or 1259 => ("Final Four", "Semifinal"),
                1260 => ("Final Four", "Third-place game"),
                1261 => ("Final Four", "Final"),
                _ => ("Challenge Cup", "Regular season")
            };

            games.Add(new BasketballProviderGame(
                Source,
                $"bbl-{gameId.Value.ToString(CultureInfo.InvariantCulture)}",
                gameDateTime,
                "finished",
                sides[0].TeamId!,
                sides[0].Name,
                sides[1].TeamId!,
                sides[1].Name,
                sides[0].Score,
                sides[1].Score,
                new BasketballProviderGameProvenance(
                    ResultsUrl,
                    season,
                    fetchedAtUtc,
                    ParserVersion,
                    gameId.Value.ToString(CultureInfo.InvariantCulture)),
                CompetitionPhase: phase,
                CompetitionRound: round));
        }

        return games;
    }

    private static BasketballProviderGame? ParseGame(
        string html,
        int gameId,
        string sourceUrl,
        string season,
        DateTime fetchedAtUtc)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var teams = new List<(string SourceId, string Name)>();
        foreach (var node in document.DocumentNode.SelectNodes("//b[contains(@style, 'font-size:13px')]") ?? Enumerable.Empty<HtmlNode>())
        {
            var anchor = node.SelectSingleNode("ancestor::a[1]");
            var name = Normalize(node.InnerText);
            var sourceTeamId = ParseTeamId(anchor?.GetAttributeValue("href", string.Empty));
            if (!string.IsNullOrWhiteSpace(name) && sourceTeamId is not null)
            {
                teams.Add((sourceTeamId, name));
            }

            if (teams.Count == 2)
            {
                break;
            }
        }

        var dateText = document.DocumentNode
            .SelectSingleNode("//strong[contains(text(), '-')]")?
            .InnerText;
        var dateMatch = Regex.Match(
            Normalize(dateText ?? string.Empty),
            @"(?<date>\d{4}-\d{2}-\d{2})\s*\|\s*(?<time>\d{1,2}:\d{2})",
            RegexOptions.CultureInvariant);

        var scoreText = document.DocumentNode
            .SelectSingleNode("//div[contains(@style, 'font-size:22px')]")?
            .InnerText;
        var scoreMatch = Regex.Match(
            Normalize(scoreText ?? string.Empty),
            @"(?<home>\d{1,3})\s*:\s*(?<away>\d{1,3})",
            RegexOptions.CultureInvariant);

        if (teams.Count < 2 ||
            !dateMatch.Success ||
            !scoreMatch.Success ||
            !DateTime.TryParseExact(
                $"{dateMatch.Groups["date"].Value} {dateMatch.Groups["time"].Value}",
                "yyyy-MM-dd H:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var gameDateTime) ||
            !short.TryParse(scoreMatch.Groups["home"].Value, out var homeScore) ||
            !short.TryParse(scoreMatch.Groups["away"].Value, out var awayScore))
        {
            return null;
        }

        var (phase, round) = gameId switch
        {
            1258 or 1259 => ("Final Four", "Semifinal"),
            1260 => ("Final Four", "Third-place game"),
            1261 => ("Final Four", "Final"),
            _ => ("Challenge Cup", "Regular season")
        };

        return new BasketballProviderGame(
            Source,
            $"bbl-{gameId.ToString(CultureInfo.InvariantCulture)}",
            gameDateTime,
            "finished",
            teams[0].SourceId,
            teams[0].Name,
            teams[1].SourceId,
            teams[1].Name,
            homeScore,
            awayScore,
            new BasketballProviderGameProvenance(
                sourceUrl,
                season,
                fetchedAtUtc,
                ParserVersion,
                gameId.ToString(CultureInfo.InvariantCulture)),
            CompetitionPhase: phase,
            CompetitionRound: round);
    }

    private static string? ParseTeamId(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var match = Regex.Match(href, @"[?&]t=(?<id>\d+)(?:$|&)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            var encodedPath = href[(href.LastIndexOf('/') + 1)..];
            try
            {
                var decodedPath = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encodedPath));
                match = Regex.Match(decodedPath, @"[?&]t=(?<id>\d+)(?:$|&)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (FormatException)
            {
                // Some archived pages retain a normal query-string link.
            }
        }

        return match.Success ? $"bbl-team-{match.Groups["id"].Value}" : null;
    }

    private static int? ParseGameId(string? href)
    {
        var decoded = DecodeLink(href);
        var match = Regex.Match(decoded ?? string.Empty, @"[?&]g_id=(?<id>\d+)(?:$|&)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["id"].Value, out var gameId) ? gameId : null;
    }

    private static string? DecodeLink(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var encodedPath = href[(href.LastIndexOf('/') + 1)..];
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encodedPath));
        }
        catch (FormatException)
        {
            return href;
        }
    }

    private static string Normalize(string value) =>
        HtmlEntity.DeEntitize(value).Replace('\u00a0', ' ').Trim();
}
