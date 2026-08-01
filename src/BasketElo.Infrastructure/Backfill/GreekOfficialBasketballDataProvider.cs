using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Official historical Greek top-flight and men's Cup results. ESAKE supplies
/// the league and EOK (basket.gr) supplies the Cup through its WordPress API.
/// </summary>
public sealed class GreekOfficialBasketballDataProvider(
    HttpClient httpClient,
    IOptions<GreekOfficialOptions> options) : IBasketballDataProvider
{
    public const string Source = "greek-official";
    public const string EsakeParserVersion = "esake-a1-v1";
    public const string EokCupParserVersion = "eok-cup-v1";
    public const string BitzenisParserVersion = "bitzenis-a1-v1";
    public const string SportGrParserVersion = "sportgr-a1-v1";

    private const string LeagueSourceId = "ESAKE_A1";
    private const string CupSourceId = "EOK_GREEK_CUP";
    private const int MaxPlayoffSeries = 40;
    private const int EmptyPlayoffSeriesToStop = 3;

    private const string SportGr1997RegularCapture =
        "web/20080528083751id_/http://archive.sport.gr/basket/hellas/a1/";
    private const string SportGr1997PlayoffCapture =
        "web/20120601060752id_/http://archive.sport.gr/playoffs/playoffs/index.htm";
    private const string SportGr1998RegularCapture =
        "web/20110419043354id_/http://archive.sport.gr/basket/hellas99/a1/";
    private const string SportGr1998PlayoffCapture =
        "web/20100528104633id_/http://archive.sport.gr/play99/play/index.htm";

    private static readonly IReadOnlyDictionary<int, string> EsakeChampionshipIds =
        new Dictionary<int, string>
        {
            [1999] = "0000000E",
            [2000] = "0000000D",
            [2001] = "0000000C",
            [2002] = "0000000B",
            [2003] = "0000000A",
            [2004] = "00000009",
            [2005] = "00000008",
            [2006] = "00000007",
            [2007] = "00000006"
        };

    private static readonly IReadOnlyDictionary<int, int> EokCupPostIds =
        new Dictionary<int, int>
        {
            [1996] = 1750,
            [1997] = 1751,
            [1998] = 1752,
            [1999] = 1753,
            [2000] = 1754,
            [2001] = 1755,
            [2002] = 1756,
            // The official 2003-2004 page omits its first 14 games.
            [2004] = 1759,
            [2005] = 1761,
            [2006] = 1763,
            [2007] = 1765
        };

    private static readonly IReadOnlyDictionary<string, string> CanonicalTeamNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["αεκ"] = "AEK Athens",
            ["α ε κ"] = "AEK Athens",
            ["aek"] = "AEK Athens",
            ["paok"] = "PAOK",
            ["bao"] = "BAO",
            ["olympiakos"] = "Olympiacos",
            ["larissa"] = "GS Larissa",
            ["larisa"] = "GS Larissa",
            ["λαρισα"] = "GS Larissa",
            ["αρης"] = "Aris",
            ["ασ αρης"] = "Aris",
            ["ολυμπιακος"] = "Olympiacos",
            ["ολυμπιακος σφπ"] = "Olympiacos",
            ["παναθηναικος"] = "Panathinaikos",
            ["παναθηναικος αο"] = "Panathinaikos",
            ["παοκ"] = "PAOK",
            ["π α ο κ"] = "PAOK",
            ["πανιωνιος"] = "Panionios",
            ["πανιωνιος γσ"] = "Panionios",
            ["περιστερι"] = "Peristeri",
            ["γσ περιστεριου"] = "Peristeri",
            ["ηρακλης"] = "Iraklis",
            ["γσ ηρακλης"] = "Iraklis",
            ["ηρακλειο"] = "Irakleio",
            ["ηρακλειο οαα"] = "Irakleio",
            ["μαρουσι"] = "Maroussi",
            ["γσ αμαρουσιου"] = "Maroussi",
            ["δαφνη"] = "Dafni",
            ["αο δαφνης"] = "Dafni",
            ["ιωνικος ν φ"] = "Ionikos NF",
            ["ιονικος νφ"] = "Ionikos NF",
            ["καοδ"] = "Kao Drama",
            ["καο δραμας"] = "Kao Drama",
            ["νηαρ ηστ"] = "Near East",
            ["αο ν ηστ"] = "Near East",
            ["αο νηαρ ηστ"] = "Near East",
            ["αο νηαρ ησ"] = "Near East",
            ["απολλων π"] = "Apollon Patras",
            ["απολλων πατρας"] = "Apollon Patras",
            ["απολλων πατρων"] = "Apollon Patras",
            ["ασ απολλων πατρας"] = "Apollon Patras",
            ["ασ απολλων"] = "Apollon Patras",
            ["γσ απολλων"] = "Apollon Patras",
            ["παπαγου"] = "Papagou",
            ["ασ παπαγου"] = "Papagou",
            ["παγκρατι"] = "Pagrati",
            ["αο παγκρατιου"] = "Pagrati",
            ["πανελληνιος"] = "Panellinios",
            ["πανελληνιος γσ"] = "Panellinios",
            ["σπορτιγκ"] = "Sporting",
            ["αο σπορτιγκ"] = "Sporting",
            ["μακεδονικος"] = "Makedonikos",
            ["απσ μακεδονικος"] = "Makedonikos",
            ["μιλων"] = "Milon Aons",
            ["αο μιλων"] = "Milon Aons",
            ["αονσ μιλων"] = "Milon Aons",
            ["κολοσσος"] = "Kolossos Rhodes",
            ["αο κολοσσος"] = "Kolossos Rhodes",
            ["ρεθυμνο"] = "Rethymno",
            ["ηλυσιακος"] = "Ilisiakos",
            ["ηλυσιακος αο"] = "Ilisiakos",
            ["μεντ"] = "MENT",
            ["αιγαλεω"] = "Egaleo",
            ["εσπερος"] = "Esperos",
            ["ποκ εσπερος"] = "Esperos",
            ["αμπελοκηποι"] = "Ampelokipoi",
            ["αο αμπελοκηπων"] = "Ampelokipoi",
            ["αελ γσ λαρισας"] = "AEL 1964 B.C.",
            ["αεπ ολυμπιας"] = "Olympias Patras",
            ["γ σ ολυμπια λαρισας"] = "Olympia Larissa",
            ["ολυμπια γε"] = "Olympia Larissa",
            ["πειραικος"] = "Peiraikos",
            ["πειραικος συνδεσμος"] = "Peiraikos",
            ["πειραικος συνδ"] = "Peiraikos",
            ["αο αμυντας"] = "Amyntas",
            ["αμυντας"] = "Amyntas",
            ["ασε δουκα"] = "Douka",
            ["δουκα"] = "Douka",
            ["πανερυθραικος ασ"] = "Panerythraikos",
            ["φιλιππος βεροιας"] = "Filippos Verias",
            ["φιλιππος"] = "Filippos Verias",
            ["ασ φιλιππος βεροιας"] = "Filippos Verias",
            ["χανθ"] = "Hanth",
            ["γας κομοτηνη"] = "Komotini",
            ["γας κομοτηνης"] = "Komotini",
            ["γσ λαρισης"] = "GS Larissa",
            ["γσ λαυριου"] = "Lavrio",
            ["λαυριο"] = "Lavrio",
            ["αοκ ικαροι σερρων"] = "Ikaros Serres",
            ["ασ τρικαλα 2000"] = "Trikala",
            ["τρικαλα 2000"] = "Trikala"
        }.ToDictionary(pair => NormalizeKey(pair.Key), pair => pair.Value, StringComparer.Ordinal);

    private static readonly HashSet<string> ClubAffixes = new[]
    {
        "αο", "ασ", "γσ", "κασ", "καο", "ποκ", "απσ", "αμσ", "μαο", "γας", "πας",
        "σφκ", "αονσ", "αγε", "αε", "αοκ", "ασε", "αλφ", "μας", "ανσ", "οαα", "σφπ"
    }.Select(NormalizeKey).ToHashSet(StringComparer.Ordinal);

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(country, "Greece", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        BasketballProviderLeague? league = leagueName.ToLowerInvariant() switch
        {
            "a1" => new(Source, LeagueSourceId, "A1", "GR", "start_year"),
            "greek cup" => new(Source, CupSourceId, "Greek Cup", "GR", "start_year"),
            _ => null
        };
        return Task.FromResult(league);
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var startYear = SeasonLabelNormalizer.ParseStartYear(season);
        return league.SourceLeagueId switch
        {
            LeagueSourceId when startYear is >= 1996 and <= 1998 =>
                await GetLegacyLeagueGamesAsync(season, startYear, context, cancellationToken),
            LeagueSourceId when EsakeChampionshipIds.ContainsKey(startYear) =>
                await GetLeagueGamesAsync(season, startYear, context, cancellationToken),
            CupSourceId when EokCupPostIds.ContainsKey(startYear) =>
                await GetCupGamesAsync(season, startYear, context, cancellationToken),
            LeagueSourceId => throw new ArgumentException(
                "Historical Greek coverage supports 1996-1997 through 2007-2008.", nameof(season)),
            CupSourceId => throw new ArgumentException(
                "Complete EOK Cup pages are cataloged from 1996-1997 through 2007-2008, excluding incomplete 2003-2004.", nameof(season)),
            _ => throw new InvalidOperationException("Greek official provider only supports Greece: A1 and Greek Cup.")
        };
    }

    private async Task<(IReadOnlyCollection<BasketballProviderGame>, bool, IReadOnlyCollection<string>)> GetLegacyLeagueGamesAsync(
        string season,
        int startYear,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.Ordinal);
        var warnings = new List<string>
        {
            "Legacy Greek archive dates are imported at 12:00 UTC because historical tip-off times are incomplete."
        };
        var hasMorePages = false;

        if (startYear == 1996)
        {
            if (!context.CanUseRequest())
            {
                return (games.Values.ToArray(), true, warnings);
            }
            var url = options.Value.Bitzenis1996Url;
            var html = await FetchStringAsync(url, context, cancellationToken);
            foreach (var game in ParseBitzenisRegularSeason(html, season, url))
            {
                games[game.SourceGameId] = game;
            }
            warnings.Add(
                "The 1996-1997 source is complete for all 182 regular-season games; undated playoff summary rows are intentionally excluded.");
        }
        else
        {
            var regularCapture = startYear == 1997 ? SportGr1997RegularCapture : SportGr1998RegularCapture;
            for (var firstRound = 1; firstRound <= 13; firstRound++)
            {
                if (!context.CanUseRequest())
                {
                    hasMorePages = true;
                    break;
                }
                var url = new Uri(new Uri(options.Value.WaybackBaseUrl),
                    $"{regularCapture}{firstRound}-{firstRound + 13}.htm").ToString();
                var html = await FetchLegacyGreekStringAsync(url, context, cancellationToken);
                foreach (var game in ParseSportGrRegularSeason(html, season, startYear, url))
                {
                    games[game.SourceGameId] = game;
                }
            }

            if (!hasMorePages)
            {
                if (!context.CanUseRequest())
                {
                    hasMorePages = true;
                }
                else
                {
                    var playoffCapture = startYear == 1997 ? SportGr1997PlayoffCapture : SportGr1998PlayoffCapture;
                    var url = new Uri(new Uri(options.Value.WaybackBaseUrl), playoffCapture).ToString();
                    var html = await FetchLegacyGreekStringAsync(url, context, cancellationToken);
                    foreach (var game in ParseSportGrPlayoffs(html, season, startYear, url))
                    {
                        games[game.SourceGameId] = game;
                    }
                }
            }
        }

        var regularCount = games.Values.Count(game => game.CompetitionPhase == "Regular Season");
        var expectedRegularCount = startYear == 1998 ? 181 : 182;
        if (regularCount != expectedRegularCount)
        {
            warnings.Add($"Expected {expectedRegularCount} scored regular-season games but parsed {regularCount}; this season is incomplete.");
        }
        if (startYear == 1998)
        {
            warnings.Add("Panionios-AEK was interrupted and Panionios was nullified; the source publishes no final score, so the game is excluded.");
        }
        return (games.Values.OrderBy(game => game.GameDateTimeUtc).ThenBy(game => game.SourceGameId).ToArray(),
            hasMorePages, warnings);
    }

    private async Task<(IReadOnlyCollection<BasketballProviderGame>, bool, IReadOnlyCollection<string>)> GetLeagueGamesAsync(
        string season,
        int startYear,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var championshipId = EsakeChampionshipIds[startYear];
        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.Ordinal);
        var warnings = new List<string>
        {
            "ESAKE archive dates are imported at 12:00 UTC because historical tip-off times are not reliable."
        };
        var hasMorePages = false;

        var landingUrl = EsakeResultsUrl(championshipId, "00000001", "undefined");
        if (!context.CanUseRequest())
        {
            return (games.Values.ToArray(), true, warnings);
        }
        var landingHtml = await FetchStringAsync(landingUrl, context, cancellationToken);
        var regularRounds = ParseEsakeSeries(landingHtml);
        if (regularRounds.Count == 0)
        {
            warnings.Add("ESAKE exposed no regular-season rounds.");
        }

        foreach (var series in regularRounds)
        {
            if (!context.CanUseRequest())
            {
                hasMorePages = true;
                break;
            }
            var url = EsakeResultsUrl(championshipId, "00000001", series);
            var html = await FetchStringAsync(url, context, cancellationToken, landingUrl);
            foreach (var game in ParseEsakeRound(html, season, startYear, "Regular Season", series, url))
            {
                games[game.SourceGameId] = game;
            }
        }

        if (!hasMorePages)
        {
            var consecutiveEmpty = 0;
            for (var seriesNumber = 1; seriesNumber <= MaxPlayoffSeries; seriesNumber++)
            {
                if (!context.CanUseRequest())
                {
                    hasMorePages = true;
                    break;
                }
                var series = seriesNumber.ToString("00", CultureInfo.InvariantCulture);
                var url = EsakeResultsUrl(championshipId, "00000002", series);
                var html = await FetchStringAsync(url, context, cancellationToken, landingUrl);
                var parsed = ParseEsakeRound(html, season, startYear, "Playoffs", series, url);
                if (parsed.Count == 0)
                {
                    consecutiveEmpty++;
                    if (consecutiveEmpty >= EmptyPlayoffSeriesToStop)
                    {
                        break;
                    }
                }
                else
                {
                    consecutiveEmpty = 0;
                    foreach (var game in parsed)
                    {
                        games[game.SourceGameId] = game;
                    }
                }
            }
        }

        if (!games.Values.Any(game => game.CompetitionPhase == "Regular Season"))
        {
            warnings.Add("No regular-season games were parsed; this season must not be treated as complete.");
        }
        return (games.Values.OrderBy(game => game.GameDateTimeUtc).ThenBy(game => game.SourceGameId).ToArray(), hasMorePages, warnings);
    }

    private async Task<(IReadOnlyCollection<BasketballProviderGame>, bool, IReadOnlyCollection<string>)> GetCupGamesAsync(
        string season,
        int startYear,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var postId = EokCupPostIds[startYear];
        var url = new Uri(new Uri(options.Value.EokBaseUrl), $"wp-json/wp/v2/posts/{postId}").ToString();
        var json = await FetchStringAsync(url, context, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var rendered = root.GetProperty("content").GetProperty("rendered").GetString() ?? string.Empty;
        var sourceUrl = root.GetProperty("link").GetString() ?? url;
        var revision = root.TryGetProperty("modified_gmt", out var modified)
            ? modified.GetString() ?? Hash(rendered)
            : Hash(rendered);
        var parsed = ParseEokCup(rendered, season, postId, sourceUrl, revision);
        var warnings = new List<string>(parsed.Warnings)
        {
            "EOK Cup dates are imported at 12:00 UTC because historical tip-off times are not published."
        };
        return (parsed.Games, false, warnings);
    }

    internal static IReadOnlyList<BasketballProviderGame> ParseBitzenisRegularSeason(
        string html,
        string season,
        string sourceUrl)
    {
        var startYear = SeasonLabelNormalizer.ParseStartYear(season);
        var revision = Hash(html);
        var games = new List<BasketballProviderGame>();
        foreach (Match tableMatch in Regex.Matches(html, @"<table\b[^>]*>(.*?)</table>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var cells = LegacyCells(tableMatch.Groups[1].Value);
            if (cells.Count < 31)
            {
                continue;
            }
            var roundMatch = Regex.Match(cells[0], @"Game\s+(\d+)\s*&\s*(\d+)", RegexOptions.IgnoreCase);
            if (!roundMatch.Success || !TryParseLegacyDate(cells[1], startYear, out var firstDate) ||
                !TryParseLegacyDate(cells[2], startYear, out var secondDate))
            {
                continue;
            }
            var firstRound = int.Parse(roundMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var secondRound = int.Parse(roundMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            var rowOrdinal = 0;
            for (var index = 3; index + 3 < cells.Count; index += 4)
            {
                rowOrdinal++;
                var firstTeam = cells[index];
                var secondTeam = cells[index + 1];
                if (!TryParseLegacyScore(cells[index + 2], out var firstHomeScore, out var firstAwayScore) ||
                    !TryParseLegacyScore(cells[index + 3], out var secondHomeScore, out var secondAwayScore))
                {
                    continue;
                }
                AddLegacyGame(games, season, firstDate, firstTeam, firstHomeScore, secondTeam, firstAwayScore,
                    "Regular Season", $"Round {firstRound}", sourceUrl, BitzenisParserVersion, revision,
                    $"bitzenis:{startYear}:{firstRound:D2}:{rowOrdinal}");
                AddLegacyGame(games, season, secondDate, secondTeam, secondHomeScore, firstTeam, secondAwayScore,
                    "Regular Season", $"Round {secondRound}", sourceUrl, BitzenisParserVersion, revision,
                    $"bitzenis:{startYear}:{secondRound:D2}:{rowOrdinal}");
            }
        }
        return games;
    }

    internal static IReadOnlyList<BasketballProviderGame> ParseSportGrRegularSeason(
        string html,
        string season,
        int startYear,
        string sourceUrl)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var pageRoundMatch = Regex.Match(sourceUrl, @"/(\d+)-(\d+)\.htm", RegexOptions.IgnoreCase);
        if (!pageRoundMatch.Success)
        {
            return [];
        }
        var firstRound = int.Parse(pageRoundMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var secondRound = int.Parse(pageRoundMatch.Groups[2].Value, CultureInfo.InvariantCulture);
        DateTime? firstFallbackDate = null;
        DateTime? secondFallbackDate = null;
        foreach (var row in document.DocumentNode.SelectNodes("//tr") ?? Enumerable.Empty<HtmlNode>())
        {
            var cells = row.SelectNodes("./th|./td");
            if (cells is null || cells.Count < 3)
            {
                continue;
            }
            var texts = cells.Select(cell => Clean(cell.InnerText)).ToArray();
            if (!texts.Any(value => value.Contains("Αγωνιστική", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            var dateValues = texts.SelectMany(value => Regex.Matches(value, @"(?<!\d)\d{1,2}/\d{1,2}(?!\d)")
                    .Select(match => match.Value))
                .ToArray();
            if (dateValues.Length >= 2)
            {
                if (TryParseLegacyDate(dateValues[0], startYear, out var parsedFirst)) firstFallbackDate = parsedFirst;
                if (TryParseLegacyDate(dateValues[^1], startYear, out var parsedSecond)) secondFallbackDate = parsedSecond;
            }
        }

        var games = new List<BasketballProviderGame>();
        var revision = Hash(html);
        var rowOrdinal = 0;
        foreach (var row in document.DocumentNode.SelectNodes("//tr") ?? Enumerable.Empty<HtmlNode>())
        {
            var cells = row.SelectNodes("./th|./td");
            if (cells is null || cells.Count != 3 ||
                !TrySplitLegacyTeams(Clean(cells[1].InnerText), out var firstTeam, out var secondTeam))
            {
                continue;
            }
            rowOrdinal++;
            var firstHref = cells[0].SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty) ?? string.Empty;
            var secondHref = cells[2].SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty) ?? string.Empty;
            var firstDate = TryParseLegacyHrefDate(firstHref, out var parsedFirstDate)
                ? parsedFirstDate
                : firstFallbackDate;
            var secondDate = TryParseLegacyHrefDate(secondHref, out var parsedSecondDate)
                ? parsedSecondDate
                : secondFallbackDate;
            if (firstDate.HasValue &&
                TryParseLegacyScore(Clean(cells[0].InnerText), out var firstHomeScore, out var firstAwayScore))
            {
                AddLegacyGame(games, season, firstDate.Value, firstTeam, firstHomeScore, secondTeam, firstAwayScore,
                    "Regular Season", $"Round {firstRound}", ResolveLegacyUrl(sourceUrl, firstHref),
                    SportGrParserVersion, revision, $"sportgr:{startYear}:{firstRound:D2}:{rowOrdinal}");
            }
            if (secondDate.HasValue &&
                TryParseLegacyScore(Clean(cells[2].InnerText), out var secondHomeScore, out var secondAwayScore))
            {
                AddLegacyGame(games, season, secondDate.Value, secondTeam, secondHomeScore, firstTeam, secondAwayScore,
                    "Regular Season", $"Round {secondRound}", ResolveLegacyUrl(sourceUrl, secondHref),
                    SportGrParserVersion, revision, $"sportgr:{startYear}:{secondRound:D2}:{rowOrdinal}");
            }
        }
        return games;
    }

    internal static IReadOnlyList<BasketballProviderGame> ParseSportGrPlayoffs(
        string html,
        string season,
        int startYear,
        string sourceUrl)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var games = new List<BasketballProviderGame>();
        var revision = Hash(html);
        var ordinal = 0;

        foreach (var table in document.DocumentNode.SelectNodes("//table") ?? Enumerable.Empty<HtmlNode>())
        {
            var header = Clean(table.SelectSingleNode("./tr[1]//th")?.InnerText ?? string.Empty);
            var headerKey = NormalizeKey(header);
            var round = headerKey switch
            {
                var value when value.Contains("ημιτελ", StringComparison.Ordinal) => "Semifinals",
                var value when value.Contains("τελικ", StringComparison.Ordinal) => "Finals",
                var value when value.Contains("β φαση", StringComparison.Ordinal) ||
                                   value.Contains("b φαση", StringComparison.Ordinal) ||
                                   value.Contains("β γυρος", StringComparison.Ordinal) ||
                                   value.Contains("b γυρος", StringComparison.Ordinal) => "Quarterfinals",
                var value when value.Contains("α φαση", StringComparison.Ordinal) ||
                                   value.Contains("a φαση", StringComparison.Ordinal) ||
                                   value.Contains("α γυρος", StringComparison.Ordinal) ||
                                   value.Contains("a γυρος", StringComparison.Ordinal) => "First Round",
                _ => string.Empty
            };
            if (round.Length == 0)
            {
                continue;
            }

            foreach (var row in table.SelectNodes("./tr") ?? Enumerable.Empty<HtmlNode>())
            {
                var cells = row.SelectNodes("./th|./td");
                if (cells is null)
                {
                    continue;
                }
                var texts = cells.Select(cell => Clean(cell.InnerText)).ToArray();

                // The 1997-1998 page stores one dated game per row.
                if (texts.Length >= 4 && TryParseLegacyDate(texts[0], startYear, out var rowDate) &&
                    TrySplitLegacyTeams(texts[2], out var datedHome, out var datedAway) &&
                    TryParseLegacyScore(texts[3], out var datedHomeScore, out var datedAwayScore))
                {
                    ordinal++;
                    var href = cells[3].SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty) ?? string.Empty;
                    AddLegacyGame(games, season, rowDate, datedHome, datedHomeScore, datedAway, datedAwayScore,
                        "Playoffs", $"{round} Game {ordinal}", ResolveLegacyUrl(sourceUrl, href), SportGrParserVersion,
                        revision, $"sportgr:{startYear}:playoffs:{ordinal:D2}");
                    continue;
                }

                // The 1998-1999 page stores a series in one row and dates in score links.
                if (texts.Length < 2 || !TrySplitLegacyTeams(texts[0], out var firstTeam, out var secondTeam))
                {
                    continue;
                }
                var seriesGame = 0;
                for (var index = 1; index < cells.Count; index++)
                {
                    var scoreText = Clean(cells[index].InnerText);
                    if (scoreText.Contains('*') ||
                        !TryParseLegacyScore(scoreText, out var homeScore, out var awayScore))
                    {
                        continue;
                    }
                    var href = cells[index].SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty) ?? string.Empty;
                    if (!TryParseLegacyHrefDate(href, out var date))
                    {
                        continue;
                    }
                    ordinal++;
                    seriesGame++;
                    var home = seriesGame % 2 == 1 ? firstTeam : secondTeam;
                    var away = seriesGame % 2 == 1 ? secondTeam : firstTeam;
                    AddLegacyGame(games, season, date, home, homeScore, away, awayScore, "Playoffs",
                        $"{round} Game {seriesGame}", ResolveLegacyUrl(sourceUrl, href), SportGrParserVersion,
                        revision, $"sportgr:{startYear}:playoffs:{ordinal:D2}");
                }
            }
        }
        return games;
    }

    private static IReadOnlyList<string> LegacyCells(string tableHtml) =>
        Regex.Matches(tableHtml,
                @"<(?:th|td)\b[^>]*>(.*?)(?=<(?:th|td|tr)\b|</table>|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(match => Clean(Regex.Replace(match.Groups[1].Value, "<[^>]+>", string.Empty)))
            .ToArray();

    private static bool TrySplitLegacyTeams(string value, out string home, out string away)
    {
        var match = Regex.Match(value, @"^\s*(.+?)\s*-\s*(.+?)(?:\s*\([^)]+\))?\s*$");
        home = match.Success ? Clean(match.Groups[1].Value) : string.Empty;
        away = match.Success ? Clean(match.Groups[2].Value) : string.Empty;
        return match.Success && home.Length > 0 && away.Length > 0;
    }

    private static bool TryParseLegacyScore(string value, out short homeScore, out short awayScore)
    {
        var match = Regex.Match(value, @"(?<!\d)(\d{1,3})\s*-+\s*(\d{1,3})(?!\d)");
        homeScore = 0;
        awayScore = 0;
        return short.TryParse(match.Groups[1].Value, out homeScore) &&
               short.TryParse(match.Groups[2].Value, out awayScore) && homeScore != awayScore &&
               !IsAdministrativeScore(homeScore, awayScore);
    }

    private static bool TryParseLegacyDate(string value, int startYear, out DateTime date)
    {
        var match = Regex.Match(value, @"(?<!\d)(\d{1,2})[-/.](\d{1,2})(?:[-/.](\d{2,4}))?(?!\d)");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var day) ||
            !int.TryParse(match.Groups[2].Value, out var month))
        {
            date = default;
            return false;
        }
        var year = month >= 7 ? startYear : startYear + 1;
        if (int.TryParse(match.Groups[3].Value, out var explicitYear))
        {
            year = explicitYear < 50 ? 2000 + explicitYear : explicitYear < 100 ? 1900 + explicitYear : explicitYear;
        }
        return TryDate(year, month, day, out date);
    }

    private static bool TryParseLegacyHrefDate(string href, out DateTime date)
    {
        var match = Regex.Match(href, @"(?<!\d)(\d{2})(\d{2})(\d{2})(?!\d)");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var year) ||
            !int.TryParse(match.Groups[2].Value, out var month) || !int.TryParse(match.Groups[3].Value, out var day))
        {
            date = default;
            return false;
        }
        return TryDate(1900 + year, month, day, out date);
    }

    private static string ResolveLegacyUrl(string sourceUrl, string href) =>
        Uri.TryCreate(new Uri(sourceUrl), href, out var resolved) ? resolved.ToString() : sourceUrl;

    private static void AddLegacyGame(
        ICollection<BasketballProviderGame> games,
        string season,
        DateTime date,
        string rawHome,
        short homeScore,
        string rawAway,
        short awayScore,
        string phase,
        string round,
        string sourceUrl,
        string parserVersion,
        string revision,
        string sourceGameId)
    {
        games.Add(new BasketballProviderGame(
            Source,
            sourceGameId,
            DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Utc),
            "finished",
            $"sportgr:{Slug(rawHome)}",
            CanonicalizeTeamName(rawHome),
            $"sportgr:{Slug(rawAway)}",
            CanonicalizeTeamName(rawAway),
            homeScore,
            awayScore,
            new BasketballProviderGameProvenance(sourceUrl, season, DateTime.UtcNow, parserVersion, revision),
            CompetitionPhase: phase,
            CompetitionRound: round));
    }

    internal static IReadOnlyList<string> ParseEsakeSeries(string html) =>
        Regex.Matches(html, @"new\s+Option\('[^']*',\s*'(\d+)'\)", RegexOptions.IgnoreCase)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                .ToString("00", CultureInfo.InvariantCulture))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    internal static IReadOnlyList<BasketballProviderGame> ParseEsakeRound(
        string html,
        string season,
        int startYear,
        string phase,
        string series,
        string sourceUrl)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var nodes = document.DocumentNode.SelectNodes(
            "//div[contains(concat(' ', normalize-space(@class), ' '), ' esake-program-game ')]")
            ?? Enumerable.Empty<HtmlNode>();
        var games = new List<BasketballProviderGame>();
        var revision = Hash(html);

        foreach (var node in nodes)
        {
            var gameLink = node.SelectSingleNode(".//a[contains(@href, 'idgame=')]");
            var gameIdMatch = Regex.Match(gameLink?.GetAttributeValue("href", string.Empty) ?? string.Empty,
                @"idgame=([0-9A-F]+)", RegexOptions.IgnoreCase);
            var scoreColumns = node.SelectNodes(
                ".//div[contains(concat(' ', normalize-space(@class), ' '), ' esake-program-game-final-score ')]/div");
            if (!gameIdMatch.Success || scoreColumns is null || scoreColumns.Count < 3)
            {
                continue;
            }

            var scoreMatch = Regex.Match(Clean(scoreColumns[1].InnerText), @"^(\d{1,3})\s*-\s*(\d{1,3})$");
            if (!scoreMatch.Success || !short.TryParse(scoreMatch.Groups[1].Value, out var homeScore) ||
                !short.TryParse(scoreMatch.Groups[2].Value, out var awayScore) || homeScore == awayScore ||
                IsAdministrativeScore(homeScore, awayScore))
            {
                continue;
            }

            var rawHome = TeamNameFromScoreCell(scoreColumns[0]);
            var rawAway = TeamNameFromScoreCell(scoreColumns[2]);
            if (rawHome.Length == 0 || rawAway.Length == 0)
            {
                continue;
            }
            var dateText = Clean(node.SelectSingleNode(
                ".//div[contains(concat(' ', normalize-space(@class), ' '), ' esake-program-game-info ')]")?.InnerText ?? string.Empty);
            if (!TryParseEsakeDate(dateText, startYear, out var date))
            {
                continue;
            }

            var homeImage = scoreColumns[0].SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty) ?? string.Empty;
            var awayImage = scoreColumns[2].SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty) ?? string.Empty;
            var homeId = EsakeTeamId(homeImage, rawHome);
            var awayId = EsakeTeamId(awayImage, rawAway);
            var gameHref = gameLink!.GetAttributeValue("href", string.Empty);
            var gameUrl = Uri.TryCreate(new Uri(sourceUrl), gameHref, out var absolute) ? absolute.ToString() : sourceUrl;
            var roundNumber = int.TryParse(series, out var parsedSeries) ? parsedSeries : 0;
            var round = phase == "Regular Season" ? $"Round {roundNumber}" : $"Playoffs Round {roundNumber}";

            games.Add(new BasketballProviderGame(
                Source,
                $"esake:{gameIdMatch.Groups[1].Value.ToUpperInvariant()}",
                DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Utc),
                "finished",
                homeId,
                CanonicalizeTeamName(rawHome),
                awayId,
                CanonicalizeTeamName(rawAway),
                homeScore,
                awayScore,
                new BasketballProviderGameProvenance(gameUrl, season, DateTime.UtcNow, EsakeParserVersion, revision),
                CompetitionPhase: phase,
                CompetitionRound: round));
        }

        return games;
    }

    internal static CupParseResult ParseEokCup(
        string html,
        string season,
        int postId,
        string sourceUrl,
        string revision)
    {
        var startYear = SeasonLabelNormalizer.ParseStartYear(season);
        var state = new CupState(startYear);
        var parsed = new List<CupCandidate>();
        var warnings = new List<string>();
        var document = new HtmlDocument();
        document.LoadHtml(html);

        foreach (var node in document.DocumentNode.Descendants().Where(node =>
                     node.NodeType == HtmlNodeType.Element &&
                     (node.Name == "table" ||
                      ((node.Name == "p" || Regex.IsMatch(node.Name, "^h[1-6]$")) && !node.Ancestors("table").Any()))))
        {
            if (node.Name == "table")
            {
                ParseCupTable(node, state, parsed, warnings);
                continue;
            }

            foreach (var line in LinesFromHtml(node.InnerHtml))
            {
                UpdateCupState(line, state);
                if (TryParseLegacyCupLine(line, state, out var candidate))
                {
                    parsed.Add(candidate);
                }
            }
        }

        var games = new List<BasketballProviderGame>();
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var fallbackOrdinal = 0;
        foreach (var candidate in parsed)
        {
            if (candidate.HomeScore == candidate.AwayScore || IsAdministrativeScore(candidate.HomeScore, candidate.AwayScore))
            {
                warnings.Add($"Skipped administrative or invalid result {candidate.Home} {candidate.HomeScore}-{candidate.AwayScore} {candidate.Away}.");
                continue;
            }
            if (candidate.Date is null)
            {
                warnings.Add($"Skipped Cup game without a parseable date: {candidate.Home} - {candidate.Away}.");
                continue;
            }

            fallbackOrdinal++;
            var number = candidate.GameNumber ?? fallbackOrdinal.ToString(CultureInfo.InvariantCulture);
            var gameDate = NormalizeKnownCupDate(postId, number, candidate.Date.Value);
            var round = NormalizeKnownCupRound(postId, number, candidate.Round);
            var sourceId = $"eok-cup:{postId}:{number}";
            var duplicate = 1;
            while (!sourceIds.Add(sourceId))
            {
                sourceId = $"eok-cup:{postId}:{number}:{++duplicate}";
            }
            var rawHomeKey = NormalizeKey(candidate.Home);
            var rawAwayKey = NormalizeKey(candidate.Away);
            games.Add(new BasketballProviderGame(
                Source,
                sourceId,
                DateTime.SpecifyKind(gameDate.Date.AddHours(12), DateTimeKind.Utc),
                "finished",
                $"eok:{Slug(rawHomeKey)}",
                CanonicalizeTeamName(candidate.Home),
                $"eok:{Slug(rawAwayKey)}",
                CanonicalizeTeamName(candidate.Away),
                candidate.HomeScore,
                candidate.AwayScore,
                new BasketballProviderGameProvenance(sourceUrl, season, DateTime.UtcNow, EokCupParserVersion, revision),
                CompetitionPhase: candidate.Phase,
                CompetitionRound: round));
        }

        if (postId == 1750)
        {
            warnings.Add("EOK game 17 omits the away team; the incomplete source row is intentionally excluded.");
        }

        return new CupParseResult(
            games.OrderBy(game => game.GameDateTimeUtc).ThenBy(game => game.SourceGameId).ToArray(),
            warnings);
    }

    private static void ParseCupTable(HtmlNode table, CupState state, ICollection<CupCandidate> games, ICollection<string> warnings)
    {
        foreach (var row in table.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
        {
            var cells = row.SelectNodes("./th|./td")?.Select(cell => Clean(cell.InnerText)).Where(value => value.Length > 0).ToArray();
            if (cells is null || cells.Length < 3)
            {
                continue;
            }
            var scoreIndex = Array.FindIndex(cells, value => Regex.IsMatch(value, @"^\s*\d{1,3}\s*-\s*\d{1,3}\s*$"));
            if (scoreIndex < 2)
            {
                continue;
            }
            var scoreMatch = Regex.Match(cells[scoreIndex], @"(\d{1,3})\s*-\s*(\d{1,3})");
            if (!short.TryParse(scoreMatch.Groups[1].Value, out var homeScore) ||
                !short.TryParse(scoreMatch.Groups[2].Value, out var awayScore))
            {
                continue;
            }
            var gameNumber = cells.Take(scoreIndex - 1).FirstOrDefault(value => Regex.IsMatch(value, @"^\d+$"));
            var date = cells.Select(value => TryParseCupDate(value, state.StartYear, out var parsedDate) ? parsedDate : (DateTime?)null)
                .FirstOrDefault(value => value.HasValue) ?? state.Date;
            games.Add(new CupCandidate(gameNumber, date, cells[scoreIndex - 2], homeScore,
                cells[scoreIndex - 1], awayScore, state.Phase, state.Round));
        }
    }

    private static bool TryParseLegacyCupLine(string line, CupState state, out CupCandidate candidate)
    {
        var scoreMatch = Regex.Match(line, @"(?<!\d)(\d{1,3})\s*-\s*(\d{1,3})(?!\d)\s*$");
        if (!scoreMatch.Success || !short.TryParse(scoreMatch.Groups[1].Value, out var homeScore) ||
            !short.TryParse(scoreMatch.Groups[2].Value, out var awayScore))
        {
            candidate = default!;
            return false;
        }

        var prefix = line[..scoreMatch.Index].Trim();
        var parts = Regex.Split(prefix, @"\s{2,}").Where(value => value.Length > 0).ToList();
        string? gameNumber = null;
        if (parts.Count > 0 && Regex.IsMatch(parts[0], @"^\d+$"))
        {
            gameNumber = parts[0];
            parts.RemoveAt(0);
        }
        if (parts.Count < 2)
        {
            candidate = default!;
            return false;
        }
        candidate = new CupCandidate(gameNumber, state.Date, parts[^2], homeScore, parts[^1], awayScore,
            state.Phase, state.Round);
        return true;
    }

    private static void UpdateCupState(string line, CupState state)
    {
        var key = NormalizeKey(line);
        if (Regex.IsMatch(key, @"(^|\s)α\s*φαση($|\s)")) state.Phase = "First Phase";
        if (Regex.IsMatch(key, @"(^|\s)β\s*φαση($|\s)")) state.Phase = "Second Phase";
        if (key.Contains("final four", StringComparison.Ordinal)) state.Phase = "Final Four";

        if (key.Contains("μικρος τελικ", StringComparison.Ordinal)) state.Round = "Third Place";
        else if (key.Contains("προημιτελ", StringComparison.Ordinal)) state.Round = "Quarterfinals";
        else if (key.Contains("ημιτελ", StringComparison.Ordinal) || key.Contains("hmitel", StringComparison.Ordinal)) state.Round = "Semifinals";
        else if (key.Contains("τελικ", StringComparison.Ordinal) || key.Contains("teliko", StringComparison.Ordinal)) state.Round = "Final";
        else if (key.Contains("αγωνιστικ", StringComparison.Ordinal))
        {
            var ordinal = Regex.Match(key, @"\d+").Value;
            state.Round = ordinal.Length == 0 ? state.Phase : $"{state.Phase} Round {ordinal}";
        }

        if (TryParseCupDate(line, state.StartYear, out var date))
        {
            state.Date = date;
        }
    }

    private static IReadOnlyList<string> LinesFromHtml(string html)
    {
        var withBreaks = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        var withoutTags = Regex.Replace(withBreaks, "<[^>]+>", string.Empty);
        return HtmlEntity.DeEntitize(withoutTags)
            .Replace('\u00A0', ' ')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
    }

    private static string TeamNameFromScoreCell(HtmlNode cell)
    {
        var span = cell.SelectSingleNode(".//span");
        return string.Join(' ', LinesFromHtml(span?.InnerHtml ?? cell.InnerHtml));
    }

    private static bool TryParseEsakeDate(string value, int startYear, out DateTime date)
    {
        var key = NormalizeKey(value);
        var match = Regex.Match(key, @"(?<!\d)(\d{1,2})\s+([α-ωa-z]+)");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var day))
        {
            date = default;
            return false;
        }
        var monthToken = match.Groups[2].Value;
        var month = monthToken switch
        {
            var token when token.StartsWith("ιαν", StringComparison.Ordinal) => 1,
            var token when token.StartsWith("φεβ", StringComparison.Ordinal) => 2,
            var token when token.StartsWith("μαρ", StringComparison.Ordinal) => 3,
            var token when token.StartsWith("απρ", StringComparison.Ordinal) => 4,
            var token when token.StartsWith("μαι", StringComparison.Ordinal) => 5,
            var token when token.StartsWith("ιουν", StringComparison.Ordinal) => 6,
            var token when token.StartsWith("ιουλ", StringComparison.Ordinal) => 7,
            var token when token.StartsWith("αυγ", StringComparison.Ordinal) => 8,
            var token when token.StartsWith("σεπ", StringComparison.Ordinal) => 9,
            var token when token.StartsWith("οκτ", StringComparison.Ordinal) => 10,
            var token when token.StartsWith("νοε", StringComparison.Ordinal) => 11,
            var token when token.StartsWith("δεκ", StringComparison.Ordinal) => 12,
            _ => 0
        };
        var year = month >= 7 ? startYear : startYear + 1;
        return TryDate(year, month, day, out date);
    }

    private static bool TryParseCupDate(string value, int startYear, out DateTime date)
    {
        var splitMonthRange = Regex.Match(value,
            @"(?<!\d)(\d{1,2})\s*-\s*(\d{1,2})\s*/\s*\d{1,2}\s*-\s*\d{1,2}\s*/\s*(\d{2,4})(?!\d)");
        if (splitMonthRange.Success && int.TryParse(splitMonthRange.Groups[1].Value, out var rangeDay) &&
            int.TryParse(splitMonthRange.Groups[2].Value, out var rangeMonth) &&
            int.TryParse(splitMonthRange.Groups[3].Value, out var rangeYear))
        {
            rangeYear = rangeYear < 100 ? (rangeYear >= 90 ? 1900 : 2000) + rangeYear : rangeYear;
            return TryDate(rangeYear, rangeMonth, rangeDay, out date);
        }

        var match = Regex.Match(value,
            @"(?<!\d)(\d{1,2})(?:\s*-\s*\d{1,2})*\s*/\s*(\d{1,2})(?:\s*/\s*(\d{2,4}))?");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var day) ||
            !int.TryParse(match.Groups[2].Value, out var month))
        {
            date = default;
            return false;
        }
        var year = month >= 7 ? startYear : startYear + 1;
        if (int.TryParse(match.Groups[3].Value, out var explicitYear))
        {
            year = explicitYear < 100 ? 2000 + explicitYear : explicitYear;
            if (explicitYear is >= 90 and < 100) year = 1900 + explicitYear;

            // Some EOK pages carry the previous calendar year on January-June
            // headings even though the surrounding edition and later rounds
            // make the intended season year unambiguous (for example the
            // 1999-2000 quarterfinal heading says January 1999).
            if (month < 7 && year == startYear)
            {
                year = startYear + 1;
            }
            else if (month >= 7 && year == startYear + 1)
            {
                year = startYear;
            }
        }
        return TryDate(year, month, day, out date);
    }

    private static DateTime NormalizeKnownCupDate(int postId, string gameNumber, DateTime parsedDate) =>
        postId == 1750 && gameNumber is "39" or "40"
            ? new DateTime(1997, 4, 12)
            : postId == 1750 && gameNumber is "41" or "42"
                ? new DateTime(1997, 4, 13)
                : parsedDate;

    private static string NormalizeKnownCupRound(int postId, string gameNumber, string parsedRound) =>
        postId == 1750 && gameNumber == "41"
            ? "Third Place"
            : postId == 1750 && gameNumber == "42"
                ? "Final"
                : parsedRound;

    private static bool TryDate(int year, int month, int day, out DateTime date)
    {
        try
        {
            date = new DateTime(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            date = default;
            return false;
        }
    }

    internal static string CanonicalizeTeamName(string value)
    {
        var clean = Clean(value);
        clean = Regex.Replace(clean, @"\b(?:CARNA|AKTOR|BETSSON|COSMORAMA\s+TRAVEL|H\s+HOTELS\s+COLLECTION|AM\.GENETICS|CRETAN\s+KINGS)\b",
            string.Empty, RegexOptions.IgnoreCase).Trim();
        var key = NormalizeKey(clean);
        if (CanonicalTeamNames.TryGetValue(key, out var canonical))
        {
            return canonical;
        }
        var tokens = key.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (tokens.Count > 1 && ClubAffixes.Contains(tokens[0])) tokens.RemoveAt(0);
        while (tokens.Count > 1 && ClubAffixes.Contains(tokens[^1])) tokens.RemoveAt(tokens.Count - 1);
        var stripped = string.Join(' ', tokens);
        if (CanonicalTeamNames.TryGetValue(stripped, out canonical))
        {
            return canonical;
        }
        var transliterated = TransliterateGreek(stripped);
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(transliterated.ToLowerInvariant());
    }

    private static string TransliterateGreek(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value)
        {
            builder.Append(character switch
            {
                'α' => "a", 'β' => "v", 'γ' => "g", 'δ' => "d", 'ε' => "e", 'ζ' => "z",
                'η' => "i", 'θ' => "th", 'ι' => "i", 'κ' => "k", 'λ' => "l", 'μ' => "m",
                'ν' => "n", 'ξ' => "x", 'ο' => "o", 'π' => "p", 'ρ' => "r", 'σ' or 'ς' => "s",
                'τ' => "t", 'υ' => "y", 'φ' => "f", 'χ' => "ch", 'ψ' => "ps", 'ω' => "o",
                _ => character.ToString()
            });
        }
        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    private string EsakeResultsUrl(string championshipId, string phaseId, string series) =>
        new Uri(new Uri(options.Value.EsakeBaseUrl),
            $"el/action/EsakeResults?idchampionship={championshipId}&idseason={phaseId}&mode=1&series={series}").ToString();

    private async Task<string> FetchStringAsync(
        string url,
        BackfillExecutionContext context,
        CancellationToken cancellationToken,
        string? referer = null)
    {
        using var response = await BackfillHttpRetryPolicy.SendAsync(
            async retryCancellationToken =>
            {
                if (!context.CanUseRequest())
                {
                    throw new InvalidOperationException("Greek official source request budget reached.");
                }
                context.ConsumeRequest();
                if (options.Value.MinRequestIntervalMilliseconds > 0)
                {
                    await Task.Delay(options.Value.MinRequestIntervalMilliseconds, retryCancellationToken);
                }
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(options.Value.UserAgent);
                if (!string.IsNullOrWhiteSpace(referer)) request.Headers.Referrer = new Uri(referer);
                return await httpClient.SendAsync(request, retryCancellationToken);
            },
            options.Value.MaxTransientRetries,
            options.Value.RetryBaseDelayMilliseconds,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<string> FetchLegacyGreekStringAsync(
        string url,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        using var response = await BackfillHttpRetryPolicy.SendAsync(
            async retryCancellationToken =>
            {
                if (!context.CanUseRequest())
                {
                    throw new InvalidOperationException("Greek historical source request budget reached.");
                }
                context.ConsumeRequest();
                if (options.Value.MinRequestIntervalMilliseconds > 0)
                {
                    await Task.Delay(options.Value.MinRequestIntervalMilliseconds, retryCancellationToken);
                }
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(options.Value.UserAgent);
                return await httpClient.SendAsync(request, retryCancellationToken);
            },
            options.Value.MaxTransientRetries,
            options.Value.RetryBaseDelayMilliseconds,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1253).GetString(bytes);
    }

    private static string EsakeTeamId(string imageUrl, string teamName)
    {
        var match = Regex.Match(imageUrl, @"/esaketeam/([^/]+)/", RegexOptions.IgnoreCase);
        return match.Success ? $"esake:{match.Groups[1].Value}" : $"esake-name:{Slug(NormalizeKey(teamName))}";
    }

    private static bool IsAdministrativeScore(short homeScore, short awayScore) =>
        (homeScore == 20 && awayScore == 0) || (homeScore == 0 && awayScore == 20);

    private static string Clean(string value) =>
        Regex.Replace(HtmlEntity.DeEntitize(value).Replace('\u00A0', ' '), @"\s+", " ").Trim();

    private static string NormalizeKey(string value)
    {
        var normalized = Clean(value).Normalize(NormalizationForm.FormD);
        var characters = normalized.Where(character =>
                CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => char.IsLetterOrDigit(character)
                ? char.ToLowerInvariant(character) is '\u03C2' ? '\u03C3' : char.ToLowerInvariant(character)
                : ' ')
            .ToArray();
        return string.Join(' ', new string(characters).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Slug(string value) =>
        string.Join('-', NormalizeKey(value).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal sealed record CupParseResult(
        IReadOnlyList<BasketballProviderGame> Games,
        IReadOnlyList<string> Warnings);

    private sealed record CupCandidate(
        string? GameNumber,
        DateTime? Date,
        string Home,
        short HomeScore,
        string Away,
        short AwayScore,
        string Phase,
        string Round);

    private sealed class CupState(int startYear)
    {
        public int StartYear { get; } = startYear;
        public DateTime? Date { get; set; }
        public string Phase { get; set; } = "Cup";
        public string Round { get; set; } = "Cup Round";
    }
}
