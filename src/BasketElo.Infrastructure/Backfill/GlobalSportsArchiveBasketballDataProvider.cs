using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Global Sports Archive provider for men's national-team tournaments. Global
/// Sports Archive publishes stages and group pages as separate documents, so a
/// tournament is complete only after every discovered stage and gameweek/page
/// has been traversed.
/// </summary>
public sealed class GlobalSportsArchiveBasketballDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "global-sports-archive";
    public const string ParserVersion = "global-sports-archive-international-html-v3";

    private const string AfroBasketSeedPath = "/competition/basketball/fiba-africa-championship-2003-egypt/group-stage/111126/";
    private const string AsiaBasketSeedPath = "/competition/basketball/abc-championship-2003-china-pr/group-stage/94961/";
    private const string EuroBasketSeedPath = "/competition/basketball/eurobasket-1961-yugoslavia/group-stage/44576/";
    private const string AfroBasketQualifiersSeedPath = "/competition/basketball/fiba-afrobasket-qualifiers-2025/group-stage/95384/";
    private const string AfroBasketPreQualifiersSeedPath = "/competition/basketball/fiba-afrobasket-qualifiers-2025/pre-qualifying-round-final/95405/";
    private const string WorldCupQualificationSeedPath = "/competition/basketball/fiba-wc-qualification-2027-qatar/round-2/124205/";
    private const string AsiaCupQualifiersSeedPath = "/competition/basketball/fiba-asia-cup-qualification-2025/group-stage/89199/";
    private const string AsianGamesSeedPath = "/competition/basketball/asian-games-2022-hangzhou/regular-season/87041/";
    private const string SummerOlympicsSeedPath = "/competition/basketball/summer-olympics-2024-paris/gold-medal/97057/";
    private const string OlympicsQualificationSeedPath = "/competition/basketball/olympics-qualification-2024-paris/pre-qualifying-round/83731/";
    private const string AmeriCupSeedPath = "/competition/basketball/fiba-americup-2025-nicaragua/group-stage/120539/";
    private const string AmeriCupQualificationSeedPath = "/competition/basketball/fiba-americup-qualification-2025-nicaragua/qualifiers/93859/";

    private static readonly IReadOnlyDictionary<string, DomesticArchiveLeague> DomesticLeagues =
        new Dictionary<string, DomesticArchiveLeague>(StringComparer.OrdinalIgnoreCase)
        {
            ["Europe|ABA League"] = new("gsa-aba-league", "ABA League", null, "/competition/basketball/admiral-bet-aba-league-2025-2026/final/130499/", "aba-league|admiral-bet-aba-league"),
            ["Europe|BIBL"] = new("gsa-bibl", "BIBL", null, "/competition/basketball/delasport-bibl-2023-2024/final/91369/", "bibl|delasport-bibl"),
            ["Europe|Champions League"] = new("gsa-champions-league", "Champions League", null, "/competition/basketball/basketball-champions-league-2026-2027/1st-qualifying-round/156389/", "basketball-champions-league"),
            ["Europe|Eurocup"] = new("gsa-eurocup", "Eurocup", null, "/competition/basketball/bkt-eurocup-2026-2027/regular-season/156578/", "7days-eurocup|bkt-eurocup|eurocup"),
            ["Europe|Euroleague"] = new("gsa-euroleague", "Euroleague", null, "/competition/basketball/euroleague-2025-2026/final/127448/", "turkish-airlines-euroleague|euroleague"),
            ["Spain|ACB"] = new("gsa-acb", "ACB", "ESP", "/competition/basketball/liga-endesa-2025-2026/final/130985/", "liga-endesa|liga-acb")
        };

    private static readonly IReadOnlyDictionary<string, ArchiveDefinition> ArchiveDefinitions =
        DomesticLeagues.Values.ToDictionary(
            league => league.SourceLeagueId,
            league => new ArchiveDefinition(league.SeedPath, league.CompetitionSlugPattern),
            StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, ArchiveDefinition> InternationalDefinitions =
        new Dictionary<string, ArchiveDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["fiba-afrobasket"] = new(AfroBasketSeedPath, "fiba-afrobasket|fiba-africa-championship"),
            ["fiba-afrobasket-qualifiers"] = new(AfroBasketQualifiersSeedPath, "fiba-afrobasket-qualifiers"),
            ["fiba-afrobasket-pre-qualifiers"] = new(
                AfroBasketPreQualifiersSeedPath,
                "fiba-afrobasket-qualifiers",
                new Dictionary<int, string?>
                {
                    [2021] = null,
                    [2025] = AfroBasketPreQualifiersSeedPath
                },
                "pre-qualifying-round-final"),
            ["fiba-wc-qualification"] = new(WorldCupQualificationSeedPath, "fiba-wc-qualification"),
            ["fiba-asia-cup"] = new(AsiaBasketSeedPath, "abc-championship|fiba-asia-championship|fiba-asia-cup"),
            ["fiba-asia-cup-qualification"] = new(AsiaCupQualifiersSeedPath, "fiba-asia-cup-qualification"),
            ["asian-games"] = new(AsianGamesSeedPath, "asian-games"),
            ["eurobasket"] = new(EuroBasketSeedPath, "eurobasket"),
            ["eurobasket-qualifiers"] = new("/competition/basketball/eurobasket-qualifiers-2025/group-stage/70688/", "eurobasket-qualifiers"),
            // The 2021/2025 pre-qualifier games are GSA records reconciled into
            // their own competition. Keep a distinct source id so coverage can
            // count them separately from the main EuroBasket qualifiers.
            ["eurobasket-pre-qualifiers"] = new(
                "/competition/basketball/eurobasket-qualifiers-2025/group-stage/70688/",
                "eurobasket-qualifiers",
                new Dictionary<int, string?>
                {
                    [2029] = "/competition/basketball/eurobasket-qualifiers-2029/round-1/135761/"
                }),
            ["summer-olympics"] = new(SummerOlympicsSeedPath, "summer-olympics"),
            ["olympics-qualification"] = new(OlympicsQualificationSeedPath, "olympics-qualification"),
            ["fiba-americup"] = new(AmeriCupSeedPath, "fiba-americup|fiba-americas-championship|tournament-of-the-americas"),
            ["fiba-americup-qualification"] = new(AmeriCupQualificationSeedPath, "fiba-americup-qualification"),
            ["fiba-basketball-world-cup"] = new("/competition/basketball/fiba-basketball-world-cup-2023-philippines-japan-indonesia/group-stage/75848/", "fiba-basketball-world-cup|fiba-world-championship")
        };

    private static readonly IReadOnlyDictionary<string, string> InternationalLeagueIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Africa|FIBA AfroBasket"] = "fiba-afrobasket",
            ["Africa|FIBA AfroBasket Qualifiers"] = "fiba-afrobasket-qualifiers",
            ["Africa|FIBA AfroBasket Pre-Qualifiers"] = "fiba-afrobasket-pre-qualifiers",
            ["World|FIBA WC Qualification"] = "fiba-wc-qualification",
            ["Asia|FIBA Asia Cup"] = "fiba-asia-cup",
            ["Asia|FIBA Asia Cup Qualification"] = "fiba-asia-cup-qualification",
            ["Asia|Asian Games"] = "asian-games",
            ["Europe|EuroBasket"] = "eurobasket",
            ["Europe|FIBA EuroBasket"] = "eurobasket",
            ["Europe|EuroBasket Qualifiers"] = "eurobasket-qualifiers",
            ["Europe|FIBA EuroBasket Pre-Qualifiers"] = "eurobasket-pre-qualifiers",
            ["World|FIBA Basketball World Cup"] = "fiba-basketball-world-cup",
            ["World|Summer Olympics"] = "summer-olympics",
            ["World|Olympics Qualification"] = "olympics-qualification",
            ["World|FIBA AmeriCup"] = "fiba-americup",
            ["World|FIBA AmeriCup Qualification"] = "fiba-americup-qualification"
        };

    private static readonly IReadOnlyDictionary<string, TeamIdentity> TeamIdentities =
        new Dictionary<string, TeamIdentity>(StringComparer.OrdinalIgnoreCase)
        {
            ["Algeria"] = new("ALG", "Algeria", "ALG"),
            ["Angola"] = new("ANG", "Angola", "ANG"),
            ["Benin"] = new("BEN", "Benin", "BEN"),
            ["Cabo Verde"] = new("CPV", "Cabo Verde", "CPV"),
            ["Cameroon"] = new("CMR", "Cameroon", "CMR"),
            ["Central African Republic"] = new("CAF", "Central African Republic", "CAF"),
            ["Congo"] = new("CGO", "Congo", "CGO"),
            ["Côte d'Ivoire"] = new("CIV", "Côte d'Ivoire", "CIV"),
            ["DR Congo"] = new("COD", "DR Congo", "COD"),
            ["Egypt"] = new("EGY", "Egypt", "EGY"),
            ["Ethiopia"] = new("ETH", "Ethiopia", "ETH"),
            ["Gambia"] = new("GAM", "Gambia", "GAM"),
            ["Guinea"] = new("GUI", "Guinea", "GUI"),
            ["Kenya"] = new("KEN", "Kenya", "KEN"),
            ["Liberia"] = new("LBR", "Liberia", "LBR"),
            ["Libya"] = new("LBA", "Libya", "LBA"),
            ["Madagascar"] = new("MAD", "Madagascar", "MAD"),
            ["Mali"] = new("MLI", "Mali", "MLI"),
            ["Mauritania"] = new("MTN", "Mauritania", "MTN"),
            ["Morocco"] = new("MAR", "Morocco", "MAR"),
            ["Mozambique"] = new("MOZ", "Mozambique", "MOZ"),
            ["Niger"] = new("NIG", "Niger", "NIG"),
            ["Nigeria"] = new("NGR", "Nigeria", "NGR"),
            ["Palestine"] = new("PLE", "Palestine", "PLE"),
            ["Senegal"] = new("SEN", "Senegal", "SEN"),
            ["Somalia"] = new("SOM", "Somalia", "SOM"),
            ["South Africa"] = new("RSA", "South Africa", "RSA"),
            ["Sudan"] = new("SUD", "Sudan", "SUD"),
            ["Tanzania"] = new("TAN", "Tanzania", "TAN"),
            ["Togo"] = new("TOG", "Togo", "TOG"),
            ["Tunisia"] = new("TUN", "Tunisia", "TUN"),
            ["United Arab Republic"] = new("UAR", "United Arab Republic", "UAR"),
            ["Zaire"] = new("ZAI", "Zaire", "ZAI"),
            ["Zimbabwe"] = new("ZIM", "Zimbabwe", "ZIM"),
            ["Afghanistan"] = new("AFG", "Afghanistan", "AFG"),
            ["Australia"] = new("AUS", "Australia", "AUS"),
            ["Bahrain"] = new("BHR", "Bahrain", "BHR"),
            ["Bangladesh"] = new("BAN", "Bangladesh", "BAN"),
            ["China PR"] = new("CHN", "China PR", "CHN"),
            ["Chinese Taipei"] = new("TPE", "Chinese Taipei", "TPE"),
            ["Guam"] = new("GUM", "Guam", "GUM"),
            ["Hong Kong SAR"] = new("HKG", "Hong Kong SAR", "HKG"),
            ["Hong Kong"] = new("HKG", "Hong Kong", "HKG"),
            ["India"] = new("IND", "India", "IND"),
            ["Indonesia"] = new("INA", "Indonesia", "INA"),
            ["Iraq"] = new("IRQ", "Iraq", "IRQ"),
            ["IR Iran"] = new("IRI", "IR Iran", "IRI"),
            ["Iran"] = new("IRI", "Iran", "IRI"),
            ["Japan"] = new("JPN", "Japan", "JPN"),
            ["Jordan"] = new("JOR", "Jordan", "JOR"),
            ["Kazakhstan"] = new("KAZ", "Kazakhstan", "KAZ"),
            ["Korea DPR"] = new("PRK", "Korea DPR", "PRK"),
            ["Korea Republic"] = new("KOR", "Korea Republic", "KOR"),
            ["Kuwait"] = new("KUW", "Kuwait", "KUW"),
            ["Lebanon"] = new("LBN", "Lebanon", "LBN"),
            ["Malaysia"] = new("MAS", "Malaysia", "MAS"),
            ["Mongolia"] = new("MGL", "Mongolia", "MGL"),
            ["Myanmar"] = new("MYA", "Myanmar", "MYA"),
            ["Nepal"] = new("NEP", "Nepal", "NEP"),
            ["New Zealand"] = new("NZL", "New Zealand", "NZL"),
            ["Oman"] = new("OMA", "Oman", "OMA"),
            ["Pakistan"] = new("PAK", "Pakistan", "PAK"),
            ["Philippines"] = new("PHI", "Philippines", "PHI"),
            ["Qatar"] = new("QAT", "Qatar", "QAT"),
            ["Saudi Arabia"] = new("KSA", "Saudi Arabia", "KSA"),
            ["Singapore"] = new("SIN", "Singapore", "SIN"),
            ["Sri Lanka"] = new("SRI", "Sri Lanka", "SRI"),
            ["Syria"] = new("SYR", "Syria", "SYR"),
            ["Thailand"] = new("THA", "Thailand", "THA"),
            ["United Arab Emirates"] = new("UAE", "United Arab Emirates", "UAE"),
            ["UAE"] = new("UAE", "UAE", "UAE"),
            ["Uzbekistan"] = new("UZB", "Uzbekistan", "UZB"),
            ["Vietnam"] = new("VIE", "Vietnam", "VIE"),
            ["Yemen"] = new("YEM", "Yemen", "YEM")
            , ["Austria"] = new("AUT", "Austria", "AUT")
            , ["Belgium"] = new("BEL", "Belgium", "BEL")
            , ["Bulgaria"] = new("BUL", "Bulgaria", "BUL")
            , ["Czechoslovakia"] = new("TCH", "Czechoslovakia", "TCH")
            , ["Czech Republic"] = new("CZE", "Czech Republic", "CZE")
            , ["Croatia"] = new("CRO", "Croatia", "CRO")
            , ["Cyprus"] = new("CYP", "Cyprus", "CYP")
            , ["Denmark"] = new("DEN", "Denmark", "DEN")
            , ["England"] = new("ENG", "England", "ENG")
            , ["Estonia"] = new("EST", "Estonia", "EST")
            , ["Finland"] = new("FIN", "Finland", "FIN")
            , ["France"] = new("FRA", "France", "FRA")
            , ["Georgia"] = new("GEO", "Georgia", "GEO")
            , ["Germany"] = new("GER", "Germany", "GER")
            , ["German DR"] = new("GDR", "German DR", "GDR")
            , ["West Germany"] = new("FRG", "West Germany", "FRG")
            , ["Greece"] = new("GRE", "Greece", "GRE")
            , ["Hungary"] = new("HUN", "Hungary", "HUN")
            , ["Israel"] = new("ISR", "Israel", "ISR")
            , ["Italy"] = new("ITA", "Italy", "ITA")
            , ["Latvia"] = new("LAT", "Latvia", "LAT")
            , ["Lithuania"] = new("LTU", "Lithuania", "LTU")
            , ["Luxembourg"] = new("LUX", "Luxembourg", "LUX")
            , ["North Macedonia"] = new("MKD", "North Macedonia", "MKD")
            , ["Norway"] = new("NOR", "Norway", "NOR")
            , ["Poland"] = new("POL", "Poland", "POL")
            , ["Portugal"] = new("POR", "Portugal", "POR")
            , ["Romania"] = new("ROU", "Romania", "ROU")
            , ["Russia"] = new("RUS", "Russia", "RUS")
            , ["Soviet Union"] = new("URS", "Soviet Union", "URS")
            , ["USSR"] = new("URS", "USSR", "URS")
            , ["Serbia"] = new("SRB", "Serbia", "SRB")
            , ["Serbia and Montenegro"] = new("SCG", "Serbia and Montenegro", "SCG")
            , ["Slovakia"] = new("SVK", "Slovakia", "SVK")
            , ["Slovenia"] = new("SLO", "Slovenia", "SLO")
            , ["Spain"] = new("ESP", "Spain", "ESP")
            , ["Sweden"] = new("SWE", "Sweden", "SWE")
            , ["Switzerland"] = new("SUI", "Switzerland", "SUI")
            , ["Turkey"] = new("TUR", "Turkey", "TUR")
            , ["Türkiye"] = new("TUR", "Türkiye", "TUR")
            , ["Ukraine"] = new("UKR", "Ukraine", "UKR")
            , ["Yugoslavia"] = new("YUG", "Yugoslavia", "YUG")
        };

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (InternationalLeagueIds.TryGetValue($"{country}|{leagueName}", out var internationalId))
        {
            return Task.FromResult<BasketballProviderLeague?>(
                new BasketballProviderLeague(Source, internationalId, leagueName, null, "year"));
        }

        if (DomesticLeagues.TryGetValue($"{country}|{leagueName}", out var domesticLeague))
        {
            return Task.FromResult<BasketballProviderLeague?>(
                new BasketballProviderLeague(
                    Source,
                    domesticLeague.SourceLeagueId,
                    domesticLeague.Name,
                    domesticLeague.CountryCode));
        }

        return Task.FromResult<BasketballProviderLeague?>(null);
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var year = SeasonLabelNormalizer.ParseStartYear(season);
        var warnings = new List<string>();
        var definition = GetDefinition(league);
        var seedPath = definition.SeasonSeedPaths is not null && definition.SeasonSeedPaths.TryGetValue(year, out var configuredSeedPath)
            ? configuredSeedPath
            : definition.SeedPath;
        if (string.IsNullOrWhiteSpace(seedPath))
        {
            warnings.Add($"Global Sports Archive {league.Name} edition {year} has no corresponding tournament page in the source.");
            return ([], false, warnings);
        }

        var seed = await GetPageAsync(seedPath, context, cancellationToken);
        var stagePaths = FindStagePaths(seed.Content, year, definition).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (stagePaths.Count == 0)
        {
            warnings.Add($"Global Sports Archive {league.Name} edition {year} was not found in the historical selector.");
            return ([], false, warnings);
        }

        var bootstrapPath = stagePaths.OrderBy(StageOrder).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).First();
        var bootstrap = seed;
        if (!bootstrapPath.Equals(definition.SeedPath, StringComparison.OrdinalIgnoreCase))
        {
            if (!context.CanUseRequest())
            {
                warnings.Add($"Global Sports Archive request budget reached before edition {bootstrapPath} could be fetched.");
                return ([], false, warnings);
            }

            try
            {
                bootstrap = await GetPageAsync(bootstrapPath, context, cancellationToken);
                foreach (var discoveredPath in FindStagePaths(bootstrap.Content, year, definition))
                {
                    stagePaths.Add(discoveredPath);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                warnings.Add($"Global Sports Archive edition page timed out: {bootstrapPath}.");
            }
            catch (HttpRequestException exception)
            {
                warnings.Add($"Global Sports Archive edition page could not be fetched: {bootstrapPath} ({exception.StatusCode?.ToString() ?? exception.Message}).");
            }
        }

        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.Ordinal);
        foreach (var stagePath in stagePaths.OrderBy(StageOrder).ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!context.CanUseRequest())
            {
                warnings.Add($"Global Sports Archive request budget reached before stage {stagePath} could be fetched.");
                break;
            }

            try
            {
                var page = stagePath.Equals(definition.SeedPath, StringComparison.OrdinalIgnoreCase)
                    ? seed
                    : stagePath.Equals(bootstrapPath, StringComparison.OrdinalIgnoreCase) && !bootstrapPath.Equals(definition.SeedPath, StringComparison.OrdinalIgnoreCase)
                        ? bootstrap
                    : await GetPageAsync(stagePath, context, cancellationToken);

                var pages = new List<(string Content, DateTime FetchedAtUtc, string Revision)> { page };
                var pageCount = GetPageCount(page.Content);
                var currentPage = GetCurrentPage(page.Content);
                var roundId = GetRoundId(stagePath);
                for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    if (pageIndex == currentPage)
                    {
                        continue;
                    }

                    if (!context.CanUseRequest())
                    {
                        warnings.Add($"Global Sports Archive request budget reached before pagination page {pageIndex + 1}/{pageCount} of {stagePath} could be fetched.");
                        break;
                    }

                    try
                    {
                        pages.Add(await GetAjaxPageAsync(roundId, pageIndex, context, cancellationToken));
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        warnings.Add($"Global Sports Archive pagination page {pageIndex + 1}/{pageCount} timed out: {stagePath}.");
                    }
                    catch (HttpRequestException exception)
                    {
                        warnings.Add($"Global Sports Archive pagination page {pageIndex + 1}/{pageCount} could not be fetched: {stagePath} ({exception.StatusCode?.ToString() ?? exception.Message}).");
                    }
                }

                var weekCount = GetWeekCount(page.Content);
                var currentWeek = GetCurrentWeek(page.Content);
                for (var week = 1; week <= weekCount; week++)
                {
                    if (week == currentWeek)
                    {
                        continue;
                    }

                    if (!context.CanUseRequest())
                    {
                        warnings.Add($"Global Sports Archive request budget reached before gameweek {week}/{weekCount} of {stagePath} could be fetched.");
                        break;
                    }

                    try
                    {
                        pages.Add(await GetAjaxWeekAsync(roundId, week, context, cancellationToken));
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        warnings.Add($"Global Sports Archive gameweek {week}/{weekCount} timed out: {stagePath}.");
                    }
                    catch (HttpRequestException exception)
                    {
                        warnings.Add($"Global Sports Archive gameweek {week}/{weekCount} could not be fetched: {stagePath} ({exception.StatusCode?.ToString() ?? exception.Message}).");
                    }
                }

                foreach (var pagedDocument in pages)
                {
                    foreach (var game in ParseGames(pagedDocument.Content, pagedDocument.FetchedAtUtc, pagedDocument.Revision, stagePath, year, warnings))
                    {
                        games[game.SourceGameId] = game;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                warnings.Add($"Global Sports Archive stage page timed out: {stagePath}.");
            }
            catch (HttpRequestException exception)
            {
                warnings.Add($"Global Sports Archive stage page could not be fetched: {stagePath} ({exception.StatusCode?.ToString() ?? exception.Message}).");
            }
        }

        return (games.Values.OrderBy(game => game.GameDateTimeUtc).ThenBy(game => game.SourceGameId).ToArray(), false, warnings);
    }

    private async Task<(string Content, DateTime FetchedAtUtc, string Revision)> GetPageAsync(
        string path,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!context.CanUseRequest())
        {
            throw new InvalidOperationException("Global Sports Archive request budget reached before the archive page could be fetched.");
        }

        context.ConsumeRequest();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await httpClient.GetAsync(path, timeout.Token);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return (content, DateTime.UtcNow, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..16]);
    }

    private async Task<(string Content, DateTime FetchedAtUtc, string Revision)> GetAjaxPageAsync(
        string roundId,
        int pageIndex,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        context.ConsumeRequest();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("page", pageIndex.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("r", roundId),
            new KeyValuePair<string, string>("lang", "en")
        ]);
        using var response = await httpClient.PostAsync("/comp_ajax_page.php", content, timeout.Token);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(payload);
        var result = document.RootElement.TryGetProperty("result", out var resultElement)
            ? resultElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new InvalidDataException($"Global Sports Archive pagination response did not contain a result for round {roundId}, page {pageIndex}.");
        }

        return (result, DateTime.UtcNow, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result)))[..16]);
    }

    private async Task<(string Content, DateTime FetchedAtUtc, string Revision)> GetAjaxWeekAsync(
        string roundId,
        int week,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        context.ConsumeRequest();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("week", week.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("round", roundId),
            new KeyValuePair<string, string>("sport", "basketball"),
            new KeyValuePair<string, string>("widget_client_id", "1")
        ]);
        using var response = await httpClient.PostAsync("/comp_ajax.php", content, timeout.Token);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(payload);
        var result = document.RootElement.TryGetProperty("result", out var resultElement)
            ? resultElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new InvalidDataException($"Global Sports Archive gameweek response did not contain a result for round {roundId}, week {week}.");
        }

        return (result, DateTime.UtcNow, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result)))[..16]);
    }

    private static int GetPageCount(string html)
    {
        var matches = Regex.Matches(html, @"changePage\([^,]+,\s*(?<pages>\d+)\)", RegexOptions.IgnoreCase);
        return matches.Count == 0
            ? 1
            : matches.Max(match => int.TryParse(match.Groups["pages"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pages) ? pages : 1);
    }

    private static int GetCurrentPage(string html)
    {
        var match = Regex.Match(html, @"id=[""']cur_page[""']\s+value=[""'](?<page>\d+)[""']", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["page"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page)
            ? page
            : 0;
    }

    private static int GetWeekCount(string html)
    {
        var match = Regex.Match(html, @"id=[""']maxweek[""']\s+value=[""'](?<week>\d+)[""']", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["week"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var week)
            ? Math.Max(1, week)
            : 1;
    }

    private static int GetCurrentWeek(string html)
    {
        var match = Regex.Match(html, @"id=[""']curweek[""']\s+value=[""'](?<week>\d+)[""']", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["week"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var week)
            ? week
            : 1;
    }

    private static string GetRoundId(string path)
    {
        var match = Regex.Match(path, @"/(?<id>\d+)/?$", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["id"].Value : throw new InvalidDataException($"Global Sports Archive stage path has no round ID: {path}.");
    }

    private static IReadOnlyCollection<string> FindStagePaths(string html, int year, ArchiveDefinition definition)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var pattern = new Regex(
            $"/competition/basketball/(?:{definition.CompetitionSlugPattern})-{year}(?:-[^/]+)?/(?<stage>[^/]+)/\\d+/",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var anchor in document.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
        {
            var href = anchor.GetAttributeValue("href", string.Empty);
            var match = pattern.Match(href);
            if (!match.Success)
            {
                continue;
            }

            if (definition.StagePathPattern is not null &&
                !Regex.IsMatch(match.Groups["stage"].Value, definition.StagePathPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                continue;
            }

            paths.Add(ToRelativePath(href));
        }

        return paths.OrderBy(path => StageOrder(path)).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyCollection<BasketballProviderGame> ParseGames(
        string html,
        DateTime fetchedAtUtc,
        string revision,
        string sourcePath,
        int year,
        ICollection<string> warnings)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var stage = StageFromPath(sourcePath);
        var games = new List<BasketballProviderGame>();

        foreach (var anchor in document.DocumentNode.SelectNodes("//a[contains(@href, '/match/basketball/')]") ?? Enumerable.Empty<HtmlNode>())
        {
            var href = anchor.GetAttributeValue("href", string.Empty);
            var matchId = Regex.Match(href, @"/match/basketball/[^/]+/[^/]+/(?<id>\d+)/?", RegexOptions.IgnoreCase);
            var dateMatch = Regex.Match(href, @"/match/basketball/(?<date>\d{4}-\d{2}-\d{2})/", RegexOptions.IgnoreCase);
            if (!matchId.Success || !dateMatch.Success)
            {
                warnings.Add($"Global Sports Archive match link could not be parsed: {href}.");
                continue;
            }

            var sourceUrl = ToAbsoluteUrl(href);

            var homeNode = anchor.SelectSingleNode(".//div[contains(@class, 'gsa-c-match-c2')] | .//span[contains(@class, 'team_a_name')]");
            var awayNode = anchor.SelectSingleNode(".//div[contains(@class, 'gsa-c-match-c4')] | .//span[contains(@class, 'team_b_name')]");
            var scoreNode = anchor.SelectSingleNode(".//div[contains(@class, 'gsa-c-match-c3')] | .//span[contains(@class, 'match_score')]");
            var timeNode = anchor.SelectSingleNode(".//div[contains(@class, 'gsa-c-match-c1')] | .//*[contains(@class, 'match_time')]");
            var home = ParseTeam(homeNode);
            var away = ParseTeam(awayNode);
            if (home is null || away is null)
            {
                warnings.Add($"Global Sports Archive match {matchId.Groups["id"].Value} has incomplete team data.");
                continue;
            }

            short? homeScore = null;
            short? awayScore = null;
            var scoreText = HtmlEntity.DeEntitize(scoreNode?.InnerText ?? string.Empty).Trim();
            var scoreMatch = Regex.Match(scoreText, @"(?<home>\d+)\s*:\s*(?<away>\d+)");
            if (scoreMatch.Success &&
                short.TryParse(scoreMatch.Groups["home"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedHome) &&
                short.TryParse(scoreMatch.Groups["away"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAway))
            {
                homeScore = parsedHome;
                awayScore = parsedAway;
            }
            else
            {
                warnings.Add($"Global Sports Archive match {matchId.Groups["id"].Value} has no final score; retained as an unresolved fixture: {sourceUrl}.");
            }

            if (!DateTime.TryParseExact(
                    dateMatch.Groups["date"].Value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var gameDate))
            {
                warnings.Add($"Global Sports Archive match {matchId.Groups["id"].Value} has an invalid date.");
                continue;
            }

            var timeText = HtmlEntity.DeEntitize(timeNode?.InnerText ?? string.Empty).Trim();
            var gameDateTime = gameDate;
            if (DateTime.TryParseExact(timeText, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var gameTime))
            {
                gameDateTime = gameDate.Date.Add(gameTime.TimeOfDay);
            }
            else if (!string.IsNullOrWhiteSpace(timeText))
            {
                warnings.Add($"Global Sports Archive match {matchId.Groups["id"].Value} has an unrecognized time '{timeText}'.");
            }

            var status = homeScore.HasValue && awayScore.HasValue ? "finished" : "score_pending";
            var exclusionReason = homeScore.HasValue && awayScore.HasValue ? null : "source_missing_final_score";
            games.Add(new BasketballProviderGame(
                Source,
                $"gsa-{matchId.Groups["id"].Value}",
                gameDateTime,
                status,
                home.SourceTeamId,
                home.CanonicalName,
                away.SourceTeamId,
                away.CanonicalName,
                homeScore,
                awayScore,
                new BasketballProviderGameProvenance(sourceUrl, $"{year}-{year + 1}", fetchedAtUtc, ParserVersion, revision),
                exclusionReason,
                stage.Phase,
                stage.Round,
                home.CountryCode,
                away.CountryCode));
        }

        return games;
    }

    private static TeamIdentity? ParseTeam(HtmlNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var name = HtmlEntity.DeEntitize(
            node.SelectSingleNode(".//span[contains(@class, 'gsa-c-team_full') or contains(@class, 'team_full')]")?.InnerText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (TeamIdentities.TryGetValue(name, out var identity))
        {
            return identity;
        }

        var tla = HtmlEntity.DeEntitize(
            node.SelectSingleNode(".//span[contains(@class, 'gsa-c-team_tla') or contains(@class, 'team_tla')]")?.InnerText ?? string.Empty).Trim();

        var onClick = node.GetAttributeValue("onClick", string.Empty);
        var idMatch = Regex.Match(onClick, @"gotoUrl\((?<id>\d+),", RegexOptions.IgnoreCase);
        return new TeamIdentity(
            idMatch.Success ? $"GSA-{idMatch.Groups["id"].Value}" : $"GSA-{NormalizeName(name)}",
            name,
            tla.Length == 3 ? tla.ToUpperInvariant() : null);
    }

    private static (string Phase, string Round) StageFromPath(string path)
    {
        var stage = path.Trim('/').Split('/').Reverse().Skip(1).FirstOrDefault() ?? string.Empty;
        return stage.ToLowerInvariant() switch
        {
            "group-stage" => ("Group Stage", "Group Stage"),
            "final-round" => ("Final Round", "Final Round"),
            "semi-finals" => ("Final Phase", "Semi-finals"),
            "final" => ("Final Phase", "Final"),
            "3rd-place" => ("Classification Round", "3rd Place"),
            "5th-place" => ("Classification Round", "5th Place"),
            "7th-place" => ("Classification Round", "7th Place"),
            "9th-place" => ("Classification Round", "9th Place"),
            "11th-place" => ("Classification Round", "11th Place"),
            _ => ("Final Phase", stage)
        };
    }

    private static int StageOrder(string path)
    {
        var stage = path.Trim('/').Split('/').Reverse().Skip(1).FirstOrDefault() ?? string.Empty;
        return stage.ToLowerInvariant() switch
        {
            "final-round" => 0,
            "group-stage" => 0,
            "main-round" => 1,
            "classification-round" => 2,
            "second-stage" => 2,
            "classification-stage" => 3,
            "play-off-round" => 4,
            "quarter-finals" => 5,
            "15th-place" => 3,
            "13th-place" => 4,
            "11th-place" => 1,
            "9th-place" => 2,
            "7th-place" => 3,
            "5th-place" => 4,
            "semi-finals" => 5,
            "3rd-place" => 6,
            "final" => 7,
            _ => 8
        };
    }

    private static string ToRelativePath(string href)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery;
        }

        return href.StartsWith('/') ? href : $"/{href}";
    }

    private static string ToAbsoluteUrl(string href)
    {
        if (href.StartsWith("//", StringComparison.Ordinal))
        {
            return $"https:{href}";
        }

        return Uri.TryCreate(href, UriKind.Absolute, out var absolute) && !string.IsNullOrWhiteSpace(absolute.Scheme)
            ? href
            : $"https://globalsportsarchive.com{ToRelativePath(href)}";
    }

    private static string NormalizeName(string name)
        => new(name.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static ArchiveDefinition GetDefinition(BasketballProviderLeague league)
    {
        if (ArchiveDefinitions.TryGetValue(league.SourceLeagueId, out var domesticDefinition))
        {
            return domesticDefinition;
        }

        if (InternationalDefinitions.TryGetValue(league.SourceLeagueId, out var internationalDefinition))
        {
            return internationalDefinition;
        }

        return league.SourceLeagueId.ToLowerInvariant() switch
        {
            "fiba-asia-cup" => new(AsiaBasketSeedPath, "abc-championship|fiba-asia-championship|fiba-asia-cup"),
            "fiba-eurobasket" => new(EuroBasketSeedPath, "eurobasket"),
            _ => new(AfroBasketSeedPath, "fiba-africa-championship")
        };
    }

    private sealed record TeamIdentity(string SourceTeamId, string CanonicalName, string? CountryCode);

    private sealed record DomesticArchiveLeague(
        string SourceLeagueId,
        string Name,
        string? CountryCode,
        string SeedPath,
        string CompetitionSlugPattern);

    private sealed record ArchiveDefinition(
        string SeedPath,
        string CompetitionSlugPattern,
        IReadOnlyDictionary<int, string?>? SeasonSeedPaths = null,
        string? StagePathPattern = null);
}
