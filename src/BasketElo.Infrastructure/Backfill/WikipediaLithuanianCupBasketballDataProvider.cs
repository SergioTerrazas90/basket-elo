using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// The English LKF Cup history table is the surviving consolidated source for
/// the historical Lithuanian men's cup.  It publishes the championship and
/// third-place results (not the full qualifying/bracket schedule), so this
/// provider deliberately imports only those published Final Four games.
/// </summary>
public sealed class WikipediaLithuanianCupBasketballDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "wikipedia-lithuanian-cup";
    public const string ParserVersion = "wikipedia-lithuanian-cup-v1";
    private const string SourceUrl = "https://en.wikipedia.org/wiki/LKF_Cup";

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!country.Equals("Lithuania", StringComparison.OrdinalIgnoreCase) ||
            (!leagueName.Equals("LKF Cup", StringComparison.OrdinalIgnoreCase) &&
             !leagueName.Equals("Lithuanian Cup", StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        return Task.FromResult<BasketballProviderLeague?>(
            new BasketballProviderLeague(Source, "lkf-cup", "LKF Cup", "LT", "literal"));
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!TryParseSeason(season, out var startYear))
        {
            return ([], false, [$"The historical LKF Cup provider does not accept the season label '{season}'."]);
        }

        if (startYear < 2006 || startYear > 2014)
        {
            return ([], false, [$"The consolidated LKF Cup history table does not cover {season}."]);
        }

        if (!context.CanUseRequest())
        {
            return ([], true, ["The request budget was exhausted before loading the LKF Cup history."]);
        }

        context.ConsumeRequest();
        using var response = await httpClient.GetAsync(SourceUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var games = ParseSeason(document, season, startYear, DateTime.UtcNow, out var warnings);
        return (games, false, warnings);
    }

    internal static IReadOnlyCollection<BasketballProviderGame> ParseSeason(
        string html,
        string season,
        int startYear,
        DateTime fetchedAtUtc,
        out IReadOnlyCollection<string> warnings)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return ParseSeason(document, season, startYear, fetchedAtUtc, out warnings);
    }

    private static IReadOnlyCollection<BasketballProviderGame> ParseSeason(
        HtmlDocument document,
        string season,
        int startYear,
        DateTime fetchedAtUtc,
        out IReadOnlyCollection<string> warnings)
    {
        var warningList = new List<string>();
        var games = new List<BasketballProviderGame>();
        var rows = document.DocumentNode.SelectNodes("//table[contains(@class, 'wikitable')]//tr") is { } rowNodes
            ? rowNodes
            : Enumerable.Empty<HtmlNode>();
        var row = rows
            .FirstOrDefault(candidate => MatchesSeason(candidate.SelectSingleNode("./td[1]")?.InnerText, startYear));
        if (row is null)
        {
            warnings = [$"Wikipedia did not expose an LKF Cup result row for {season}."];
            return games;
        }

        var cells = row.SelectNodes("./td");
        if (cells is null || cells.Count < 9)
        {
            warnings = [$"Wikipedia exposed an incomplete LKF Cup result row for {season}."];
            return games;
        }

        var dates = ParseDateRange(Normalize(cells[8].InnerText), startYear);
        var finalScores = ParseScores(cells[3].InnerText);
        var champion = NormalizeTeam(cells[2].InnerText);
        var finalist = NormalizeTeam(cells[4].InnerText);
        if (finalScores.Count == 0 || string.IsNullOrWhiteSpace(champion) || string.IsNullOrWhiteSpace(finalist))
        {
            warningList.Add($"Skipped the incomplete LKF Cup final result for {season}.");
        }
        else
        {
            for (var index = 0; index < finalScores.Count; index++)
            {
                var date = dates.Count == 0
                    ? new DateTime(startYear + 1, 2, 1, 0, 0, 0, DateTimeKind.Utc)
                    : dates[Math.Min(index, dates.Count - 1)];
                games.Add(CreateGame(
                    $"lkf-{startYear}-final-{index + 1}",
                    date,
                    champion,
                    finalist,
                    finalScores[index],
                    "Final",
                    season,
                    fetchedAtUtc));
            }
        }

        var thirdScores = ParseScores(cells[6].InnerText);
        var third = NormalizeTeam(cells[5].InnerText);
        var fourth = NormalizeTeam(cells[7].InnerText);
        if (thirdScores.Count > 0 && !string.IsNullOrWhiteSpace(third) && !string.IsNullOrWhiteSpace(fourth) &&
            !third.Contains("No 3rd", StringComparison.OrdinalIgnoreCase))
        {
            games.Add(CreateGame(
                $"lkf-{startYear}-third-place",
                dates.LastOrDefault() == default ? new DateTime(startYear + 1, 2, 1, 0, 0, 0, DateTimeKind.Utc) : dates.Last(),
                third,
                fourth,
                thirdScores[0],
                "Third-place",
                season,
                fetchedAtUtc));
        }

        if (games.Count == 0)
        {
            warningList.Add($"Wikipedia returned no parseable published LKF Cup games for {season}.");
        }

        warnings = warningList;
        return games;
    }

    private static BasketballProviderGame CreateGame(
        string sourceGameId,
        DateTime date,
        string home,
        string away,
        (short Home, short Away) score,
        string round,
        string season,
        DateTime fetchedAtUtc)
    {
        return new BasketballProviderGame(
            Source,
            sourceGameId,
            date,
            "finished",
            TeamId(home),
            home,
            TeamId(away),
            away,
            score.Home,
            score.Away,
            new BasketballProviderGameProvenance(SourceUrl, season, fetchedAtUtc, ParserVersion, sourceGameId),
            CompetitionPhase: "Final Four",
            CompetitionRound: round,
            SourceHomeTeamCountryCode: "LT",
            SourceAwayTeamCountryCode: "LT");
    }

    private static bool MatchesSeason(string? label, int startYear)
    {
        var years = Regex.Matches(Normalize(label ?? string.Empty), @"\d{4}")
            .Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToArray();
        return years.Length > 0 && years[0] == (startYear <= 2009 ? startYear + 1 : startYear);
    }

    private static List<(short Home, short Away)> ParseScores(string value)
    {
        var normalized = Normalize(value)
            .Replace("â€“", "-")
            .Replace("–", "-")
            .Replace("—", "-");
        return Regex.Matches(normalized, @"(?<home>\d{1,3})\s*-\s*(?<away>\d{1,3})")
            .Select(match => short.TryParse(match.Groups["home"].Value, out var home) &&
                             short.TryParse(match.Groups["away"].Value, out var away)
                ? (true, Value: (Home: home, Away: away))
                : (false, Value: (Home: (short)0, Away: (short)0)))
            .Where(result => result.Item1)
            .Select(result => result.Value)
            .ToList();
    }

    private static List<DateTime> ParseDateRange(string value, int startYear)
    {
        var dates = Regex.Matches(value, @"(?<day>\d{1,2})\s*(?:-|–|â€“)\s*(?<end>\d{1,2})?\s*(?<month>[A-Za-z]+)\s*(?<year>\d{4})")
            .Select(match =>
            {
                var month = DateTime.ParseExact(match.Groups["month"].Value[..3], "MMM", CultureInfo.InvariantCulture).Month;
                var day = int.Parse(match.Groups["end"].Success ? match.Groups["end"].Value : match.Groups["day"].Value, CultureInfo.InvariantCulture);
                var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
                return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
            })
            .ToList();
        if (dates.Count > 0)
        {
            return dates;
        }

        var single = Regex.Match(value, @"(?<day>\d{1,2})\s+(?<month>[A-Za-z]+)\s+(?<year>\d{4})");
        if (!single.Success)
        {
            return [];
        }

        var parsedMonth = DateTime.ParseExact(single.Groups["month"].Value[..3], "MMM", CultureInfo.InvariantCulture).Month;
        return [new DateTime(int.Parse(single.Groups["year"].Value, CultureInfo.InvariantCulture), parsedMonth, int.Parse(single.Groups["day"].Value, CultureInfo.InvariantCulture), 0, 0, 0, DateTimeKind.Utc)];
    }

    private static bool TryParseSeason(string season, out int startYear)
    {
        var match = Regex.Match(season.Trim(), @"^(?<start>\d{4})-(?<end>\d{4})$", RegexOptions.CultureInvariant);
        startYear = match.Success && int.TryParse(match.Groups["start"].Value, out var value) ? value : 0;
        return match.Success && int.TryParse(match.Groups["end"].Value, out var end) && end == startYear + 1;
    }

    private static string NormalizeTeam(string value)
    {
        var name = Normalize(value)
            .Replace("â€“", "")
            .Replace("Žalgiris", "Zalgiris Kaunas", StringComparison.OrdinalIgnoreCase)
            .Replace("Lietuvos Rytas", "Lietuvos Rytas Vilnius", StringComparison.OrdinalIgnoreCase)
            .Replace("Lietuvos rytas", "Lietuvos Rytas Vilnius", StringComparison.OrdinalIgnoreCase)
            .Replace("Neptūnas", "Neptunas Klaipeda", StringComparison.OrdinalIgnoreCase)
            .Replace("Nevėžis", "Nevezis Kedainiai", StringComparison.OrdinalIgnoreCase)
            .Replace("Šiauliai", "Siauliai", StringComparison.OrdinalIgnoreCase)
            .Replace("Prienai", "Prienai", StringComparison.OrdinalIgnoreCase);
        return Regex.Replace(name, @"\[[^\]]+\]", string.Empty).Trim();
    }

    private static string TeamId(string name) =>
        $"lkf-team-{Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-')}";

    private static string Normalize(string value) =>
        HtmlEntity.DeEntitize(WebUtility.HtmlDecode(value)).Replace('\u00a0', ' ').Trim();
}
