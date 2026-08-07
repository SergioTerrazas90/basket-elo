using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Historical Lithuanian LKL schedules published by Eurobasket.
///
/// Eurobasket's older LKL pages contain a complete season schedule in the
/// JavaScript-backed <c>thetext11</c> arrays.  That schedule includes every
/// team, so a season can be ingested with one request rather than relying on
/// whichever teams happen to have a current team page.
/// </summary>
public sealed class EurobasketLithuaniaBasketballDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "eurobasket-lithuania";
    public const string ParserVersion = "eurobasket-lithuania-lkl-v1";

    private const string SeasonUrlTemplate =
        "https://www.eurobasket.com/Lithuania/Lietuvos-Krepsinio-Lyga_{0}.aspx";

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!country.Equals("Lithuania", StringComparison.OrdinalIgnoreCase) ||
            !leagueName.Equals("LKL", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        return Task.FromResult<BasketballProviderLeague?>(
            new BasketballProviderLeague(Source, "lkl", "LKL", "LT", "literal"));
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!TryParseSeason(season, out _))
        {
            return ([], false, [$"Eurobasket Lithuania does not accept the season label '{season}'."]);
        }

        if (!context.CanUseRequest())
        {
            return ([], true, ["The request budget was exhausted before loading the Eurobasket LKL schedule."]);
        }

        context.ConsumeRequest();
        var sourceUrl = string.Format(CultureInfo.InvariantCulture, SeasonUrlTemplate, season);
        using var response = await httpClient.GetAsync(sourceUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var fetchedAtUtc = DateTime.UtcNow;
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var teamNames = ParseTeamNames(document, season);
        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        ParseLegacySchedule(document, html, season, sourceUrl, fetchedAtUtc, teamNames, games, warnings);
        ParseModernSchedule(document, season, sourceUrl, fetchedAtUtc, teamNames, games, warnings);

        if (games.Count == 0)
        {
            warnings.Add($"Eurobasket did not expose a parseable LKL schedule for {season}.");
        }

        return (games.Values.ToArray(), false, warnings);
    }

    internal static IReadOnlyCollection<BasketballProviderGame> ParseLegacySchedule(
        string html,
        string season,
        string sourceUrl,
        DateTime fetchedAtUtc,
        out IReadOnlyCollection<string> warnings)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.OrdinalIgnoreCase);
        var warningList = new List<string>();
        var teamNames = ParseTeamNames(document, season);
        ParseLegacySchedule(document, html, season, sourceUrl, fetchedAtUtc, teamNames, games, warningList);
        warnings = warningList;
        return games.Values.ToArray();
    }

    private static void ParseLegacySchedule(
        HtmlDocument document,
        string html,
        string season,
        string sourceUrl,
        DateTime fetchedAtUtc,
        IReadOnlyDictionary<string, string> teamNames,
        IDictionary<string, BasketballProviderGame> games,
        ICollection<string> warnings)
    {
        var optionsBySchedule = document.DocumentNode
            .SelectNodes("//select[starts-with(@id, 'select')]")?
            .Where(select => select.GetAttributeValue("id", string.Empty).EndsWith("11", StringComparison.Ordinal))
            .ToDictionary(
                select => select.GetAttributeValue("id", string.Empty)[6..],
                select => select.SelectNodes("./option")?.Select(option => Normalize(option.InnerText)).ToArray() ?? [],
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var assignmentPattern = new Regex(
            @"thetext(?<schedule>\d+)\[(?<index>\d+)\]\s*=\s*'(?<value>(?:\\.|[^'])*)'",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        foreach (Match assignment in assignmentPattern.Matches(html))
        {
            var scheduleId = assignment.Groups["schedule"].Value;
            if (!scheduleId.Equals("11", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var index = int.Parse(assignment.Groups["index"].Value, CultureInfo.InvariantCulture);
            var phaseText = optionsBySchedule.TryGetValue(scheduleId, out var options) && index < options.Length
                ? options[index]
                : string.Empty;
            var innerDocument = new HtmlDocument();
            innerDocument.LoadHtml(WebUtility.HtmlDecode(assignment.Groups["value"].Value));

            foreach (var row in innerDocument.DocumentNode.SelectNodes("//tr") ?? Enumerable.Empty<HtmlNode>())
            {
                var cells = row.SelectNodes("./td");
                var resultLink = cells?.ElementAtOrDefault(2)?.SelectSingleNode(".//a");
                if (cells is null || cells.Count < 4 || resultLink is null)
                {
                    continue;
                }

                var href = WebUtility.HtmlDecode(resultLink.GetAttributeValue("href", string.Empty));
                var gameMatch = Regex.Match(
                    href,
                    @"Game=(?<date>\d{4}_\d{4})_(?<home>\d+)_(?<away>\d+)-Lithuania",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var score = ParseScore(resultLink.InnerText);
                if (!gameMatch.Success || score is null ||
                    !DateTime.TryParseExact(
                        gameMatch.Groups["date"].Value,
                        "yyyy_MMdd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var gameDate))
                {
                    warnings.Add($"Skipped an incomplete Eurobasket LKL row for {season}.");
                    continue;
                }

                var homeId = gameMatch.Groups["home"].Value;
                var awayId = gameMatch.Groups["away"].Value;
                var homeName = teamNames.TryGetValue(homeId, out var knownHome)
                    ? knownHome
                    : Normalize(cells[1].InnerText);
                var awayName = teamNames.TryGetValue(awayId, out var knownAway)
                    ? knownAway
                    : Normalize(cells[3].InnerText);
                if (string.IsNullOrWhiteSpace(homeName) || string.IsNullOrWhiteSpace(awayName))
                {
                    warnings.Add($"Skipped an unnamed Eurobasket LKL row for {season}: {href}.");
                    continue;
                }

                var sourceGameId = $"eb-lkl-{gameMatch.Groups["date"].Value}_{homeId}_{awayId}";
                games[sourceGameId] = CreateGame(
                    sourceGameId,
                    gameDate,
                    homeId,
                    homeName,
                    awayId,
                    awayName,
                    score.Value,
                    sourceUrl,
                    season,
                    fetchedAtUtc,
                    phaseText);
            }
        }
    }

    private static void ParseModernSchedule(
        HtmlDocument document,
        string season,
        string sourceUrl,
        DateTime fetchedAtUtc,
        IReadOnlyDictionary<string, string> teamNames,
        IDictionary<string, BasketballProviderGame> games,
        ICollection<string> warnings)
    {
        foreach (var row in document.DocumentNode.SelectNodes("//table[contains(@class, 'GamesScheduleDetailsTable')]//tr[contains(@class, 'gamesschedulegames')]") ?? Enumerable.Empty<HtmlNode>())
        {
            var resultLink = row.SelectSingleNode(".//td[contains(@class, 'GamesResult')]//a");
            if (resultLink is null)
            {
                continue;
            }

            var href = WebUtility.HtmlDecode(resultLink.GetAttributeValue("href", string.Empty));
            var gameMatch = Regex.Match(
                href,
                @"(?:/boxScores/Lithuania/(?<year>\d{4})/(?<monthday>\d{4})_(?<home>\d+)_(?<away>\d+)\.aspx|Basketball-Box-Score\.aspx\?Game=(?<legacyDate>\d{4})_(?<legacyMonthday>\d{4})_(?<legacyHome>\d+)_(?<legacyAway>\d+)-Lithuania)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var score = ParseScore(resultLink.InnerText);
            var year = gameMatch.Groups["year"].Success ? gameMatch.Groups["year"].Value : gameMatch.Groups["legacyDate"].Value;
            var monthDay = gameMatch.Groups["monthday"].Success ? gameMatch.Groups["monthday"].Value : gameMatch.Groups["legacyMonthday"].Value;
            if (!gameMatch.Success || score is null ||
                !DateTime.TryParseExact(
                    $"{year}_{monthDay}",
                    "yyyy_MMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var gameDate))
            {
                warnings.Add($"Skipped an incomplete modern Eurobasket LKL row for {season}.");
                continue;
            }

            var homeId = gameMatch.Groups["home"].Success ? gameMatch.Groups["home"].Value : gameMatch.Groups["legacyHome"].Value;
            var awayId = gameMatch.Groups["away"].Success ? gameMatch.Groups["away"].Value : gameMatch.Groups["legacyAway"].Value;
            var teams = row.SelectNodes(".//td[contains(@class, 'GamesTeam')]") is { } teamNodes
                ? teamNodes
                : Enumerable.Empty<HtmlNode>();
            var homeName = teamNames.TryGetValue(homeId, out var knownHome)
                ? knownHome
                : Normalize(teams.ElementAtOrDefault(0)?.InnerText ?? $"Team {homeId}");
            var awayName = teamNames.TryGetValue(awayId, out var knownAway)
                ? knownAway
                : Normalize(teams.ElementAtOrDefault(1)?.InnerText ?? $"Team {awayId}");
            var sourceGameId = $"eb-lkl-{year}_{monthDay}_{homeId}_{awayId}";
            games[sourceGameId] = CreateGame(
                sourceGameId,
                gameDate,
                homeId,
                homeName,
                awayId,
                awayName,
                score.Value,
                sourceUrl,
                season,
                fetchedAtUtc,
                InferModernPhase(row.GetAttributeValue("class", string.Empty)));
        }
    }

    private static BasketballProviderGame CreateGame(
        string sourceGameId,
        DateTime gameDate,
        string homeId,
        string homeName,
        string awayId,
        string awayName,
        (short Home, short Away) score,
        string sourceUrl,
        string season,
        DateTime fetchedAtUtc,
        string phaseText)
    {
        var phase = phaseText.Contains("Regular", StringComparison.OrdinalIgnoreCase)
            ? "Regular season"
            : phaseText.Contains("Quarter", StringComparison.OrdinalIgnoreCase)
                ? "Quarter-finals"
                : phaseText.Contains("Semi", StringComparison.OrdinalIgnoreCase)
                    ? "Semi-finals"
                    : phaseText.Contains("Final", StringComparison.OrdinalIgnoreCase)
                        ? "Finals"
                        : phaseText;

        return new BasketballProviderGame(
            Source,
            sourceGameId,
            gameDate,
            "finished",
            homeId,
            homeName,
            awayId,
            awayName,
            score.Home,
            score.Away,
            new BasketballProviderGameProvenance(sourceUrl, season, fetchedAtUtc, ParserVersion, sourceGameId),
            CompetitionPhase: phase,
            CompetitionRound: phaseText);
    }

    private static IReadOnlyDictionary<string, string> ParseTeamNames(HtmlDocument document, string season)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var suffix = $"/Roster/{season}";
        foreach (var link in document.DocumentNode.SelectNodes("//a[contains(@href, '/team/')]") ?? Enumerable.Empty<HtmlNode>())
        {
            var href = WebUtility.HtmlDecode(link.GetAttributeValue("href", string.Empty));
            if (!href.Contains(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = Regex.Match(href, @"/team/[^/]+/(?<id>\d+)/Roster/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var name = Normalize(link.InnerText);
            if (match.Success && !string.IsNullOrWhiteSpace(name))
            {
                names[match.Groups["id"].Value] = name;
            }
        }

        return names;
    }

    private static (short Home, short Away)? ParseScore(string value)
    {
        var match = Regex.Match(Normalize(value), @"(?<home>\d{1,3})\s*-\s*(?<away>\d{1,3})", RegexOptions.CultureInvariant);
        return match.Success &&
            short.TryParse(match.Groups["home"].Value, out var home) &&
            short.TryParse(match.Groups["away"].Value, out var away)
            ? (home, away)
            : null;
    }

    private static string InferModernPhase(string rowClass)
    {
        if (rowClass.Contains("Quarter", StringComparison.OrdinalIgnoreCase)) return "Quarter-finals";
        if (rowClass.Contains("Semi", StringComparison.OrdinalIgnoreCase)) return "Semi-finals";
        if (rowClass.Contains("Final", StringComparison.OrdinalIgnoreCase)) return "Finals";
        return "Regular season";
    }

    private static bool TryParseSeason(string season, out int startYear)
    {
        var match = Regex.Match(season.Trim(), @"^(?<start>\d{4})-(?<end>\d{4})$", RegexOptions.CultureInvariant);
        startYear = match.Success && int.TryParse(match.Groups["start"].Value, out var value) ? value : 0;
        return match.Success && int.TryParse(match.Groups["end"].Value, out var end) && end == startYear + 1;
    }

    private static string Normalize(string value) =>
        HtmlEntity.DeEntitize(value).Replace('\u00a0', ' ').Trim();
}
