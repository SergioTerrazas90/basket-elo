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
    public const string TheSportsParserVersion = "the-sports-fr-v1";
    public const string FrenchCupParserVersion = "fr-wikipedia-cup-v1";

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
            ["pau orthez"] = "Pau-Orthez",
            ["poitiers basket 86"] = "Poitiers",
            ["reims champagne basket"] = "Reims",
            ["rouen metropole basket"] = "Rouen",
            ["rupella basket 17"] = "La Rochelle",
            ["saint chamond basket"] = "St. Chamond",
            ["saint etienne basket"] = "Saint-Étienne",
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
            "FR_TOP_FLIGHT" when startYear is >= 2001 and <= 2007 =>
                await GetTheSportsLeagueAsync(season, startYear, context, cancellationToken),
            "COUPE_DE_FRANCE" when startYear is >= 2004 and <= 2007 =>
                await GetFrenchCupAsync(season, startYear, endYear, context, cancellationToken),
            "FR_TOP_FLIGHT" => throw new ArgumentException(
                "French historical league coverage supports 1981-1982 and 1998-1999 through 2007-2008.", nameof(season)),
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

    private sealed record CupGame(DateTime Date, string Home, short HomeScore, string Away, short AwayScore, string Round);
}
