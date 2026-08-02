using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Historical Turkish men's top-flight results from TBLStat.net. TBLStat's
/// season page exposes the competition structure and each team season page
/// exposes dated game rows, so one team page per club is enough to recover the
/// dates without fetching every game detail page.
///
/// The same provider also exposes the published historical Turkish Cup and
/// Presidential Cup (Super Cup) finals. Those overview sources do not publish
/// complete early-round brackets, exact dates, or venue order, so the provider
/// imports only the final records and records those limitations as warnings.
/// </summary>
public sealed partial class TurkishBasketballDataProvider(
    HttpClient httpClient,
    IOptions<TurkishBasketballOptions> options) : IBasketballDataProvider
{
    public const string Source = "turkish-historical";
    public const string ParserVersion = "tblstat-turkey-v1";
    public const string CupParserVersion = "wikipedia-turkish-cup-finals-v1";
    public const string SuperCupParserVersion = "wikipedia-turkish-super-cup-finals-v1";
    public const string LeagueSourceUrl = "https://bsl.tblstat.net/";
    public const string CupSourceUrl = "https://en.wikipedia.org/wiki/Turkish_Basketball_Cup";
    public const string SuperCupSourceUrl = "https://en.wikipedia.org/wiki/Turkish_Basketball_Presidential_Cup";

    private static readonly Regex TeamLinkRegex = new(
        @"go\('team/(?<id>[^/]+)/(?<season>[^']+)'\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GameLinkRegex = new(
        @"go\('game/(?<id>\d+)'\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OpponentRegex = new(
        @"^(?:iç\.|dış\.)\s*(?<name>.*?)\s*\|",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ScoreRegex = new(
        @"(?<home>\d+)\s*-\s*(?<away>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, string> TeamAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Anadolu Efes"] = "Anadolu Efes",
            ["Efes Pilsen"] = "Anadolu Efes",
            ["Fenerbahçe Ülker"] = "Fenerbahçe",
            ["Fenerbahçe Doğuş"] = "Fenerbahçe",
            ["Pınar Karşıyaka"] = "Karşıyaka",
            ["Banvitspor"] = "Banvit",
            ["CASA TED Kolejliler"] = "TED Ankara Kolejliler",
            ["TTNet Beykoz"] = "Beykoz",
            ["Mutlu Akü Selçuk Üni."] = "Selçuk Üniversitesi",
            ["Antalya BŞB."] = "Antalya Büyükşehir Belediyesi",
            ["Mersin B.B."] = "Mersin Büyükşehir Belediyesi",
            ["Mersin BŞB."] = "Mersin Büyükşehir Belediyesi",
            ["Kepez Belediye"] = "Kepez Belediyesi",
            ["Galatasaray Cafe Crown"] = "Galatasaray",
            ["Beşiktaş Cola Turka"] = "Beşiktaş",
            ["Tofaş SAS"] = "Tofaş"
        };

    private static readonly IReadOnlyCollection<FinalRecord> TurkishCupFinals =
    [
        TwoLeg("1966-1967", "Fenerbahçe", "Muhafızgücü", 84, 67, 65, 76),
        TwoLeg("1967-1968", "Altınordu", "Muhafızgücü", 71, 56, 63, 67),
        Single("1968-1969", "İTÜ", "Galatasaray", 64, 56),
        Single("1969-1970", "Galatasaray", "İTÜ", 86, 83),
        Single("1970-1971", "İTÜ", "Beşiktaş", 74, 69),
        Single("1971-1972", "Galatasaray", "İTÜ", 70, 69),
        TwoLeg("1972-1973", "TED Ankara Kolejliler", "Beşiktaş", 64, 68, 68, 56),
        Single("1991-1992", "Paşabahçe", "Nasaş", 79, 71),
        Single("1992-1993", "Tofaş", "Nasaş", 90, 64),
        Single("1993-1994", "Anadolu Efes", "Fenerbahçe", 81, 76),
        Single("1994-1995", "Galatasaray", "Ortaköy", 66, 54),
        Single("1995-1996", "Anadolu Efes", "Türk Telekom", 89, 63),
        Single("1996-1997", "Anadolu Efes", "Fenerbahçe", 84, 71),
        Single("1997-1998", "Anadolu Efes", "Türk Telekom", 71, 67),
        Single("1998-1999", "Tofaş", "Fenerbahçe", 77, 75),
        Single("1999-2000", "Tofaş", "Ülker", 72, 54),
        Single("2000-2001", "Anadolu Efes", "Türk Telekom", 85, 78),
        Single("2001-2002", "Anadolu Efes", "Darüşşafaka", 78, 74),
        Single("2002-2003", "Ülker", "Türk Telekom", 79, 63),
        Single("2003-2004", "Ülker", "Anadolu Efes", 84, 74),
        Single("2004-2005", "Ülker", "Karşıyaka", 73, 41),
        Single("2005-2006", "Anadolu Efes", "Ülker", 74, 68),
        Single("2006-2007", "Anadolu Efes", "Banvit", 73, 59),
        Single("2007-2008", "Türk Telekom", "Oyak Renault", 80, 61),
        Single("2008-2009", "Anadolu Efes", "Erdemirspor", 79, 70),
        Single("2009-2010", "Fenerbahçe Ülker", "Mersin B.B.", 72, 68),
        Single("2010-2011", "Fenerbahçe Ülker", "Beşiktaş Cola Turka", 81, 72)
    ];

    private static readonly IReadOnlyCollection<FinalRecord> TurkishSuperCupFinals =
    [
        Single("1985", "Galatasaray", "Fenerbahçe", 85, 84),
        Single("1986", "Anadolu Efes", "Galatasaray", 101, 100),
        Single("1987", "Karşıyaka", "Beşiktaş", 81, 65),
        Single("1988", "Eczacıbaşı", "Fenerbahçe", 89, 87),
        Single("1989", "Çukurova Sanayi", "Eczacıbaşı", 82, 74),
        Single("1990", "Fenerbahçe", "Galatasaray", 95, 86),
        Single("1991", "Fenerbahçe", "Tofaş", 75, 72),
        Single("1992", "Anadolu Efes", "Paşabahçe", 102, 98),
        Single("1993", "Anadolu Efes", "Tofaş", 88, 63),
        Single("1994", "Fenerbahçe", "Anadolu Efes", 85, 74),
        Single("1995", "Ülker", "Galatasaray", 70, 55),
        Single("1996", "Anadolu Efes", "Türk Telekom", 69, 44),
        Single("1997", "Türk Telekom", "Anadolu Efes", 78, 75),
        Single("1998", "Anadolu Efes", "Ülker", 76, 63),
        Single("1999", "Tofaş", "Anadolu Efes", 77, 66),
        Single("2000", "Anadolu Efes", "Ülker", 66, 65),
        Single("2001", "Ülker", "Anadolu Efes", 94, 87),
        Single("2002", "Ülker", "Anadolu Efes", 83, 78),
        Single("2003", "Ülker", "Anadolu Efes", 68, 66),
        Single("2004", "Ülker", "Anadolu Efes", 68, 66),
        Single("2005", "Ülker", "Anadolu Efes", 83, 72),
        Single("2006", "Anadolu Efes", "Alpella", 77, 61),
        Single("2007", "Fenerbahçe", "Anadolu Efes", 79, 77),
        Single("2008", "Türk Telekom", "Fenerbahçe", 83, 79),
        Single("2009", "Anadolu Efes", "Fenerbahçe Ülker", 81, 74),
        Single("2010", "Anadolu Efes", "Fenerbahçe Ülker", 79, 77)
    ];

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(country, "Turkey", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        var league = string.Equals(leagueName, "Super Ligi", StringComparison.OrdinalIgnoreCase)
            ? new BasketballProviderLeague(Source, "BSL", "Super Ligi", "TR", "start_year")
            : string.Equals(leagueName, "Turkish Cup", StringComparison.OrdinalIgnoreCase)
                ? new BasketballProviderLeague(Source, "TURKISH_CUP", "Turkish Cup", "TR", "start_year")
                : string.Equals(leagueName, "Super Cup", StringComparison.OrdinalIgnoreCase)
                    ? new BasketballProviderLeague(Source, "TURKISH_SUPER_CUP", "Super Cup", "TR", "year")
                    : null;

        return Task.FromResult(league);
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        return league.SourceLeagueId switch
        {
            "BSL" => await GetLeagueGamesAsync(season, context, cancellationToken),
            "TURKISH_CUP" => BuildFinalGames(TurkishCupFinals, season, CupSourceUrl, CupParserVersion, "Turkish Cup"),
            "TURKISH_SUPER_CUP" => BuildFinalGames(TurkishSuperCupFinals, season, SuperCupSourceUrl, SuperCupParserVersion, "Turkish Presidential Cup"),
            _ => throw new InvalidOperationException($"Turkish historical provider does not support '{league.SourceLeagueId}'.")
        };
    }

    internal static IReadOnlyCollection<TeamRef> ParseTeamsPage(string html, string seasonCode)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var teams = new Dictionary<string, TeamRef>(StringComparer.Ordinal);
        foreach (var anchor in document.DocumentNode.SelectNodes("//a[@onclick]") ?? Enumerable.Empty<HtmlNode>())
        {
            var match = TeamLinkRegex.Match(anchor.GetAttributeValue("onclick", string.Empty));
            if (!match.Success || !string.Equals(match.Groups["season"].Value, seasonCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var observedName = CleanText(anchor.InnerText);
            if (string.IsNullOrWhiteSpace(observedName))
            {
                continue;
            }

            var canonicalName = CanonicalTeamName(observedName);
            teams[match.Groups["id"].Value] = new TeamRef(
                match.Groups["id"].Value,
                observedName,
                canonicalName,
                SourceTeamId(canonicalName));
        }

        return teams.Values.OrderBy(x => x.CanonicalName, StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyCollection<BasketballProviderGame> ParseTeamSchedule(
        string html,
        TeamRef currentTeam,
        IReadOnlyDictionary<string, string> sourceTeamIdsByName,
        string season,
        string sourceUrl,
        DateTime fetchedAtUtc)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var games = new List<BasketballProviderGame>();
        foreach (var row in document.DocumentNode.SelectNodes("//tr[td[contains(concat(' ', normalize-space(@class), ' '), ' c ')]]") ?? Enumerable.Empty<HtmlNode>())
        {
            var cells = row.SelectNodes("./td");
            if (cells is null || cells.Count < 3 ||
                !DateTime.TryParseExact(
                    CleanText(cells[1].InnerText),
                    "dd.MM.yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var gameDate))
            {
                continue;
            }

            var rowText = CleanText(cells[2].InnerText);
            var opponentMatch = OpponentRegex.Match(rowText);
            var scoreMatch = ScoreRegex.Match(rowText);
            if (!opponentMatch.Success || !scoreMatch.Success ||
                !short.TryParse(scoreMatch.Groups["home"].Value, out var listedHomeScore) ||
                !short.TryParse(scoreMatch.Groups["away"].Value, out var listedAwayScore))
            {
                continue;
            }

            var observedOpponent = CleanText(opponentMatch.Groups["name"].Value);
            var opponentName = CanonicalTeamName(observedOpponent);
            var currentIsHome = rowText.StartsWith("iç.", StringComparison.OrdinalIgnoreCase);
            var homeName = currentIsHome ? currentTeam.CanonicalName : opponentName;
            var awayName = currentIsHome ? opponentName : currentTeam.CanonicalName;
            // TBLStat always displays the score in venue order. The iç./dış.
            // marker only tells us which team is the current team; it does
            // not change the score order on an away team's page.
            var homeScore = listedHomeScore;
            var awayScore = listedAwayScore;
            var homeTeamId = sourceTeamIdsByName.GetValueOrDefault(homeName, SourceTeamId(homeName));
            var awayTeamId = sourceTeamIdsByName.GetValueOrDefault(awayName, SourceTeamId(awayName));
            var gameLink = row.SelectNodes(".//a[@onclick]")?.Select(x => GameLinkRegex.Match(x.GetAttributeValue("onclick", string.Empty)))
                .FirstOrDefault(x => x.Success);
            var sourceGameId = gameLink is { Success: true }
                ? $"tblstat-game-{gameLink.Groups["id"].Value}"
                : CreateSyntheticGameId(season, gameDate, homeTeamId, awayTeamId, homeScore, awayScore);
            var round = CleanText(cells[0].InnerText);

            games.Add(new BasketballProviderGame(
                Source,
                sourceGameId,
                gameDate,
                "finished",
                homeTeamId,
                homeName,
                awayTeamId,
                awayName,
                homeScore,
                awayScore,
                new BasketballProviderGameProvenance(
                    sourceUrl,
                    season,
                    fetchedAtUtc,
                    ParserVersion),
                CompetitionPhase: PhaseFor(round),
                CompetitionRound: round,
                SourceHomeTeamCountryCode: "TR",
                SourceAwayTeamCountryCode: "TR"));
        }

        return games;
    }

    internal static IReadOnlyCollection<BasketballProviderGame> ParseFinals(string leagueName, string season)
    {
        var finals = leagueName.Equals("Turkish Cup", StringComparison.OrdinalIgnoreCase)
            ? TurkishCupFinals
            : TurkishSuperCupFinals;
        var sourceUrl = leagueName.Equals("Turkish Cup", StringComparison.OrdinalIgnoreCase)
            ? CupSourceUrl
            : SuperCupSourceUrl;
        var parserVersion = leagueName.Equals("Turkish Cup", StringComparison.OrdinalIgnoreCase)
            ? CupParserVersion
            : SuperCupParserVersion;
        return BuildFinalGames(finals, season, sourceUrl, parserVersion, leagueName).Games;
    }

    private async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetLeagueGamesAsync(
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var seasonCode = SeasonCode(season);
        var warnings = new List<string>();
        if (!context.CanUseRequest())
        {
            return ([], true, [$"Request budget reached before TBLStat season {seasonCode} could be fetched."]);
        }

        context.ConsumeRequest();
        await DelayAsync(cancellationToken);
        var teamsHtml = await httpClient.GetStringAsync($"teams/{seasonCode}", cancellationToken);
        var teams = ParseTeamsPage(teamsHtml, seasonCode);
        if (teams.Count == 0)
        {
            return ([], false, [$"TBLStat did not expose any teams for season {season} ({seasonCode})."]);
        }

        var sourceTeamIdsByName = teams.ToDictionary(x => x.CanonicalName, x => x.SourceTeamId, StringComparer.OrdinalIgnoreCase);
        var games = new List<BasketballProviderGame>();
        var hasMorePages = false;
        foreach (var team in teams)
        {
            if (!context.CanUseRequest())
            {
                hasMorePages = true;
                warnings.Add($"Request budget reached after {games.Count} parsed team rows; remaining TBLStat team pages were not fetched.");
                break;
            }

            context.ConsumeRequest();
            await DelayAsync(cancellationToken);
            var teamUrl = $"team/{team.SourceId}/{seasonCode}";
            var teamHtml = await httpClient.GetStringAsync(teamUrl, cancellationToken);
            games.AddRange(ParseTeamSchedule(
                teamHtml,
                team,
                sourceTeamIdsByName,
                season,
                new Uri(httpClient.BaseAddress!, teamUrl).ToString(),
                DateTime.UtcNow));
        }

        var uniqueGames = games
            .GroupBy(x => x.SourceGameId, StringComparer.Ordinal)
            .Select(x => x.First())
            .OrderBy(x => x.GameDateTimeUtc)
            .ThenBy(x => x.SourceGameId, StringComparer.Ordinal)
            .ToArray();
        warnings.Add($"TBLStat exposed {teams.Count} teams and {uniqueGames.Length} unique game(s) for {season}.");
        warnings.Add("TBLStat team pages provide dated game rows; rows without a source game link use a deterministic source ID derived from season, date, teams and score.");
        return (uniqueGames, hasMorePages, warnings);
    }

    private async Task DelayAsync(CancellationToken cancellationToken)
    {
        if (options.Value.MinRequestIntervalMilliseconds > 0)
        {
            await Task.Delay(options.Value.MinRequestIntervalMilliseconds, cancellationToken);
        }
    }

    private static (IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings) BuildFinalGames(
        IReadOnlyCollection<FinalRecord> finals,
        string season,
        string sourceUrl,
        string parserVersion,
        string competitionName)
    {
        var final = finals.FirstOrDefault(x => string.Equals(x.Season, season, StringComparison.OrdinalIgnoreCase));
        if (final is null)
        {
            return ([], false, [$"No published {competitionName} final record was configured for {season}."]);
        }

        var games = final.Games.Select((game, index) => new BasketballProviderGame(
            Source,
            $"{Slug(competitionName)}-{final.Season}-final-{index + 1}",
            FinalDate(final.Season, competitionName, index),
            "finished",
            SourceTeamId(CanonicalTeamName(game.Home)),
            CanonicalTeamName(game.Home),
            SourceTeamId(CanonicalTeamName(game.Away)),
            CanonicalTeamName(game.Away),
            game.HomeScore,
            game.AwayScore,
            new BasketballProviderGameProvenance(
                sourceUrl,
                final.Season,
                DateTime.UtcNow,
                parserVersion,
                "historical-finals-overview"),
            CompetitionPhase: "Final phase",
            CompetitionRound: "Final",
            SourceHomeTeamCountryCode: "TR",
            SourceAwayTeamCountryCode: "TR")).ToArray();

        var warnings = competitionName.Equals("Turkish Cup", StringComparison.OrdinalIgnoreCase)
            ? new[]
            {
                "The historical Turkish Cup overview publishes final/final-series records only; earlier rounds are not exposed by this source.",
                "The source lists the champion first and does not publish exact final dates or venue order; dates are deterministic placement dates and the listed order is retained as home/away."
            }
            : new[]
            {
                "The historical Turkish Presidential Cup overview publishes the final record only; dates and venue order are not exposed by this source.",
                "The source lists the champion first; deterministic placement dates and the listed order are retained as home/away."
            };
        return (games, false, warnings);
    }

    private static DateTime FinalDate(string season, string competitionName, int index)
    {
        if (competitionName.Equals("Turkish Super Cup", StringComparison.OrdinalIgnoreCase) ||
            competitionName.Equals("Super Cup", StringComparison.OrdinalIgnoreCase) ||
            competitionName.Equals("Turkish Presidential Cup", StringComparison.OrdinalIgnoreCase))
        {
            return new DateTime(int.Parse(season, CultureInfo.InvariantCulture), 9, 15 + index, 0, 0, 0, DateTimeKind.Utc);
        }

        var startYear = int.Parse(season[..4], CultureInfo.InvariantCulture);
        return new DateTime(startYear + 1, 4, 15 + index, 0, 0, 0, DateTimeKind.Utc);
    }

    private static string SeasonCode(string season)
    {
        var startYear = SeasonLabelNormalizer.ParseStartYear(season);
        return $"{startYear % 100:00}{(startYear + 1) % 100:00}";
    }

    private static string PhaseFor(string round)
    {
        if (round.StartsWith("NS", StringComparison.OrdinalIgnoreCase))
        {
            return "Regular season";
        }

        if (round.StartsWith("P", StringComparison.OrdinalIgnoreCase))
        {
            return "Playoffs";
        }

        return "Competition phase";
    }

    private static string CanonicalTeamName(string observedName)
    {
        var clean = CleanText(observedName);
        return TeamAliases.TryGetValue(clean, out var canonical) ? canonical : clean;
    }

    private static string SourceTeamId(string teamName) => $"turkey:club:{Slug(teamName)}";

    private static string CreateSyntheticGameId(
        string season,
        DateTime date,
        string homeTeamId,
        string awayTeamId,
        short homeScore,
        short awayScore)
    {
        var value = $"{season}|{date:yyyy-MM-dd}|{homeTeamId}|{awayTeamId}|{homeScore}|{awayScore}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];
        return $"tblstat-synthetic-{hash}";
    }

    private static string CleanText(string value)
    {
        var decoded = System.Net.WebUtility.HtmlDecode(value) ?? string.Empty;
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static string Slug(string value) =>
        new string(value.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray())
            .Trim('-');

    private static FinalRecord Single(string season, string home, string away, short homeScore, short awayScore) =>
        new(season, [new GameRecord(home, away, homeScore, awayScore)]);

    private static FinalRecord TwoLeg(
        string season,
        string firstHome,
        string firstAway,
        short firstHomeScore,
        short firstAwayScore,
        short secondHomeScore,
        short secondAwayScore) =>
        new(season,
        [
            new GameRecord(firstHome, firstAway, firstHomeScore, firstAwayScore),
            new GameRecord(firstHome, firstAway, secondHomeScore, secondAwayScore)
        ]);

    internal sealed record TeamRef(string SourceId, string ObservedName, string CanonicalName, string SourceTeamId);

    private sealed record FinalRecord(string Season, IReadOnlyCollection<GameRecord> Games);

    private sealed record GameRecord(string Home, string Away, short HomeScore, short AwayScore);
}
