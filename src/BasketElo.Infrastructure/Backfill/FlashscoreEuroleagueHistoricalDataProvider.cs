using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Imports the historical EuroLeague bridge from Flashscore's one-page season
/// results stream. Flashscore publishes stable event IDs and finished scores
/// for the regular season and knockout phases.
/// </summary>
public sealed class FlashscoreEuroleagueHistoricalDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "flashscore-euroleague";
    public const string ParserVersion = "flashscore-euroleague-results-v1";

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!country.Equals("Europe", StringComparison.OrdinalIgnoreCase) ||
            !leagueName.Equals("Euroleague", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        return Task.FromResult<BasketballProviderLeague?>(
            new BasketballProviderLeague(Source, "euroleague-historical", "Euroleague", "EUR", "literal"));
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var (startYear, endYear) = ParseSeason(season);
        if (startYear is < 2000 or > 2007)
        {
            warnings.Add($"Flashscore historical Euroleague coverage is configured for 2000-2001 through 2007-2008; {season} is outside that range.");
            return ([], false, warnings);
        }

        if (!context.CanUseRequest())
        {
            warnings.Add($"Request budget reached before the Flashscore Euroleague {season} results page could be fetched.");
            return ([], false, warnings);
        }

        context.ConsumeRequest();
        var pagePath = $"/basketball/europe/euroleague-{startYear}-{endYear}/results/";
        using var response = await httpClient.GetAsync(pagePath, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var fetchedAtUtc = DateTime.UtcNow;
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)))[..16];
        var pageUrl = new Uri(httpClient.BaseAddress!, pagePath).ToString();
        var games = ParseGames(html, season, pageUrl, fetchedAtUtc, revision, warnings);
        warnings.Add($"Flashscore parsed {games.Count} distinct Euroleague game(s) for {season}.");
        return (games, false, warnings);
    }

    internal static IReadOnlyCollection<BasketballProviderGame> ParseGames(
        string html,
        string season,
        string pageUrl,
        DateTime fetchedAtUtc,
        string revision,
        ICollection<string> warnings)
    {
        var games = new List<BasketballProviderGame>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segment in Regex.Split(html, @"~AA÷", RegexOptions.IgnoreCase).Skip(1))
        {
            // The split removes the event's AA field, so restore it before
            // parsing the key/value stream.
            var fields = Regex.Matches($"AA÷{segment}", @"(?<key>[A-Z]{2,3})÷(?<value>.*?)(?=¬|$)")
                .Cast<Match>()
                .GroupBy(match => match.Groups["key"].Value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Groups["value"].Value, StringComparer.Ordinal);
            if (!fields.TryGetValue("AA", out var eventId) || !seen.Add(eventId) ||
                !fields.TryGetValue("AD", out var timestampRaw) ||
                !long.TryParse(timestampRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp) ||
                !fields.TryGetValue("CX", out var homeName) ||
                !fields.TryGetValue("AF", out var awayName) ||
                !TryScore(fields, "AG", out var homeScore) ||
                !TryScore(fields, "AH", out var awayScore))
            {
                continue;
            }

            var homeId = fields.GetValueOrDefault("WU") ?? Slug(homeName);
            var awayId = fields.GetValueOrDefault("WV") ?? Slug(awayName);
            var round = fields.GetValueOrDefault("ER") ?? "Published results";
            var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.Date;
            games.Add(new BasketballProviderGame(
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
                CompetitionPhase: "Euroleague",
                CompetitionRound: round));
        }

        if (games.Count == 0)
        {
            warnings.Add("Flashscore did not expose any complete finished event records.");
        }

        return games
            .OrderBy(game => game.GameDateTimeUtc)
            .ThenBy(game => game.SourceGameId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryScore(IReadOnlyDictionary<string, string> fields, string key, out short score)
    {
        score = default;
        return fields.TryGetValue(key, out var value) &&
            short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out score);
    }

    private static string Slug(string value)
        => Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');

    private static (int StartYear, int EndYear) ParseSeason(string season)
    {
        var parts = season.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var startYear) || !int.TryParse(parts[1], out var endYear))
        {
            throw new ArgumentException($"Season '{season}' must be a full two-year label.", nameof(season));
        }

        return (startYear, endYear);
    }
}
