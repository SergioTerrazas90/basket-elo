using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Imports historical Czech NBL results from Flashscore's paginated results feed.
/// The HTML page contains the first results batch; subsequent batches are loaded by
/// the browser's "Show more matches" action through the tr_* feed.
/// </summary>
public sealed class FlashscoreCzechNblHistoricalDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "flashscore-czech-nbl";
    public const string ParserVersion = "flashscore-czech-nbl-results-v1";

    private const int FirstSeason = 2000;
    private const int LastSeason = 2007;
    private static readonly Regex InitialResultsPattern = new(
        """cjs\.initialFeeds\[(?:['"]results['"]|['"]summary-results['"])\]\s*=\s*\{\s*data:\s*`(?<data>[\s\S]*?)`,\s*allEventsCount:\s*(?<count>\d+),\s*seasonId:\s*(?<season>\d+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FeedSignPattern = new(
        """(?:feed_sign|feed-sign)\s*["']?\s*[:=]\s*["'](?<sign>[A-Za-z0-9_-]+)["']""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CountryIdPattern = new(
        """country_id\s*=\s*(?<value>\d+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TournamentIdPattern = new(
        """tournament_id\s*=\s*["'](?<value>[A-Za-z0-9]+)["']""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
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
        if (!country.Equals("Czech Republic", StringComparison.OrdinalIgnoreCase) ||
            !leagueName.Equals("NBL", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        return Task.FromResult<BasketballProviderLeague?>(
            new BasketballProviderLeague(Source, "nbl", "NBL", "CZ", "literal"));
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
            warnings.Add($"Flashscore Czech NBL coverage is configured for 2000-2001 through 2007-2008; {season} is outside that range.");
            return ([], false, warnings);
        }

        if (!context.CanUseRequest())
        {
            return ([], true, ["The request budget was exhausted before loading the Flashscore Czech NBL page."]);
        }

        context.ConsumeRequest();
        var pagePath = $"/basketball/czech-republic/nbl-{startYear}-{startYear + 1}/results/";
        using var pageResponse = await httpClient.GetAsync(pagePath, cancellationToken);
        pageResponse.EnsureSuccessStatusCode();
        var html = await pageResponse.Content.ReadAsStringAsync(cancellationToken);
        var fetchedAtUtc = DateTime.UtcNow;
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)))[..16];
        var pageUrl = new Uri(httpClient.BaseAddress!, pagePath).ToString();

        var initial = ParseInitialResults(html);
        if (initial is null)
        {
            warnings.Add($"Flashscore did not expose the paginated Czech NBL results feed for {season}.");
            return ([], false, warnings);
        }

        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.OrdinalIgnoreCase);
        AddParsedGames(initial.Value.Data, season, pageUrl, fetchedAtUtc, revision, games);

        var feedSign = FeedSignPattern.Match(html).Groups["sign"].Value;
        var countryId = CountryIdPattern.Match(html).Groups["value"].Value;
        var tournamentId = TournamentIdPattern.Match(html).Groups["value"].Value;
        var timezone = TimezonePattern.Match(html).Groups["value"].Value;
        var projectType = ProjectTypePattern.Match(html).Groups["value"].Value;
        if (string.IsNullOrWhiteSpace(feedSign) || string.IsNullOrWhiteSpace(countryId) ||
            string.IsNullOrWhiteSpace(tournamentId) || string.IsNullOrWhiteSpace(timezone))
        {
            warnings.Add($"Flashscore omitted one or more pagination identifiers for Czech NBL {season}.");
            return (games.Values.ToArray(), false, warnings);
        }

        var dataPart = 1;
        while (games.Count < initial.Value.AllEventsCount && dataPart <= 32)
        {
            if (!context.CanUseRequest())
            {
                warnings.Add($"The request budget stopped Flashscore Czech NBL pagination at batch {dataPart} for {season}.");
                return (games.Values.ToArray(), true, warnings);
            }

            context.ConsumeRequest();
            var feedPath = $"/x/feed/tr_3_{countryId}_{tournamentId}_{initial.Value.SeasonId}_{dataPart}_{timezone}_en_{(string.IsNullOrWhiteSpace(projectType) ? "1" : projectType)}";
            using var feedRequest = new HttpRequestMessage(HttpMethod.Get, feedPath);
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
            AddParsedGames(feed, season, pageUrl, fetchedAtUtc, revision, games);
            dataPart++;
            if (games.Count == before)
            {
                break;
            }
        }

        if (games.Count == 0)
        {
            warnings.Add($"Flashscore did not expose any complete finished Czech NBL games for {season}.");
        }
        else if (games.Count < initial.Value.AllEventsCount)
        {
            warnings.Add($"Flashscore exposed {games.Count} of {initial.Value.AllEventsCount} listed Czech NBL games for {season}.");
        }

        warnings.Add($"Flashscore parsed {games.Count} distinct Czech NBL game(s) for {season}.");
        return (games.Values
            .OrderBy(game => game.GameDateTimeUtc)
            .ThenBy(game => game.SourceGameId, StringComparer.Ordinal)
            .ToArray(), false, warnings);
    }

    internal static IReadOnlyCollection<BasketballProviderGame> ParseGames(
        string feed,
        string season,
        string pageUrl,
        DateTime fetchedAtUtc,
        string revision)
    {
        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.OrdinalIgnoreCase);
        AddParsedGames(feed, season, pageUrl, fetchedAtUtc, revision, games);
        return games.Values.OrderBy(game => game.GameDateTimeUtc).ThenBy(game => game.SourceGameId, StringComparer.Ordinal).ToArray();
    }

    private static void AddParsedGames(
        string feed,
        string season,
        string pageUrl,
        DateTime fetchedAtUtc,
        string revision,
        IDictionary<string, BasketballProviderGame> games)
    {
        foreach (Match recordMatch in RecordPattern.Matches(feed))
        {
            var fields = FieldPattern.Matches("AA÷" + recordMatch.Groups["record"].Value)
                .Cast<Match>()
                .GroupBy(match => match.Groups["key"].Value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Groups["value"].Value, StringComparer.Ordinal);

            if (!fields.TryGetValue("AA", out var eventId) ||
                !fields.TryGetValue("AD", out var timestampRaw) ||
                !long.TryParse(timestampRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp) ||
                !fields.TryGetValue("AE", out var homeName) ||
                !fields.TryGetValue("AF", out var awayName) ||
                !TryScore(fields, "AG", out var homeScore) ||
                !TryScore(fields, "AH", out var awayScore) ||
                !games.TryAdd(eventId, CreateGame(eventId, season, pageUrl, fetchedAtUtc, revision, timestamp, homeName, awayName, homeScore, awayScore, fields)))
            {
                continue;
            }
        }
    }

    private static BasketballProviderGame CreateGame(
        string eventId,
        string season,
        string pageUrl,
        DateTime fetchedAtUtc,
        string revision,
        long timestamp,
        string homeName,
        string awayName,
        short homeScore,
        short awayScore,
        IReadOnlyDictionary<string, string> fields)
    {
        var homeId = fields.GetValueOrDefault("PX") ?? fields.GetValueOrDefault("WU") ?? Slug(homeName);
        var awayId = fields.GetValueOrDefault("PY") ?? fields.GetValueOrDefault("WV") ?? Slug(awayName);
        var round = fields.GetValueOrDefault("ER") ?? "Published results";
        var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.Date;
        return new BasketballProviderGame(
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
            CompetitionPhase: "NBL",
            CompetitionRound: round);
    }

    private static (string Data, int AllEventsCount, int SeasonId)? ParseInitialResults(string html)
    {
        var match = InitialResultsPattern.Match(html);
        if (!match.Success ||
            !int.TryParse(match.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
            !int.TryParse(match.Groups["season"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seasonId))
        {
            return null;
        }

        return (match.Groups["data"].Value, count, seasonId);
    }

    private static bool TryScore(IReadOnlyDictionary<string, string> fields, string key, out short score)
    {
        score = default;
        return fields.TryGetValue(key, out var value) && short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out score);
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
}
