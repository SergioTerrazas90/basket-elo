using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Imports historical domestic-league results from Flashscore's paginated
/// results feeds. The configured routes intentionally use the country domains
/// where the historical pages are indexed (including flashscore.com.gh for the
/// Russian PBL archive).
/// </summary>
public sealed class FlashscoreDomesticHistoricalDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "flashscore-domestic";
    public const string ParserVersion = "flashscore-domestic-results-v1";

    private static readonly IReadOnlyCollection<LeagueConfig> Configs =
    [
        new("Russia", "PBL", "pbl", "PBL", "RU", "https://www.flashscore.com.gh", 2005, 2008),
        new("Belgium", "EuroMillions Basketball League", "pro-basketball-league", "Ethias League", "BE", "https://www.flashscore.com", 2009, 2009),
        new("Belgium", "Pro Basketball League", "pro-basketball-league", "Ethias League", "BE", "https://www.flashscore.com", 2009, 2009),
        new("Croatia", "Premijer liga", "premijer-liga", "A1 Liga", "HR", "https://www.flashscore.info", 2008, 2013),
        new("Latvia", "LBL", "lbl", "LBL", "LV", "https://www.flashscore.com", 2011, 2020)
    ];

    private static readonly Regex InitialResultsPattern = new(
        """cjs\.initialFeeds\[(?:['\"]results['\"]|['\"]summary-results['\"]|['\"]summaryResults['\"])]\s*=\s*\{\s*data:\s*`(?<data>[\s\S]*?)`,\s*allEventsCount:\s*(?<count>\d+),\s*seasonId:\s*(?<season>\d+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FeedSignPattern = new(
        """(?:feed_sign|feed-sign)\s*[\"']?\s*[:=]\s*[\"'](?<sign>[A-Za-z0-9_-]+)[\"']""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CountryIdPattern = new(
        """country_id\s*=\s*(?<value>\d+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TournamentIdPattern = new(
        """tournament_id\s*=\s*[\"'](?<value>[A-Za-z0-9]+)[\"']""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TimezonePattern = new(
        """default_tz\s*=\s*(?<value>-?\d+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ProjectTypePattern = new(
        """project_type\"\s*:\s*\{\s*\"id\"\s*:\s*(?<value>\d+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RecordPattern = new(
        """(?:^|~)AA(?:\u00f7|\u00c3\u00b7|\u00c3\u0192\u00c2\u00b7)(?<record>[\s\S]*?)(?=(?:~AA(?:\u00f7|\u00c3\u00b7|\u00c3\u0192\u00c2\u00b7)|$))""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FieldPattern = new(
        """(?<key>[A-Z]{2,3})(?:\u00f7|\u00c3\u00b7|\u00c3\u0192\u00c2\u00b7)(?<value>.*?)(?=(?:\u00ac|\u00c2\u00ac|\u00c3\u201a\u00ac)|$)""",
        RegexOptions.Compiled);

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var config = Configs.FirstOrDefault(candidate =>
            candidate.Country.Equals(country, StringComparison.OrdinalIgnoreCase) &&
            candidate.LeagueNames.Any(name => name.Equals(leagueName, StringComparison.OrdinalIgnoreCase)));

        return Task.FromResult<BasketballProviderLeague?>(config is null
            ? null
            : new BasketballProviderLeague(Source, config.Route, config.DisplayName, config.CountryCode, "literal"));
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var config = Configs.FirstOrDefault(candidate =>
            candidate.Route.Equals(league.SourceLeagueId, StringComparison.OrdinalIgnoreCase));
        var warnings = new List<string>();
        if (config is null || !TryParseSeason(season, out var startYear) || startYear < config.FirstSeason || startYear > config.LastSeason)
        {
            warnings.Add($"Flashscore domestic coverage does not include {league.Name} {season}.");
            return ([], false, warnings);
        }

        if (!context.CanUseRequest())
        {
            return ([], true, ["The request budget was exhausted before loading the Flashscore domestic page."]);
        }

        context.ConsumeRequest();
        var pagePath = $"/basketball/{config.CountryPath}/{config.Route}-{startYear}-{startYear + 1}/results/";
        var pageUrl = $"{config.BaseUrl}{pagePath}";
        using var pageResponse = await httpClient.GetAsync(pageUrl, cancellationToken);
        pageResponse.EnsureSuccessStatusCode();
        var html = await pageResponse.Content.ReadAsStringAsync(cancellationToken);
        var fetchedAtUtc = DateTime.UtcNow;
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)))[..16];
        var initial = ParseInitialResults(html);
        if (initial is null)
        {
            warnings.Add($"Flashscore did not expose the paginated results feed for {league.Name} {season}.");
            return ([], false, warnings);
        }

        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.OrdinalIgnoreCase);
        AddParsedGames(initial.Value.Data, season, pageUrl, config.DisplayName, fetchedAtUtc, revision, games);

        var feedSign = FeedSignPattern.Match(html).Groups["sign"].Value;
        var countryId = CountryIdPattern.Match(html).Groups["value"].Value;
        var tournamentId = TournamentIdPattern.Match(html).Groups["value"].Value;
        var timezone = TimezonePattern.Match(html).Groups["value"].Value;
        var projectType = ProjectTypePattern.Match(html).Groups["value"].Value;
        if (games.Count >= initial.Value.AllEventsCount)
        {
            return (games.Values.OrderBy(game => game.GameDateTimeUtc).ThenBy(game => game.SourceGameId, StringComparer.Ordinal).ToArray(), false, warnings);
        }

        if (string.IsNullOrWhiteSpace(feedSign) || string.IsNullOrWhiteSpace(countryId) ||
            string.IsNullOrWhiteSpace(tournamentId) || string.IsNullOrWhiteSpace(timezone))
        {
            warnings.Add($"Flashscore omitted one or more pagination identifiers for {league.Name} {season}.");
            return (games.Values.ToArray(), false, warnings);
        }

        var dataPart = 1;
        while (games.Count < initial.Value.AllEventsCount && dataPart <= 64)
        {
            if (!context.CanUseRequest())
            {
                warnings.Add($"The request budget stopped Flashscore pagination at batch {dataPart} for {league.Name} {season}.");
                return (games.Values.OrderBy(game => game.GameDateTimeUtc).ToArray(), true, warnings);
            }

            context.ConsumeRequest();
            var feedPath = $"/x/feed/tr_3_{countryId}_{tournamentId}_{initial.Value.SeasonId}_{dataPart}_{timezone}_en_{(string.IsNullOrWhiteSpace(projectType) ? "1" : projectType)}";
            using var feedRequest = new HttpRequestMessage(HttpMethod.Get, $"{config.BaseUrl}{feedPath}");
            feedRequest.Headers.TryAddWithoutValidation("x-fsign", feedSign);
            feedRequest.Headers.TryAddWithoutValidation("x-geoip", "1");
            feedRequest.Headers.Referrer = new Uri(pageUrl);
            using var feedResponse = await httpClient.SendAsync(feedRequest, cancellationToken);
            feedResponse.EnsureSuccessStatusCode();
            var feed = await feedResponse.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(feed) || feed.Trim() == "0")
            {
                break;
            }

            var before = games.Count;
            AddParsedGames(feed, season, pageUrl, config.DisplayName, fetchedAtUtc, revision, games);
            dataPart++;
            if (games.Count == before)
            {
                break;
            }
        }

        if (games.Count == 0)
        {
            warnings.Add($"Flashscore did not expose any complete finished games for {league.Name} {season}.");
        }
        else if (games.Count < initial.Value.AllEventsCount)
        {
            warnings.Add($"Flashscore exposed {games.Count} of {initial.Value.AllEventsCount} listed {league.Name} games for {season}.");
        }

        return (games.Values.OrderBy(game => game.GameDateTimeUtc).ThenBy(game => game.SourceGameId, StringComparer.Ordinal).ToArray(), false, warnings);
    }

    internal static IReadOnlyCollection<BasketballProviderGame> ParseGames(
        string feed,
        string season,
        string pageUrl,
        string competitionPhase,
        DateTime fetchedAtUtc,
        string revision)
    {
        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.OrdinalIgnoreCase);
        AddParsedGames(feed, season, pageUrl, competitionPhase, fetchedAtUtc, revision, games);
        return games.Values.OrderBy(game => game.GameDateTimeUtc).ThenBy(game => game.SourceGameId, StringComparer.Ordinal).ToArray();
    }

    private static void AddParsedGames(
        string feed,
        string season,
        string pageUrl,
        string competitionPhase,
        DateTime fetchedAtUtc,
        string revision,
        IDictionary<string, BasketballProviderGame> games)
    {
        foreach (Match recordMatch in RecordPattern.Matches(feed))
        {
            var fields = FieldPattern.Matches("AAÃ·" + recordMatch.Groups["record"].Value)
                .Cast<Match>()
                .GroupBy(match => match.Groups["key"].Value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Groups["value"].Value, StringComparer.Ordinal);

            if (!fields.TryGetValue("AA", out var eventId) ||
                !fields.TryGetValue("AD", out var timestampRaw) ||
                !long.TryParse(timestampRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp) ||
                !fields.TryGetValue("AE", out var homeName) ||
                !fields.TryGetValue("AF", out var awayName) ||
                !TryScore(fields, "AG", out var homeScore) ||
                !TryScore(fields, "AH", out var awayScore))
            {
                continue;
            }

            var homeId = fields.GetValueOrDefault("PX") ?? fields.GetValueOrDefault("WU") ?? Slug(homeName);
            var awayId = fields.GetValueOrDefault("PY") ?? fields.GetValueOrDefault("WV") ?? Slug(awayName);
            var round = fields.GetValueOrDefault("ER") ?? "Published results";
            var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.Date;
            games.TryAdd(eventId, new BasketballProviderGame(
                Source,
                eventId,
                DateTime.SpecifyKind(date.AddHours(12), DateTimeKind.Utc),
                "finished",
                $"flashscore-team:{homeId}",
                homeName,
                $"flashscore-team:{awayId}",
                awayName,
                homeScore,
                awayScore,
                new BasketballProviderGameProvenance(pageUrl, season, fetchedAtUtc, ParserVersion, revision),
                CompetitionPhase: competitionPhase,
                CompetitionRound: round));
        }
    }

    private static (string Data, int AllEventsCount, int SeasonId)? ParseInitialResults(string html)
    {
        var match = InitialResultsPattern.Match(html);
        return match.Success &&
               int.TryParse(match.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) &&
               int.TryParse(match.Groups["season"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seasonId)
            ? (match.Groups["data"].Value, count, seasonId)
            : null;
    }

    private static bool TryScore(IReadOnlyDictionary<string, string> fields, string key, out short score)
    {
        score = default;
        return fields.TryGetValue(key, out var value) &&
               short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out score);
    }

    private static string Slug(string value)
        => Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');

    private static bool TryParseSeason(string season, out int startYear)
    {
        startYear = 0;
        var parts = season.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 &&
               int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out startYear) &&
               int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var endYear) &&
               endYear == startYear + 1;
    }

    private sealed record LeagueConfig(
        string Country,
        string LeagueName,
        string Route,
        string DisplayName,
        string CountryCode,
        string BaseUrl,
        int FirstSeason,
        int LastSeason)
    {
        public string CountryPath => Country switch
        {
            "Czech Republic" => "czech-republic",
            _ => Country.ToLowerInvariant().Replace(' ', '-')
        };

        public IReadOnlyCollection<string> LeagueNames => [LeagueName];
    }
}
