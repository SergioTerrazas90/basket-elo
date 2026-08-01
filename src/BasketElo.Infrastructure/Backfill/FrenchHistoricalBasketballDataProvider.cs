using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Historical French top-flight and Coupe de France results. BasketArchives is
/// used where it publishes complete season pages, TheSports bridges the final
/// seven pre-API-Sports seasons, and French Wikipedia supplies the four cup
/// editions for which complete game-level articles exist.
/// </summary>
public sealed partial class FrenchHistoricalBasketballDataProvider(
    HttpClient httpClient,
    IOptions<FrenchHistoricalOptions> options) : IBasketballDataProvider
{
    public const string Source = "french-historical";
    public const string BasketArchivesParserVersion = "basketarchives-fr-v1";
    public const string LEquipeParserVersion = "lequipe-fr-v1";
    public const string TheSportsParserVersion = "the-sports-fr-v1";
    public const string FrenchCupParserVersion = "fr-wikipedia-cup-v1";
    public const string GallicaParserVersion = "ffbb-gallica-alto-v1";

    private static readonly IReadOnlyDictionary<int, GallicaSeasonSource> GallicaSeasons =
        new Dictionary<int, GallicaSeasonSource>
        {
            [1982] = new("bpt6k6559330r", 182),
            [1983] = new("bpt6k6560640n", 182),
            [1984] = new("bpt6k6558623b", 182),
            [1985] = new("bpt6k6559621t", 432),
            [1986] = new("bpt6k65592201", 427)
        };

    private static readonly IReadOnlyList<GallicaTeamAlias> GallicaTeamAliases =
    [
        new(@"(?:S\.?\s*C\.?\s*M\.?\s*)?Le\s+Mans", "Le Mans"),
        new(@"(?:E\.?\s*[BS]\.?\s*)?(?:Pau[\s-]*)?Orthez", "Pau-Orthez"),
        new(@"(?:A\.?\s*S\.?\s*)?Monaco", "Monaco"),
        new(@"(?:C\.?\s*S\.?\s*P\.?\s*)?Limoges(?:\s+C\.?\s*S\.?\s*P\.?)?", "Limoges"),
        new(@"(?:A\.?\s*S\.?\s*V\.?\s*E\.?\s*L\.?|A\.?\s*V\.?\s*S\.?\s*E\.?\s*L\.?|Villeurbanne|AS\s+Villeurbanne)", "Lyon-Villeurbanne"),
        new(@"(?:O[l1i0]\.?|0[1l]?\.?)?\s*Antibes", "Antibes"),
        new(@"(?:E\.?\s*S\.?\s*M\.?\s*)?Challans(?:\s+B[CV]{2})?", "Challans"),
        new(@"Caen(?:\s+B\.?\s*C\.?)?", "Caen"),
        new(@"(?:J\.?\s*A\.?\s*)?Vichy", "Vichy"),
        new(@"(?:E\.?\s*S\.?\s*)?Avignon", "Avignon"),
        new(@"Tours(?:\s+B\.?\s*C\.?)?", "Tours"),
        new(@"Mulhouse(?:\s+B\.?\s*C\.?)?", "Mulhouse"),
        new(@"Stade\s+Fran[cç]ais", "Stade Français"),
        new(@"(?:C\.?\s*R\.?\s*O\.?\s*)?Lyon", "Lyon"),
        new(@"Reims(?:\s+C\.?\s*B\.?)?", "Reims"),
        new(@"(?:R\.?\s*C\.?\s*(?:F\.?\s*)?Paris|Racing(?:\s+Paris)?)", "Racing Paris"),
        new(@"Nantes(?:\s+B\.?\s*C\.?)?", "Nantes"),
        new(@"(?:C\.?\s*A\.?\s*)?S(?:ain)?t[\s.-]*Etienne", "Saint-Étienne"),
        new(@"(?:S\.?\s*L\.?\s*U\.?\s*C\.?\s*)?Nancy", "Nancy"),
        new(@"(?:C\.?\s*E\.?\s*P\.?\s*)?Lorient", "Lorient"),
        new(@"(?:Stade\s+Clermontois|St\.?\s*Clermont|A\.?\s*S\.?\s*Clermont)", "Clermont"),
        new(@"(?:J\.?\s*A\.?|J\.?\s*D\.?\s*A\.?)\s+Dijon", "Dijon"),
        new(@"(?:Etoile|E\.?)\s+Vo[il]ron", "Voiron"),
        new(@"(?:Avenir|A\.?)\s+Rennes", "Rennes"),
        new(@"Grenoble(?:\s+B(?:C|CI)?)?", "Grenoble"),
        new(@"Cholet(?:\s+B(?:asket)?)?", "Cholet"),
        new(@"(?:B\.?\s*C\.?\s*)?Nice(?:\s+O[l1i]\.?)?", "Nice")
    ];

    private static readonly IReadOnlyDictionary<int, (string Regular, string Playoffs)> TheSportsPages =
        new Dictionary<int, (string, string)>
        {
            [2001] = ("basketball-pro-a-regular-season-2001-2002-results-eprd407.html", "basketball-pro-a-playoffs-2001-2002-results-eprd606.html"),
            [2002] = ("basketball-pro-a-regular-season-2002-2003-results-eprd445.html", "basketball-pro-a-playoffs-2002-2003-results-eprd607.html"),
            [2003] = ("basketball-pro-a-regular-season-2003-2004-results-eprd495.html", "basketball-pro-a-playoffs-2003-2004-results-eprd608.html"),
            [2004] = ("basketball-pro-a-regular-season-2004-2005-results-eprd683.html", "basketball-pro-a-playoffs-2004-2005-results-eprd1389.html"),
            [2005] = ("basketball-pro-a-regular-season-2005-2006-results-eprd2838.html", "basketball-pro-a-playoffs-2005-2006-results-eprd2839.html"),
            [2006] = ("basketball-pro-a-regular-season-2006-2007-results-eprd4566.html", "basketball-pro-a-playoffs-2006-2007-results-eprd4567.html"),
            [2007] = ("basketball-pro-a-regular-season-2007-2008-results-eprd6298.html", "basketball-pro-a-playoffs-2007-2008-results-eprd6299.html")
        };

    private static readonly IReadOnlyDictionary<string, string> CanonicalTeamNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ada blois"] = "Ada Blois",
            ["adecco asvel"] = "Lyon-Villeurbanne",
            ["aix maurienne sb"] = "Aix Maurienne",
            ["alm evreux"] = "Evreux",
            ["alm evreux basket"] = "Evreux",
            ["angers bc 49"] = "Angers",
            ["antibes"] = "Antibes",
            ["antibes olympique juan les pins"] = "Antibes",
            ["as golbey epinal"] = "Vosges",
            ["asvel"] = "Lyon-Villeurbanne",
            ["asvel lyon villeurbanne"] = "Lyon-Villeurbanne",
            ["besancon bcd"] = "Besancon",
            ["besancon basket comte doubs"] = "Besancon",
            ["bcm gravelines"] = "Gravelines-Dunkerque",
            ["bcm gravelines dunkerque"] = "Gravelines-Dunkerque",
            ["blois"] = "Ada Blois",
            ["bourg"] = "JL Bourg",
            ["bourg en bresse"] = "JL Bourg",
            ["boulazac basket dordogne"] = "Boulazac",
            ["boulogne sur mer"] = "Boulogne sur Mer",
            ["jl bourg en bresse"] = "JL Bourg",
            ["jl bourg en bresse basket"] = "JL Bourg",
            ["chalon"] = "Chalon/Saone",
            ["chalon sur saone"] = "Chalon/Saone",
            ["chalons en champagne"] = "Chalons-Reims",
            ["cholet basket"] = "Cholet",
            ["chorale de roanne basket"] = "Roanne",
            ["chorale roanne basket"] = "Roanne",
            ["clermont ferrand"] = "Clermont",
            ["elan bearnais pau lacq orthez"] = "Pau-Orthez",
            ["elan chalon sur saone"] = "Chalon/Saone",
            ["elan chalon"] = "Chalon/Saone",
            ["elan sportif chalonnais"] = "Chalon/Saone",
            ["entente orleanaise 45"] = "Orleans",
            ["entente orleanaise loiret"] = "Orleans",
            ["entente orleans 45"] = "Orleans",
            ["etendard de brest"] = "Brest",
            ["etendard de brest basket"] = "Brest",
            ["essm le portel"] = "Le Portel",
            ["feurs ef"] = "Feurs",
            ["feurs forez basket"] = "Feurs",
            ["fos"] = "Fos-sur-Mer",
            ["fos ouest provence basket"] = "Fos-sur-Mer",
            ["get vosges"] = "Vosges",
            ["golbey epinal"] = "Vosges",
            ["gravelines"] = "Gravelines-Dunkerque",
            ["gravelines dunkerque"] = "Gravelines-Dunkerque",
            ["hyeres toulon var basket"] = "Hyeres-Toulon",
            ["hyeres toulon vb"] = "Hyeres-Toulon",
            ["hermine de nantes"] = "Nantes",
            ["l hermine nantes atlantique"] = "Nantes",
            ["ja de vichy"] = "Vichy",
            ["ja vichy"] = "Vichy",
            ["ja vichy clermont"] = "Vichy",
            ["jda dijon"] = "Dijon",
            ["jda dijon bourgogne"] = "Dijon",
            ["jda dijon basket"] = "Dijon",
            ["jl bourg basket"] = "JL Bourg",
            ["jsa bordeaux"] = "Bordeaux",
            ["jsa bordeaux basket"] = "Bordeaux",
            ["jsf nanterre"] = "Nanterre",
            ["la rochelle rupella 17"] = "La Rochelle",
            ["le mans sarthe basket"] = "Le Mans",
            ["le mans sb"] = "Le Mans",
            ["levallois"] = "Boulogne-Levallois",
            ["levallois sc"] = "Boulogne-Levallois",
            ["levallois metropolitans bc 92"] = "Boulogne-Levallois",
            ["limoges csp"] = "Limoges",
            ["limoges csp elite"] = "Limoges",
            ["lille metropole"] = "Lille",
            ["lille metropole bc"] = "Lille",
            ["montpellier pb"] = "Montpellier",
            ["nancy"] = "Nancy",
            ["nanterre 92"] = "Nanterre",
            ["olympique d antibes"] = "Antibes",
            ["orleans loiret basket"] = "Orleans",
            ["orthez"] = "Pau-Orthez",
            ["paris basket racing"] = "Paris Basket Racing",
            ["paris br"] = "Paris Basket Racing",
            ["paris levallois"] = "Boulogne-Levallois",
            ["psg racing"] = "Paris Basket Racing",
            ["pau lacq orthez"] = "Pau-Orthez",
            ["pau orthez"] = "Pau-Orthez",
            ["poitiers basket 86"] = "Poitiers",
            ["reims champagne basket"] = "Reims",
            ["rouen metropole basket"] = "Rouen",
            ["rupella basket 17"] = "La Rochelle",
            ["saint chamond basket"] = "St. Chamond",
            ["saint etienne"] = "Saint-Étienne",
            ["saint etienne basket"] = "Saint-Étienne",
            ["saint quentin"] = "Saint Quentin",
            ["saint quentin bb"] = "Saint Quentin",
            ["saint vallier basket drome"] = "Saint Vallier",
            ["saint vallier bd"] = "Saint Vallier",
            ["sig strasbourg"] = "Strasbourg",
            ["sluc nancy"] = "Nancy",
            ["sluc nancy basket"] = "Nancy",
            ["stade clermontois"] = "Clermont",
            ["stade clermontois basket auvergne"] = "Clermont",
            ["stb le havre"] = "Le Havre",
            ["stade de vanves basket"] = "Stade de Vanves",
            ["strasbourg ig basket"] = "Strasbourg",
            ["spo rouen"] = "Rouen",
            ["spo rouen bb"] = "Rouen",
            ["tours joue"] = "Tours",
            ["toulouse spacer s"] = "Toulouse",
            ["ujap quimper"] = "Quimper",
            ["usa lievin basket"] = "Liévin",
            ["usa toulouges"] = "Toulouges",
            ["vallauris"] = "Juan Vallauris",
            ["vendee challans"] = "Challans"
        };

    private static readonly IReadOnlyDictionary<string, int> FrenchMonths =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["janvier"] = 1, ["fevrier"] = 2, ["mars"] = 3, ["avril"] = 4,
            ["mai"] = 5, ["juin"] = 6, ["juillet"] = 7, ["aout"] = 8,
            ["septembre"] = 9, ["octobre"] = 10, ["novembre"] = 11, ["decembre"] = 12
        };

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(country, "France", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        BasketballProviderLeague? league = leagueName.ToLowerInvariant() switch
        {
            "lnb" => new(Source, "FR_TOP_FLIGHT", "LNB", "FR", "start_year"),
            "french cup" => new(Source, "COUPE_DE_FRANCE", "French Cup", "FR", "start_year"),
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
        var (startYear, endYear) = ParseSeason(season);
        return league.SourceLeagueId switch
        {
            "FR_TOP_FLIGHT" when startYear is 1981 or >= 1998 and <= 2000 =>
                await GetBasketArchivesLeagueAsync(season, endYear, context, cancellationToken),
            "FR_TOP_FLIGHT" when startYear is >= 1982 and <= 1986 =>
                await GetGallicaLeagueAsync(season, startYear, endYear, context, cancellationToken),
            "FR_TOP_FLIGHT" when startYear is >= 1987 and <= 1997 =>
                await GetLEquipeLeagueAsync(season, startYear, endYear, context, cancellationToken),
            "FR_TOP_FLIGHT" when startYear is >= 2001 and <= 2007 =>
                await GetTheSportsLeagueAsync(season, startYear, context, cancellationToken),
            "COUPE_DE_FRANCE" when startYear is >= 2004 and <= 2007 =>
                await GetFrenchCupAsync(season, startYear, endYear, context, cancellationToken),
            "FR_TOP_FLIGHT" => throw new ArgumentException(
                "French historical league coverage supports 1981-1982 through 2007-2008.", nameof(season)),
            "COUPE_DE_FRANCE" => throw new ArgumentException(
                "Complete historical French Cup articles support 2004-2005 through 2007-2008.", nameof(season)),
            _ => throw new InvalidOperationException("French historical provider only supports France: LNB and French Cup.")
        };
    }

    private async Task<(IReadOnlyCollection<BasketballProviderGame>, bool, IReadOnlyCollection<string>)> GetBasketArchivesLeagueAsync(
        string season,
        int endYear,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var relativeUrl = $"{endYear}/resultats{endYear}.htm";
        var url = new Uri(new Uri(options.Value.BasketArchivesBaseUrl), relativeUrl).ToString();
        var bytes = await FetchBytesAsync(url, context, cancellationToken);
        var html = Encoding.Latin1.GetString(bytes);
        var games = ParseBasketArchivesGames(html, season, url, endYear);
        var warnings = new List<string>
        {
            "BasketArchives publishes no tip-off times; imported times are 12:00 UTC."
        };
        if (games.Count == 0)
        {
            warnings.Add("The BasketArchives page contained no parseable games.");
        }
        return (games, false, warnings);
    }

    internal static IReadOnlyList<BasketballProviderGame> ParseBasketArchivesGames(
        string html,
        string season,
        string sourceUrl,
        int endYear)
    {
        var games = new List<BasketballProviderGame>();
        var regularRoundOrdinal = 0;
        var sourceGameOrdinal = 0;
        foreach (Match block in BasketArchivesBlockRegex().Matches(html))
        {
            var heading = CleanMarkup(block.Groups[1].Value);
            var phase = heading.Contains("JOURNEE", StringComparison.OrdinalIgnoreCase)
                ? "Regular Season"
                : heading.Contains("final", StringComparison.OrdinalIgnoreCase)
                    ? "Playoffs"
                    : null;
            if (phase is null)
            {
                continue;
            }

            var round = phase == "Regular Season"
                ? $"Round {++regularRoundOrdinal}"
                : NormalizeRound(heading);
            var dateMatch = BasketArchivesDateRegex().Match(block.Groups[2].Value);
            if (!dateMatch.Success || !TryParseFrenchDate(CleanMarkup(dateMatch.Groups[1].Value), endYear, out var date))
            {
                continue;
            }
            if (date.Year < endYear - 1 || date.Year > endYear)
            {
                date = new DateTime(endYear, date.Month, date.Day);
            }

            foreach (var row in Regex.Split(block.Groups[2].Value, @"<TR[^>]*>", RegexOptions.IgnoreCase).Skip(1))
            {
                var cells = Regex.Split(row, @"<TD[^>]*>", RegexOptions.IgnoreCase)
                    .Skip(1)
                    .Select(CleanMarkup)
                    .ToList();
                if (cells.Count < 4 ||
                    !short.TryParse(cells[1], out var homeScore) ||
                    !short.TryParse(cells[2], out var awayScore))
                {
                    continue;
                }

                var homeName = CanonicalizeTeamName(cells[0]);
                var awayName = CanonicalizeTeamName(cells[3]);
                if (homeName.Length == 0 || awayName.Length == 0)
                {
                    continue;
                }

                sourceGameOrdinal++;
                games.Add(BuildGame(
                    $"ba:{season}:{sourceGameOrdinal}",
                    date,
                    homeName,
                    Slug(homeName),
                    homeScore,
                    awayName,
                    Slug(awayName),
                    awayScore,
                    sourceUrl,
                    season,
                    BasketArchivesParserVersion,
                    Hash(html),
                    phase,
                    round));
            }
        }
        return games;
    }

    private async Task<(IReadOnlyCollection<BasketballProviderGame>, bool, IReadOnlyCollection<string>)> GetGallicaLeagueAsync(
        string season,
        int startYear,
        int endYear,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var source = GallicaSeasons[startYear];
        var searchUrl = new Uri(new Uri(options.Value.GallicaBaseUrl),
            $"services/ContentSearch?ark={source.Ark}&query=LIMOGES").ToString();
        var searchXml = await FetchGallicaStringAsync(searchUrl, context, cancellationToken);
        var pages = ParseGallicaSearchPages(searchXml);
        if (pages.Count == 0)
        {
            throw new InvalidOperationException($"Gallica returned no searchable FFBB magazine pages for {season}.");
        }

        var games = new List<BasketballProviderGame>();
        foreach (var page in pages)
        {
            var altoUrl = new Uri(new Uri(options.Value.GallicaBaseUrl),
                $"RequestDigitalElement?O={source.Ark}&E=ALTO&Deb={page}").ToString();
            var alto = await FetchGallicaStringAsync(altoUrl, context, cancellationToken, searchUrl);
            var sourceUrl = new Uri(new Uri(options.Value.GallicaBaseUrl),
                $"ark:/12148/{source.Ark}/f{page}.item").ToString();
            games.AddRange(ParseGallicaAltoPage(alto, season, endYear, page, sourceUrl));
        }

        var distinctGames = games
            .DistinctBy(game => $"{game.GameDateTimeUtc:yyyyMMdd}|{game.SourceHomeTeamId}|{game.HomeScore}|{game.SourceAwayTeamId}|{game.AwayScore}")
            .OrderBy(game => game.GameDateTimeUtc)
            .ThenBy(game => game.SourceGameId, StringComparer.Ordinal)
            .ToList();
        if (distinctGames.Count != source.ExpectedGames)
        {
            throw new InvalidOperationException(
                $"Gallica FFBB completeness check failed for {season}: parsed {distinctGames.Count} games, expected {source.ExpectedGames}. Nothing was imported.");
        }

        string[] warnings =
        [
            "Official FFBB magazine OCR supplied the historical results; score tables were constrained to senior Nationale Masculine 1 sections.",
            "FFBB magazines do not supply reliable tip-off times; imported times are 12:00 UTC.",
            "The 1986-1987 playoff summary omits exact dates; deterministic round-order dates preserve the playoff sequence."
        ];
        return (distinctGames, false, warnings);
    }

    internal static IReadOnlyList<int> ParseGallicaSearchPages(string xml)
    {
        var document = XDocument.Parse(xml);
        return document.Descendants()
            .Where(element => element.Name.LocalName == "p_id")
            .Select(element => Regex.Match(element.Value, @"\d+").Value)
            .Where(value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .Select(value => int.Parse(value, CultureInfo.InvariantCulture))
            .Distinct()
            .OrderBy(page => page)
            .ToList();
    }

    internal static IReadOnlyList<BasketballProviderGame> ParseGallicaAltoPage(
        string altoXml,
        string season,
        int endYear,
        int page,
        string sourceUrl)
    {
        var document = XDocument.Parse(altoXml);
        var lines = document.Descendants()
            .Where(element => element.Name.LocalName == "TextLine")
            .Select(element => string.Join(' ', element.Descendants()
                .Where(word => word.Name.LocalName == "String")
                .Select(word => (string?)word.Attribute("CONTENT") ?? string.Empty)))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        var games = new List<BasketballProviderGame>();
        var revision = Hash(altoXml);
        var active = false;
        var youth = false;
        var phase = "Regular Season";
        var division = string.Empty;
        var round = string.Empty;
        var pool = string.Empty;
        DateTime? date = null;
        var playoffOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = WebUtility.HtmlDecode(lines[lineIndex]);
            var key = NormalizeKey(line);

            if (TryParseFrenchDate(line, endYear, out var parsedDate))
            {
                date = parsedDate;
                youth = false;
                continue;
            }
            if (key.StartsWith("espoirs", StringComparison.Ordinal))
            {
                active = false;
                youth = true;
                continue;
            }
            if (Regex.IsMatch(key, @"(?:^|\s)(?:nationale\s+)?masculine\s+1(?:\s+[ab])?(?:\s|$)") ||
                Regex.IsMatch(key, @"^nm\s*1$") )
            {
                active = !youth;
                division = key.Contains("1 b", StringComparison.Ordinal) ? "N1B" :
                    key.Contains("1 a", StringComparison.Ordinal) ? "N1A" : "N1";
                pool = string.Empty;
                continue;
            }
            if (key.Contains("resultats des play off", StringComparison.Ordinal))
            {
                active = !youth;
                phase = "Playoffs";
                division = "N1";
                pool = string.Empty;
                continue;
            }
            if (key.Contains("qualification korac", StringComparison.Ordinal) ||
                (key.Contains("masculine", StringComparison.Ordinal) &&
                 !Regex.IsMatch(key, @"(?:^|\s)1(?:\s+[ab])?(?:\s|$)")))
            {
                active = false;
                continue;
            }
            if (!active)
            {
                continue;
            }

            var playoffRound = NormalizeGallicaPlayoffRound(line);
            if (playoffRound.Length > 0)
            {
                phase = "Playoffs";
                round = playoffRound;
                pool = string.Empty;
                continue;
            }
            if (key.Contains("phase", StringComparison.Ordinal))
            {
                phase = "Regular Season";
                continue;
            }
            if (key.StartsWith("poule ", StringComparison.Ordinal))
            {
                pool = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key);
                continue;
            }
            if (Regex.IsMatch(key, @"(?:^|\s)tour(?:\s|$)") || key.Contains("journee", StringComparison.Ordinal))
            {
                phase = "Regular Season";
                round = NormalizeGallicaRound(line);
                pool = string.Empty;
                continue;
            }
            if (round.Length == 0 || !TryParseGallicaGameLine(line, out var parsedGame))
            {
                continue;
            }

            var gameDate = date;
            if (gameDate is null && phase == "Playoffs")
            {
                playoffOrdinals.TryGetValue(round, out var ordinal);
                gameDate = GallicaPlayoffFallbackDate(round, endYear, ordinal);
                playoffOrdinals[round] = ordinal + 1;
            }
            if (gameDate is null)
            {
                continue;
            }

            var competitionRound = string.Join(" / ", new[] { round, division, pool }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            games.Add(BuildGame(
                $"gallica:{season}:{page}:{lineIndex}",
                gameDate.Value,
                parsedGame.Home,
                Slug(parsedGame.Home),
                parsedGame.HomeScore,
                parsedGame.Away,
                Slug(parsedGame.Away),
                parsedGame.AwayScore,
                sourceUrl,
                season,
                GallicaParserVersion,
                revision,
                phase,
                competitionRound));
        }
        return games;
    }

    private static bool TryParseGallicaGameLine(string line, out GallicaGame game)
    {
        var matches = MatchGallicaTeams(line);
        if (matches.Count < 2)
        {
            game = default!;
            return false;
        }

        var homeMatch = matches[0];
        var awayMatch = matches.FirstOrDefault(match => match.Index >= homeMatch.Index + homeMatch.Length);
        if (awayMatch is null || matches.Any(match =>
                match.Index >= awayMatch.Index + awayMatch.Length && match.Canonical != awayMatch.Canonical))
        {
            game = default!;
            return false;
        }

        var between = line.Substring(homeMatch.Index + homeMatch.Length,
            awayMatch.Index - homeMatch.Index - homeMatch.Length);
        var after = line[(awayMatch.Index + awayMatch.Length)..];
        var homeScoreMatch = Regex.Match(between, @"(?<!\d)(\d{2,3})(?!\d)");
        var awayScoreMatch = Regex.Match(after, @"(?<!\d)(\d{2,3})(?!\d)");
        if (homeScoreMatch.Success && awayScoreMatch.Success &&
            short.TryParse(homeScoreMatch.Groups[1].Value, out var homeScore) &&
            short.TryParse(awayScoreMatch.Groups[1].Value, out var awayScore))
        {
            game = new(homeMatch.Canonical, homeScore, awayMatch.Canonical, awayScore);
            return true;
        }

        var winnerScore = Regex.Match(after, @"(?<!\d)(\d{2,3})\s*[-–]\s*(\d{2,3})(?!\d)");
        if (!winnerScore.Success ||
            !Regex.IsMatch(between, @"\b(?:b(?:at)?|et)\b", RegexOptions.IgnoreCase) ||
            !short.TryParse(winnerScore.Groups[1].Value, out var firstScore) ||
            !short.TryParse(winnerScore.Groups[2].Value, out var secondScore))
        {
            game = default!;
            return false;
        }

        var firstIsHome = IsGallicaHomeMarked(line, homeMatch);
        var secondIsHome = IsGallicaHomeMarked(line, awayMatch);
        if (secondIsHome && !firstIsHome)
        {
            game = new(awayMatch.Canonical, secondScore, homeMatch.Canonical, firstScore);
        }
        else
        {
            game = new(homeMatch.Canonical, firstScore, awayMatch.Canonical, secondScore);
        }
        return true;
    }

    private static List<GallicaTeamMatch> MatchGallicaTeams(string line) =>
        GallicaTeamAliases
            .SelectMany(alias => Regex.Matches(line, alias.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Cast<Match>()
                .Select(match => new GallicaTeamMatch(match.Index, match.Length, alias.Canonical)))
            .OrderBy(match => match.Index)
            .ThenByDescending(match => match.Length)
            .ToList();

    private static bool IsGallicaHomeMarked(string line, GallicaTeamMatch match)
    {
        var before = line[..match.Index].TrimEnd();
        var after = line[(match.Index + match.Length)..].TrimStart();
        return before.EndsWith('*') || before.EndsWith('\'') || before.EndsWith('"') ||
            after.StartsWith('*') || after.StartsWith('\'') || after.StartsWith('"');
    }

    private static string NormalizeGallicaRound(string value)
    {
        var key = NormalizeKey(value);
        var number = Regex.Match(key, @"\d+").Value;
        var leg = key.Contains("retour", StringComparison.Ordinal) ? "Return" :
            key.Contains("aller", StringComparison.Ordinal) ? "First Leg" : string.Empty;
        return string.Join(" - ", new[] { number.Length > 0 ? $"Round {number}" : "Round", leg }
            .Where(item => item.Length > 0));
    }

    private static string NormalizeGallicaPlayoffRound(string value)
    {
        if (Regex.IsMatch(value, @"1\s*/\s*8", RegexOptions.IgnoreCase)) return "Round of 16";
        if (Regex.IsMatch(value, @"1\s*/\s*4", RegexOptions.IgnoreCase)) return "Quarterfinals";
        if (Regex.IsMatch(value, @"1\s*/\s*2", RegexOptions.IgnoreCase)) return "Semifinals";
        var key = NormalizeKey(value);
        if (key is "finale" or "final") return "Final";
        if (key.Contains("demi finale", StringComparison.Ordinal)) return "Semifinals";
        return string.Empty;
    }

    private static DateTime GallicaPlayoffFallbackDate(string round, int endYear, int ordinal)
    {
        var (month, day, legSize) = round switch
        {
            "Round of 16" => (3, 21, 2),
            "Quarterfinals" => (4, 4, 2),
            "Semifinals" => (4, 18, 2),
            "Final" => (5, 2, 1),
            _ => (3, 1, 1)
        };
        return new DateTime(endYear, month, day).AddDays(7 * (ordinal % legSize));
    }

    private async Task<(IReadOnlyCollection<BasketballProviderGame>, bool, IReadOnlyCollection<string>)> GetLEquipeLeagueAsync(
        string season,
        int startYear,
        int endYear,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var relativeUrl = $"Basket/pro-a/saison-{season}/page-calendrier-resultats";
        var landingUrl = new Uri(new Uri(options.Value.LEquipeBaseUrl), relativeUrl).ToString();
        var landing = await FetchStringAsync(landingUrl, context, cancellationToken);
        var document = new HtmlDocument();
        document.LoadHtml(landing);
        var stages = (document.DocumentNode.SelectNodes("//select//option[contains(@value,'page-calendrier-resultats/')]") ?? Enumerable.Empty<HtmlNode>())
            .Select(node => new
            {
                Url = new Uri(new Uri(options.Value.LEquipeBaseUrl), node.GetAttributeValue("value", string.Empty)).ToString(),
                Round = CleanHtml(node.InnerText)
            })
            .Where(item => item.Round.Length > 0)
            .DistinctBy(item => item.Url)
            .ToList();

        var games = new List<BasketballProviderGame>();
        foreach (var stage in stages)
        {
            var html = await FetchStringAsync(stage.Url, context, cancellationToken, landingUrl);
            games.AddRange(ParseLEquipeStage(html, season, startYear, endYear, stage.Round, stage.Url));
        }

        var warnings = new List<string>
        {
            "L'Equipe supplies historical local dates without reliable tip-off times; imported times are 12:00 UTC."
        };
        if (stages.Count == 0)
        {
            warnings.Add($"No L'Equipe round links were found at {landingUrl}.");
        }
        if (games.Count == 0)
        {
            warnings.Add("L'Equipe pages contained no parseable games.");
        }
        return (games, false, warnings);
    }

    internal static IReadOnlyList<BasketballProviderGame> ParseLEquipeStage(
        string html,
        string season,
        int startYear,
        int endYear,
        string round,
        string sourceUrl)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var games = new List<BasketballProviderGame>();
        foreach (var dateNode in document.DocumentNode.SelectNodes("//div[contains(@class,'caption--small')]") ?? Enumerable.Empty<HtmlNode>())
        {
            if (!TryParseLEquipeDate(CleanHtml(dateNode.InnerText), startYear, endYear, out var date))
            {
                continue;
            }
            var grid = dateNode.SelectSingleNode("following-sibling::div[contains(@class,'grid')][1]");
            if (grid is null)
            {
                continue;
            }
            foreach (var match in grid.SelectNodes(".//div[contains(concat(' ',normalize-space(@class),' '),' TeamScore__top ')]") ?? Enumerable.Empty<HtmlNode>())
            {
                var game = ParseLEquipeMatch(match, date, season, round, sourceUrl, html);
                if (game is not null) games.Add(game);
            }
        }
        foreach (var childEvent in document.DocumentNode.SelectNodes("//div[contains(concat(' ',normalize-space(@class),' '),' CalendarResults__childEvent ')]") ?? Enumerable.Empty<HtmlNode>())
        {
            var dateNode = childEvent.SelectSingleNode("./span[contains(@class,'CalendarResults__childEventDate')]");
            var match = childEvent.SelectSingleNode(".//div[contains(concat(' ',normalize-space(@class),' '),' TeamScore__top ')]");
            if (dateNode is null || match is null ||
                !TryParseLEquipeDate(CleanHtml(dateNode.InnerText), startYear, endYear, out var date))
            {
                continue;
            }
            var game = ParseLEquipeMatch(match, date, season, round, sourceUrl, html);
            if (game is not null) games.Add(game);
        }
        return games.DistinctBy(game => game.SourceGameId).ToList();
    }

    private static BasketballProviderGame? ParseLEquipeMatch(
        HtmlNode match,
        DateTime date,
        string season,
        string round,
        string sourceUrl,
        string html)
    {
        var homeLink = match.SelectSingleNode(".//a[contains(@class,'TeamScore__team--home')]");
        var awayLink = match.SelectSingleNode(".//a[contains(@class,'TeamScore__team--away')]");
        var gameLink = match.SelectSingleNode(".//a[contains(@href,'match-en-direct')]");
        var scoreNode = match.SelectSingleNode(".//div[contains(@class,'TeamScore__score--ended')]");
        if (homeLink is null || awayLink is null || gameLink is null || scoreNode is null)
        {
            return null; // Series aggregate rows do not have a match URL.
        }
        var score = ScoreRegex().Match(CleanHtml(scoreNode.InnerText));
        var homeId = LEquipeTeamIdRegex().Match(homeLink.GetAttributeValue("href", string.Empty)).Groups[1].Value;
        var awayId = LEquipeTeamIdRegex().Match(awayLink.GetAttributeValue("href", string.Empty)).Groups[1].Value;
        var gameId = LEquipeGameIdRegex().Match(gameLink.GetAttributeValue("href", string.Empty)).Groups[1].Value;
        if (!score.Success || !short.TryParse(score.Groups[1].Value, out var homeScore) ||
            !short.TryParse(score.Groups[2].Value, out var awayScore) ||
            homeId.Length == 0 || awayId.Length == 0 || gameId.Length == 0)
        {
            return null;
        }
        var homeName = CanonicalizeTeamName(CleanHtml(homeLink.InnerText));
        var awayName = CanonicalizeTeamName(CleanHtml(awayLink.InnerText));
        return BuildGame(
            $"lequipe:{gameId}", date, homeName, $"lequipe-club:{homeId}", homeScore,
            awayName, $"lequipe-club:{awayId}", awayScore, sourceUrl, season,
            LEquipeParserVersion, Hash(html), IsLEquipeRegularRound(round) ? "Regular Season" : "Playoffs",
            NormalizeLEquipeRound(round));
    }

    private async Task<(IReadOnlyCollection<BasketballProviderGame>, bool, IReadOnlyCollection<string>)> GetTheSportsLeagueAsync(
        string season,
        int startYear,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var pages = TheSportsPages[startYear];
        var games = new List<BasketballProviderGame>();
        var warnings = new List<string>();
        foreach (var (page, phase) in new[] { (pages.Regular, "Regular Season"), (pages.Playoffs, "Playoffs") })
        {
            var pageUrl = new Uri(new Uri(options.Value.TheSportsBaseUrl), page).ToString();
            var landing = await FetchStringAsync(pageUrl, context, cancellationToken);
            var document = new HtmlDocument();
            document.LoadHtml(landing);
            var optionsNodes = document.DocumentNode.SelectNodes("//select[@id='select_manche']/option") ?? Enumerable.Empty<HtmlNode>();
            var rounds = optionsNodes
                .Select(node => new
                {
                    Id = MancheIdRegex().Match(node.GetAttributeValue("value", string.Empty)).Groups[1].Value,
                    Round = CleanHtml(node.InnerText)
                })
                .Where(item => item.Id.Length > 0)
                .DistinctBy(item => item.Id)
                .ToList();
            if (rounds.Count == 0)
            {
                warnings.Add($"{phase}: no round identifiers were found at {pageUrl}.");
                continue;
            }

            foreach (var item in rounds)
            {
                var ajaxUrl = new Uri(new Uri(options.Value.TheSportsBaseUrl),
                    $"ajax_php.php?majajax=resultats_manche_collectif&mancheid={item.Id}&langage=en").ToString();
                var roundHtml = await FetchStringAsync(ajaxUrl, context, cancellationToken, pageUrl);
                games.AddRange(ParseTheSportsRound(roundHtml, season, item.Id, item.Round, phase, ajaxUrl));
            }
        }

        warnings.Add("TheSports supplies historical local dates; imported times are 12:00 UTC.");
        if (games.Count == 0)
        {
            warnings.Add("TheSports pages contained no parseable games.");
        }
        return (games, false, warnings);
    }

    internal static IReadOnlyList<BasketballProviderGame> ParseTheSportsRound(
        string html,
        string season,
        string mancheId,
        string round,
        string phase,
        string sourceUrl)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var games = new List<BasketballProviderGame>();
        var tables = document.DocumentNode.SelectNodes("//table[contains(@class,'table-style-2')]") ?? Enumerable.Empty<HtmlNode>();
        var index = 0;
        foreach (var table in tables)
        {
            DateTime? carriedDate = null;
            foreach (var row in table.SelectNodes("./tr") ?? Enumerable.Empty<HtmlNode>())
            {
                var dateHeading = row.SelectSingleNode(".//h6[contains(@class,'daterenc')]");
                if (dateHeading is not null && TryParseEnglishDate(CleanHtml(dateHeading.InnerText), out var headingDate))
                {
                    carriedDate = headingDate;
                    continue;
                }

                var cells = row.SelectNodes("./td")?.ToList() ?? [];
                if (cells.Count < 4)
                {
                    continue;
                }
                var scoreMatch = ScoreRegex().Match(CleanHtml(cells[2].InnerText));
                if (!scoreMatch.Success || !short.TryParse(scoreMatch.Groups[1].Value, out var homeScore) ||
                    !short.TryParse(scoreMatch.Groups[2].Value, out var awayScore))
                {
                    continue;
                }

                var hasCellDate = TryParseFlexibleDate(CleanHtml(cells[0].InnerText), out var date);
                if (!hasCellDate && carriedDate.HasValue)
                {
                    date = carriedDate.Value;
                }
                if (!hasCellDate && !carriedDate.HasValue)
                {
                    continue; // Series aggregate rows contain a score but are not games.
                }

                var homeLink = cells[1].SelectSingleNode(".//a[contains(@href,'identity-equ')]");
                var awayLink = cells[3].SelectSingleNode(".//a[contains(@href,'identity-equ')]");
                if (homeLink is null || awayLink is null)
                {
                    continue;
                }
                var homeName = CanonicalizeTeamName(CleanHtml(homeLink.GetAttributeValue("title", homeLink.InnerText)));
                var awayName = CanonicalizeTeamName(CleanHtml(awayLink.GetAttributeValue("title", awayLink.InnerText)));
                var homeId = TeamIdRegex().Match(homeLink.GetAttributeValue("href", string.Empty)).Groups[1].Value;
                var awayId = TeamIdRegex().Match(awayLink.GetAttributeValue("href", string.Empty)).Groups[1].Value;
                if (homeName.Length == 0 || awayName.Length == 0 || homeId.Length == 0 || awayId.Length == 0)
                {
                    continue;
                }

                index++;
                games.Add(BuildGame(
                    $"ts:{mancheId}:{index}", date, homeName, $"equ:{homeId}", homeScore,
                    awayName, $"equ:{awayId}", awayScore, sourceUrl, season,
                    TheSportsParserVersion, Hash(html), phase, round));
            }
        }
        return games;
    }

    private async Task<(IReadOnlyCollection<BasketballProviderGame>, bool, IReadOnlyCollection<string>)> GetFrenchCupAsync(
        string season,
        int startYear,
        int endYear,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var title = $"Coupe de France masculine de basket-ball {season}";
        var path = "w/api.php?action=parse&format=json&formatversion=2&prop=text%7Cwikitext%7Crevid" +
            $"&page={Uri.EscapeDataString(title)}";
        var url = new Uri(new Uri(options.Value.WikipediaBaseUrl), path).ToString();
        var json = await FetchStringAsync(url, context, cancellationToken);
        using var payload = JsonDocument.Parse(json);
        var parsed = payload.RootElement.GetProperty("parse");
        var html = parsed.GetProperty("text").GetString() ?? string.Empty;
        var wikitext = parsed.GetProperty("wikitext").GetString() ?? string.Empty;
        var revision = parsed.GetProperty("revid").GetInt64().ToString(CultureInfo.InvariantCulture);
        var games = ParseFrenchCupGames(html, wikitext, season, startYear, endYear, url, revision);
        var warnings = new List<string>
        {
            "French Wikipedia supplies dates without tip-off times; imported times are 12:00 UTC."
        };
        if (games.Count == 0)
        {
            warnings.Add("The French Cup article contained no parseable games.");
        }
        return (games, false, warnings);
    }

    internal static IReadOnlyList<BasketballProviderGame> ParseFrenchCupGames(
        string html,
        string wikitext,
        string season,
        int startYear,
        int endYear,
        string sourceUrl,
        string revision)
    {
        var candidates = new List<CupGame>();
        ParseCupHtmlTables(html, endYear, candidates);
        ParseCupPhaseTemplate(wikitext, endYear, candidates);
        ParseCupRawBracketRows(wikitext, endYear, candidates);

        var fetchedAt = DateTime.UtcNow;
        return candidates
            .Where(game => game.Date.Year >= startYear && game.Date.Year <= endYear)
            .DistinctBy(game => $"{game.Date:yyyyMMdd}|{Slug(game.Home)}|{game.HomeScore}|{Slug(game.Away)}|{game.AwayScore}")
            .Select((game, index) => new BasketballProviderGame(
                Source,
                $"frwiki:{season}:{Slug(game.Round)}:{Slug(game.Home)}:{Slug(game.Away)}:{game.HomeScore}-{game.AwayScore}",
                DateTime.SpecifyKind(game.Date.Date.AddHours(12), DateTimeKind.Utc),
                "finished",
                Slug(game.Home),
                game.Home,
                Slug(game.Away),
                game.Away,
                game.HomeScore,
                game.AwayScore,
                new BasketballProviderGameProvenance(sourceUrl, season, fetchedAt, FrenchCupParserVersion, revision),
                CompetitionPhase: "Cup",
                CompetitionRound: game.Round))
            .ToList();
    }

    private static void ParseCupHtmlTables(string html, int endYear, ICollection<CupGame> games)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var round = string.Empty;
        foreach (var node in document.DocumentNode.SelectNodes("//h2|//h3|//table") ?? Enumerable.Empty<HtmlNode>())
        {
            if (node.Name is "h2" or "h3")
            {
                round = NormalizeRound(CleanHtml(node.InnerText));
                continue;
            }
            if (!IsCupRound(round))
            {
                continue;
            }

            DateTime? carriedDate = null;
            foreach (var row in node.SelectNodes("./tbody/tr|./tr") ?? Enumerable.Empty<HtmlNode>())
            {
                var cells = row.SelectNodes("./th|./td")?.ToList() ?? [];
                var scoreIndex = cells.FindIndex(cell => ScoreRegex().IsMatch(CleanHtml(cell.InnerText)));
                if (scoreIndex <= 0 || scoreIndex >= cells.Count - 1)
                {
                    continue;
                }
                var score = ScoreRegex().Match(CleanHtml(cells[scoreIndex].InnerText));
                if (!short.TryParse(score.Groups[1].Value, out var homeScore) ||
                    !short.TryParse(score.Groups[2].Value, out var awayScore))
                {
                    continue;
                }

                for (var i = 0; i < scoreIndex; i++)
                {
                    if (TryParseFrenchDate(CleanHtml(cells[i].InnerText), endYear, out var parsedDate))
                    {
                        carriedDate = parsedDate;
                    }
                }
                var date = carriedDate ?? CupRoundFallbackDate(round, endYear);
                var home = TeamNameFromWikiCell(cells[scoreIndex - 1]);
                var away = TeamNameFromWikiCell(cells[scoreIndex + 1]);
                if (home.Length > 0 && away.Length > 0)
                {
                    games.Add(new(date, home, homeScore, away, awayScore, round));
                }
            }
        }
    }

    private static void ParseCupPhaseTemplate(string wikitext, int endYear, ICollection<CupGame> games)
    {
        var values = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in PhaseParameterRegex().Matches(wikitext))
        {
            var gameKey = match.Groups[1].Value.ToUpperInvariant();
            var field = match.Groups[2].Value.ToUpperInvariant();
            if (!values.TryGetValue(gameKey, out var fields))
            {
                fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                values[gameKey] = fields;
            }
            fields[field] = match.Groups[3].Value.Trim();
        }

        foreach (var item in values)
        {
            var fields = item.Value;
            if (!fields.TryGetValue("E1", out var homeText) || !fields.TryGetValue("E2", out var awayText) ||
                !fields.TryGetValue("S1", out var homeScoreText) || !fields.TryGetValue("S2", out var awayScoreText) ||
                !short.TryParse(DigitsOnly(homeScoreText), out var homeScore) ||
                !short.TryParse(DigitsOnly(awayScoreText), out var awayScore))
            {
                continue;
            }
            var round = item.Key[0] switch
            {
                'H' => "Round of 16",
                'Q' => "Quarterfinals",
                'D' => "Semifinals",
                'F' => "Final",
                _ => string.Empty
            };
            fields.TryGetValue("INFO", out var info);
            var date = TryParseFrenchDate(info ?? string.Empty, endYear, out var parsedDate)
                ? parsedDate
                : CupRoundFallbackDate(round, endYear);
            var home = CanonicalizeTeamName(CleanWikiTeam(homeText));
            var away = CanonicalizeTeamName(CleanWikiTeam(awayText));
            if (home.Length > 0 && away.Length > 0)
            {
                games.Add(new(date, home, homeScore, away, awayScore, round));
            }
        }
    }

    private static void ParseCupRawBracketRows(string wikitext, int endYear, ICollection<CupGame> games)
    {
        foreach (Match match in RawBracketRowRegex().Matches(wikitext))
        {
            if (!TryParseFrenchDate(match.Groups[1].Value, endYear, out var date) ||
                !short.TryParse(match.Groups[3].Value, out var homeScore) ||
                !short.TryParse(match.Groups[5].Value, out var awayScore))
            {
                continue;
            }
            var round = date.Month switch
            {
                5 => "Final",
                _ when date.Day == 27 => "Semifinals",
                _ when date.Day == 26 => "Quarterfinals",
                _ => "Round of 16"
            };
            var home = CanonicalizeTeamName(CleanWikiTeam(match.Groups[2].Value));
            var away = CanonicalizeTeamName(CleanWikiTeam(match.Groups[4].Value));
            if (home.Length > 0 && away.Length > 0)
            {
                games.Add(new(date, home, homeScore, away, awayScore, round));
            }
        }
    }

    private async Task<byte[]> FetchBytesAsync(
        string url,
        BackfillExecutionContext context,
        CancellationToken cancellationToken,
        string? referer = null)
    {
        using var response = await SendAsync(url, context, cancellationToken, referer);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<string> FetchStringAsync(
        string url,
        BackfillExecutionContext context,
        CancellationToken cancellationToken,
        string? referer = null)
    {
        var bytes = await FetchBytesAsync(url, context, cancellationToken, referer);
        return Encoding.UTF8.GetString(bytes);
    }

    private async Task<string> FetchGallicaStringAsync(
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
                    throw new InvalidOperationException("French historical Gallica request budget reached.");
                }
                context.ConsumeRequest();
                if (options.Value.GallicaMinRequestIntervalMilliseconds > 0)
                {
                    await Task.Delay(options.Value.GallicaMinRequestIntervalMilliseconds, retryCancellationToken);
                }
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(options.Value.UserAgent);
                if (!string.IsNullOrWhiteSpace(referer))
                {
                    request.Headers.Referrer = new Uri(referer);
                }
                return await httpClient.SendAsync(request, retryCancellationToken);
            },
            options.Value.GallicaMaxTransientRetries,
            options.Value.GallicaRetryBaseDelayMilliseconds,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private Task<HttpResponseMessage> SendAsync(
        string url,
        BackfillExecutionContext context,
        CancellationToken cancellationToken,
        string? referer)
        => BackfillHttpRetryPolicy.SendAsync(
            async retryCancellationToken =>
            {
                if (!context.CanUseRequest())
                {
                    throw new InvalidOperationException("French historical source request budget reached.");
                }
                context.ConsumeRequest();
                if (options.Value.MinRequestIntervalMilliseconds > 0)
                {
                    await Task.Delay(options.Value.MinRequestIntervalMilliseconds, retryCancellationToken);
                }
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(options.Value.UserAgent);
                if (!string.IsNullOrWhiteSpace(referer))
                {
                    request.Headers.Referrer = new Uri(referer);
                }
                return await httpClient.SendAsync(request, retryCancellationToken);
            },
            options.Value.MaxTransientRetries,
            options.Value.RetryBaseDelayMilliseconds,
            cancellationToken);

    private static BasketballProviderGame BuildGame(
        string sourceGameId,
        DateTime date,
        string homeName,
        string homeId,
        short homeScore,
        string awayName,
        string awayId,
        short awayScore,
        string sourceUrl,
        string season,
        string parserVersion,
        string revision,
        string phase,
        string round)
        => new(
            Source,
            sourceGameId,
            DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Utc),
            "finished",
            homeId,
            homeName,
            awayId,
            awayName,
            homeScore,
            awayScore,
            new BasketballProviderGameProvenance(sourceUrl, season, DateTime.UtcNow, parserVersion, revision),
            CompetitionPhase: phase,
            CompetitionRound: round);

    private static (int StartYear, int EndYear) ParseSeason(string season)
    {
        var canonical = SeasonLabelNormalizer.ToFullSeasonLabel(season);
        var parts = canonical.Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var startYear) ||
            !int.TryParse(parts[1], out var endYear) || endYear != startYear + 1)
        {
            throw new ArgumentException($"Invalid French season '{season}'.", nameof(season));
        }
        return (startYear, endYear);
    }

    private static string TeamNameFromWikiCell(HtmlNode cell)
    {
        var links = cell.SelectNodes(".//a[contains(@href,'/wiki/')]") ?? Enumerable.Empty<HtmlNode>();
        var link = links.FirstOrDefault(node =>
            !node.GetAttributeValue("href", string.Empty).Contains("Championnat_de_France", StringComparison.OrdinalIgnoreCase) &&
            !node.GetAttributeValue("href", string.Empty).Contains("Nationale_masculine", StringComparison.OrdinalIgnoreCase));
        return CanonicalizeTeamName(CleanHtml(link?.InnerText ?? cell.InnerText));
    }

    private static string CleanWikiTeam(string value)
    {
        value = Regex.Replace(value, @"\s*\(\[\[(?:Championnat|Pro A|Pro B|Nationale).*$", string.Empty, RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"'''|''", string.Empty);
        value = Regex.Replace(value, @"\[\[(?:[^\]|]+\|)?([^\]]+)\]\]", "$1");
        value = Regex.Replace(value, @"<ref.*?</ref>|<ref[^>]*/>", string.Empty, RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\{\{.*?\}\}", string.Empty);
        return CleanHtml(value);
    }

    private static string CanonicalizeTeamName(string value)
    {
        value = Regex.Replace(value, @"\s*\([^)]*(?:Pro\s*[AB]|N[123M]|Nationale)[^)]*\)\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
        value = Regex.Replace(value, @"\s*\(a\d*p\)\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
        var key = NormalizeKey(value);
        return CanonicalTeamNames.TryGetValue(key, out var canonical) ? canonical : value;
    }

    private static string NormalizeKey(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var chars = normalized.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ')
            .ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeRound(string value)
    {
        var key = NormalizeKey(value);
        if (key.Contains("journee")) return Regex.Match(key, @"journee\s+\d+").Value.Replace("journee", "Round", StringComparison.OrdinalIgnoreCase).Trim();
        if (key.Contains("trente deuxieme")) return "Round of 32";
        if (key.Contains("seizieme")) return "Round of 16 (32 teams)";
        if (key.Contains("huitieme")) return "Round of 16";
        if (key.Contains("quart")) return "Quarterfinals";
        if (key.Contains("demi")) return "Semifinals";
        if (key.Contains("final")) return "Final";
        return CleanHtml(value);
    }

    private static bool IsCupRound(string round) => round is
        "Round of 32" or "Round of 16 (32 teams)" or "Round of 16" or "Quarterfinals" or "Semifinals" or "Final";

    private static DateTime CupRoundFallbackDate(string round, int endYear) => round switch
    {
        "Round of 32" => new DateTime(endYear, 2, 1),
        "Round of 16 (32 teams)" => new DateTime(endYear, 3, 20),
        "Round of 16" => new DateTime(endYear, 4, 15),
        "Quarterfinals" => new DateTime(endYear, 4, 22),
        "Semifinals" => new DateTime(endYear, 4, 23),
        "Final" => new DateTime(endYear, 5, 15),
        _ => new DateTime(endYear, 4, 1)
    };

    private static bool TryParseLEquipeDate(string value, int startYear, int endYear, out DateTime date)
    {
        var match = Regex.Match(NormalizeKey(value), @"(?<!\d)(\d{1,2})\s+([a-z]+)(?:\s+(\d{4}))?");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var day))
        {
            date = default;
            return false;
        }
        var monthToken = match.Groups[2].Value;
        var month = monthToken switch
        {
            var token when token.StartsWith("janv", StringComparison.Ordinal) => 1,
            var token when token.StartsWith("fevr", StringComparison.Ordinal) => 2,
            var token when token.StartsWith("mars", StringComparison.Ordinal) => 3,
            var token when token.StartsWith("avr", StringComparison.Ordinal) => 4,
            var token when token.StartsWith("mai", StringComparison.Ordinal) => 5,
            var token when token.StartsWith("juin", StringComparison.Ordinal) => 6,
            var token when token.StartsWith("juil", StringComparison.Ordinal) => 7,
            var token when token.StartsWith("aout", StringComparison.Ordinal) => 8,
            var token when token.StartsWith("sept", StringComparison.Ordinal) => 9,
            var token when token.StartsWith("oct", StringComparison.Ordinal) => 10,
            var token when token.StartsWith("nov", StringComparison.Ordinal) => 11,
            var token when token.StartsWith("dec", StringComparison.Ordinal) => 12,
            _ => 0
        };
        var year = int.TryParse(match.Groups[3].Value, out var parsedYear)
            ? parsedYear
            : month >= 7 ? startYear : endYear;
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

    private static bool IsLEquipeRegularRound(string round) =>
        NormalizeKey(round).Contains("journee", StringComparison.Ordinal);

    private static string NormalizeLEquipeRound(string round)
    {
        var key = NormalizeKey(round);
        var roundNumber = Regex.Match(key, @"\d+").Value;
        if (key.Contains("journee", StringComparison.Ordinal) && roundNumber.Length > 0) return $"Round {roundNumber}";
        if (key.Contains("tour preliminaire", StringComparison.Ordinal)) return "Preliminary Round";
        if (key.Contains("8es de finale", StringComparison.Ordinal) || key.Contains("huitieme", StringComparison.Ordinal)) return "Round of 16";
        if (key.Contains("quart", StringComparison.Ordinal)) return "Quarterfinals";
        if (key.Contains("demi", StringComparison.Ordinal)) return "Semifinals";
        if (key.Contains("final", StringComparison.Ordinal)) return "Final";
        return CleanHtml(round);
    }

    private static bool TryParseFrenchDate(string value, int defaultYear, out DateTime date)
    {
        var key = NormalizeKey(value);
        var match = Regex.Match(key, @"(?<!\d)(\d{1,2})\s+(janvier|fevrier|mars|avril|mai|juin|juillet|aout|septembre|octobre|novembre|decembre)(?:\s+(\d{4}))?");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var day) &&
            FrenchMonths.TryGetValue(match.Groups[2].Value, out var month))
        {
            var year = int.TryParse(match.Groups[3].Value, out var parsedYear) ? parsedYear : defaultYear;
            try
            {
                date = new DateTime(year, month, day);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }
        date = default;
        return false;
    }

    private static bool TryParseEnglishDate(string value, out DateTime date) =>
        DateTime.TryParseExact(value, "d MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ||
        DateTime.TryParseExact(value, "dd MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool TryParseFlexibleDate(string value, out DateTime date) =>
        DateTime.TryParseExact(value, new[] { "dd/MM/yyyy", "d/M/yyyy", "d MMMM yyyy", "dd MMMM yyyy" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string CleanHtml(string value) =>
        Regex.Replace(HtmlEntity.DeEntitize(value).Replace('\u00A0', ' '), @"\s+", " ").Trim();

    private static string CleanMarkup(string value) =>
        CleanHtml(Regex.Replace(value, "<[^>]+>", " "));

    private static string DigitsOnly(string value) => new(value.Where(char.IsDigit).ToArray());

    private static string Slug(string value) =>
        string.Join('-', NormalizeKey(value).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex(@"mancheid=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MancheIdRegex();

    [GeneratedRegex(@"equ(\d+)\.html", RegexOptions.IgnoreCase)]
    private static partial Regex TeamIdRegex();

    [GeneratedRegex(@"BasketFicheClub(\d+)\.html", RegexOptions.IgnoreCase)]
    private static partial Regex LEquipeTeamIdRegex();

    [GeneratedRegex(@"match-en-direct/.+?/(\d+)(?:[/?#]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex LEquipeGameIdRegex();

    [GeneratedRegex(@"^\s*(\d{1,3})\s*[-–]\s*(\d{1,3})(?:\s+(?:ot|a\.?p\.?))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ScoreRegex();

    [GeneratedRegex(@"^\|\s*([HQDF]\d+)-(info|E1|S1|E2|S2)\s*=\s*(.*?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex PhaseParameterRegex();

    [GeneratedRegex(@"^\|\s*(\{\{Date.*?\}\}(?:\s+à\s+\[\[.*?\]\])?)\s*\|\s*(.+?)\s*\|\s*'{0,3}(\d+)'{0,3}\s*\|\|\s*(.+?)\s*\|\s*'{0,3}(\d+)'{0,3}\s*\|", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex RawBracketRowRegex();

    [GeneratedRegex(@"<B>\s*([^<]+?)\s*</B>(.*?)</TABLE>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex BasketArchivesBlockRegex();

    [GeneratedRegex(@"<I>(.*?)(?:</I>|<I>)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex BasketArchivesDateRegex();

    private sealed record GallicaSeasonSource(string Ark, int ExpectedGames);
    private sealed record GallicaTeamAlias(string Pattern, string Canonical);
    private sealed record GallicaTeamMatch(int Index, int Length, string Canonical);
    private sealed record GallicaGame(string Home, short HomeScore, string Away, short AwayScore);
    private sealed record CupGame(DateTime Date, string Home, short HomeScore, string Away, short AwayScore, string Round);
}
