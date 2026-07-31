using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Reads the official ACB match archive for the final Liga Nacional season
/// before the ACB era. The old match pages expose structured Next.js data even
/// though the visible score widgets are placeholders.
/// </summary>
public sealed class AcbOfficialLigaNacionalBasketballDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "acb-official-liga-nacional";
    public const string ParserVersion = "acb-official-liga-nacional-v1";
    private const string BaseUrl = "https://www.acb.com/partido/ver/id/";

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(string country, string leagueName, BackfillExecutionContext context, CancellationToken cancellationToken)
    {
        var league = string.Equals(country, "Spain", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(leagueName, "Liga Nacional", StringComparison.OrdinalIgnoreCase)
            ? new BasketballProviderLeague(Source, "LIGA_NACIONAL", "Liga Nacional", "ES", "start_year")
            : null;
        return Task.FromResult(league);
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league, string season, BackfillExecutionContext context, CancellationToken cancellationToken)
    {
        if (!string.Equals(league.Source, Source, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(league.SourceLeagueId, "LIGA_NACIONAL", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Official ACB Liga Nacional provider only supports Spain: Liga Nacional.");
        var canonicalSeason = SeasonLabelNormalizer.ToFullSeasonLabel(season);
        var (firstGameId, lastGameId) = canonicalSeason switch
        {
            "1956-1957" => (855, 884),
            "1957-1958" => (885, 974),
            "1958-1959" => (975, 1110),
            "1959-1960" => (1111, 1274),
            "1960-1961" => (1275, 1370),
            "1961-1962" => (1371, 1460),
            "1962-1963" => (1461, 1532),
            "1963-1964" => (1533, 1646),
            "1964-1965" => (1647, 1702),
            "1965-1966" => (1703, 1792),
            "1967-1968" => (1903, 2012),
            "1968-1969" => (2013, 2144),
            "1969-1970" => (2145, 2276),
            "1970-1971" => (2277, 2408),
            "1971-1972" => (2409, 2540),
            "1972-1973" => (2541, 2780),
            "1973-1974" => (2781, 2990),
            "1974-1975" => (2991, 3122),
            "1975-1976" => (3123, 3314),
            "1976-1977" => (3315, 3446),
            "1977-1978" => (3447, 3578),
            "1978-1979" => (3579, 3710),
            "1979-1980" => (3711, 3842),
            "1980-1981" => (3843, 4024),
            "1981-1982" => (4025, 4206),
            "1982-1983" => (4207, 4389),
            _ => throw new ArgumentException("Official ACB Liga Nacional coverage currently supports 1956-1957 through 1982-1983.", nameof(season))
        };

        var games = new List<BasketballProviderGame>();
        var warnings = new List<string>();
        for (var id = firstGameId; id <= lastGameId; id++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.CanUseRequest())
            {
                warnings.Add($"Request budget reached after {games.Count} parsed games; remaining official ACB match IDs were not attempted.");
                break;
            }
            context.ConsumeRequest();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{id}");
            request.Headers.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                warnings.Add($"Official ACB match {id} was not found.");
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                warnings.Add($"Official ACB match {id} returned {(int)response.StatusCode}.");
                continue;
            }
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (TryParseMatch(html, id.ToString(CultureInfo.InvariantCulture), season, $"{BaseUrl}{id}", out var game, out var warning))
                games.Add(game!);
            else
                warnings.Add($"Official ACB match {id}: {warning}");
        }
        if (canonicalSeason == "1975-1976" && games.Count == 187)
            warnings.Add("Source completeness review: 1975-1976 should contain 192 games (132 first-phase plus 30 in each second-phase group), but the official ACB archive exposes 187 records. The 5-game gap is intentionally flagged for manual review.");

        return (games.OrderBy(x => x.GameDateTimeUtc).ThenBy(x => x.SourceGameId).ToArray(), false, warnings);
    }

    internal static bool TryParseMatch(string html, string sourceGameId, string season, string sourceUrl, out BasketballProviderGame? game, out string warning)
    {
        game = null;
        warning = string.Empty;
        var header = Match(html, "initialMatchHeader(?<value>.*?)(?:quarterScores|arenaImage)", "value");
        var home = Match(header, "currentHomeScore[^0-9]{0,5}(?<score>\\d{1,3})", "score");
        var away = Match(header, "currentAwayScore[^0-9]{0,5}(?<score>\\d{1,3})", "score");
        var date = Match(header, "start.{0,8}(?<value>\\d{4}-\\d{2}-\\d{2}T[^,}\\\\]+)", "value");
        var homeName = Match(html, "customTagHome\" content=\"(?<value>[^\"]+)", "value");
        var awayName = Match(html, "customTagAway\" content=\"(?<value>[^\"]+)", "value");
        if (!short.TryParse(home, NumberStyles.Integer, CultureInfo.InvariantCulture, out var homeScore) ||
            !short.TryParse(away, NumberStyles.Integer, CultureInfo.InvariantCulture, out var awayScore) ||
            !DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate) ||
            string.IsNullOrWhiteSpace(homeName) || string.IsNullOrWhiteSpace(awayName))
        {
            warning = "structured match teams, date, or final score was not found";
            return false;
        }
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html))).ToLowerInvariant();
        game = new BasketballProviderGame(Source, sourceGameId, parsedDate.ToUniversalTime(), "finished",
            TeamId(homeName), homeName, TeamId(awayName), awayName, homeScore, awayScore,
            new BasketballProviderGameProvenance(sourceUrl, season, DateTime.UtcNow, ParserVersion, revision),
            CompetitionPhase: "Liga Nacional");
        return true;
    }

    private static string Match(string html, string pattern, string group) => Regex.Match(html, pattern, RegexOptions.Singleline).Groups[group].Value;
    private static string CleanJsonValue(string value) => value.Trim().Trim('"', '\\');
    private static string TeamId(string name) => new(name.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
}
