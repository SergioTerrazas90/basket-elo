using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Reads the official FIBA history pages. The archive page is server-rendered and
/// exposes stable game links, dates, phase labels and final scores in its HTML.
/// </summary>
public sealed class FibaBasketballDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "fiba";
    public const string ParserVersion = "fiba-history-html-v1";

    public static string? CountryCodeFromTeamId(string? sourceTeamId)
    {
        if (string.IsNullOrWhiteSpace(sourceTeamId))
        {
            return null;
        }

        var normalized = sourceTeamId.Trim().ToUpperInvariant();
        return normalized.Length is > 0 and <= 3 && normalized.All(char.IsLetter)
            ? normalized
            : null;
    }

    private static readonly IReadOnlyDictionary<string, (string Family, string Name, string? CountryCode)> Catalog =
        new Dictionary<string, (string, string, string?)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Europe:FIBA EuroBasket"] = ("208-fiba-eurobasket", "FIBA EuroBasket", "EUR"),
            ["Europe:FIBA European Champions Cup"] = ("112-fiba-mens-european-club-competitions-tier-1", "FIBA European Champions Cup / EuroLeague predecessor", "EUR"),
            ["Europe:FIBA Saporta Cup"] = ("212-fiba-mens-european-club-competitions-tier-2", "FIBA Saporta Cup / European Cup Winners' Cup predecessor", "EUR"),
            ["Europe:FIBA European Tier 2"] = ("212-fiba-mens-european-club-competitions-tier-2|post-saporta", "FIBA European Tier 2 / FIBA Europe League / EuroCup lineage", "EUR"),
            ["Europe:FIBA Korac Cup"] = ("164-eurocup-challenge", "FIBA Korac Cup / European Cup Radivoj Korac", "EUR"),
            // Keep the SuproLeague alias distinct from the Champions Cup alias even
            // though FIBA publishes both editions in the same history family.
            ["Europe:FIBA SuproLeague"] = ("112-fiba-mens-european-club-competitions-tier-1|suproleague", "FIBA SuproLeague", "EUR"),
            ["Europe:FIBA EuroBasket Pre-Qualifiers"] = ("204-fiba-eurobasket-pre-qualifiers", "FIBA EuroBasket Pre-Qualifiers", "EUR"),
            ["Europe:FIBA EuroBasket Qualifiers"] = ("205-fiba-eurobasket-qualifiers", "FIBA EuroBasket Qualifiers", "EUR"),
            ["Europe:EuroBasket Qualifiers"] = ("205-fiba-eurobasket-qualifiers", "FIBA EuroBasket Qualifiers", "EUR"),
            ["Europe:FIBA EuroBasket Division B"] = ("206-fiba-eurobasket-division-b", "FIBA EuroBasket Division B", "EUR"),
            ["Africa:FIBA AfroBasket"] = ("179-fiba-afrobasket", "FIBA AfroBasket", "AFR"),
            ["Africa:FIBA AfroBasket Qualifiers"] = ("178-fiba-afrobasket-qualifiers", "FIBA AfroBasket Qualifiers", "AFR"),
            ["Africa:FIBA AfroBasket Pre-Qualifiers"] = ("178-fiba-afrobasket-qualifiers|pre-qualifiers", "FIBA AfroBasket Pre-Qualifiers", "AFR"),
            ["Asia:FIBA Asia Cup"] = ("195-fiba-asia-cup", "FIBA Asia Cup", "ASI"),
            ["Americas:FIBA AmeriCup Qualifiers"] = ("183-fiba-americup-qualifiers", "FIBA AmeriCup Qualifiers", "AME"),
            ["Americas:FIBA AmeriCup Pre-Qualifiers"] = ("182-fiba-americup-pre-qualifiers", "FIBA AmeriCup Pre-Qualifiers", "AME"),
            ["Americas:FIBA Americas Championship"] = ("184-fiba-americup", "FIBA Americas Championship", "AME"),
            ["Americas:Centrobasket Championship"] = ("122-centrobasket-championship", "Centrobasket Championship", "AME"),
            ["Americas:COCABA Championship"] = ("113-cbc-championship|cocaba", "COCABA Championship", "AME"),
            ["Americas:South American Championship"] = ("327-south-american-championship", "South American Championship", "AME"),
            ["Americas:Caribbean Basketball Championship"] = ("113-cbc-championship|caribbean", "Caribbean Basketball Championship", "AME"),
            ["Asia:FIBA Asia Cup Qualifiers"] = ("192-fiba-asia-cup-qualifiers", "FIBA Asia Cup Qualifiers", "ASI"),
            ["Oceania:FIBA Oceania Championship"] = ("216-fiba-oceania-championship", "FIBA Oceania Championship", "OCE"),
            ["World:FIBA Basketball World Cup"] = ("201-fiba-basketball-world-cup", "FIBA Basketball World Cup", "WOR"),
            ["World:FIBA Basketball World Cup Qualifiers"] = ("200-fiba-basketball-world-cup-qualifiers", "FIBA Basketball World Cup Qualifiers", "WOR"),
            ["World:FIBA Olympic Qualifying Tournament"] = ("219-fiba-olympic-qualifying-tournament", "FIBA Olympic Qualifying Tournament", "WOR"),
            ["World:FIBA Olympic Pre-Qualifying Tournament"] = ("218-fiba-olympic-pre-qualifying-tournament", "FIBA Olympic Pre-Qualifying Tournament", "WOR"),
            ["World:Olympic Qualifying Tournament"] = ("324-olympic-qualifying-tournament", "Olympic Qualifying Tournament", "WOR"),
            ["World:FIBA Men's Olympic Basketball Tournament"] = ("320-mens-olympic-basketball-tournament", "FIBA Men's Olympic Basketball Tournament", "WOR")
        };

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        Catalog.TryGetValue($"{country}:{leagueName}", out var entry);
        return Task.FromResult<BasketballProviderLeague?>(
            entry.Family is null
                ? null
                : new BasketballProviderLeague(Source, entry.Family, entry.Name, entry.CountryCode, "year"));
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var year = ParseStartYear(season);
        var warnings = new List<string>();
        var (historyFamily, variant) = ParseFamily(league.SourceLeagueId);
        var historyPath = $"/en/history/{historyFamily}";
        var archiveYear = IsEuropeanClubCompetition(historyFamily) ? year + 1 : year;
        var editionPaths = KnownEditionPaths(historyFamily, variant, year);
        if (editionPaths is null)
        {
            var history = await GetPageAsync(historyPath, context, cancellationToken);
            editionPaths = FindEditionPaths(history.Content, historyFamily, archiveYear);
        }

        if (editionPaths.Count == 0)
        {
            warnings.Add($"FIBA edition {year} was not found in {historyPath}.");
            return ([], false, warnings);
        }

        var games = new List<BasketballProviderGame>();
        foreach (var editionPath in editionPaths)
        {
            if (!context.CanUseRequest())
            {
                warnings.Add($"FIBA request budget reached before edition {editionPath} could be fetched.");
                break;
            }

            var gamesPath = $"{editionPath}/games";
            try
            {
                var gamesPage = await GetPageAsync(gamesPath, context, cancellationToken);
                var parsedGames = ParseGames(gamesPage.Content, gamesPage.FetchedAtUtc, gamesPage.Revision, gamesPath, archiveYear, warnings);
                if (IsEuroBasket2005Event(editionPath))
                {
                    parsedGames = parsedGames
                        .Where(IsEuroBasket2005QualificationGame)
                        .ToArray();
                }

                games.AddRange(parsedGames);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                warnings.Add($"FIBA edition page timed out: {gamesPath}.");
            }
            catch (HttpRequestException exception)
            {
                warnings.Add($"FIBA edition page could not be fetched: {gamesPath} ({exception.StatusCode?.ToString() ?? exception.Message}).");
            }
        }

        // Older Champions Cup editions can expose unresolved cards even when
        // the archive has a complete serialized game payload. Use Wikipedia or
        // Todor66 only for genuinely sparse editions; a rich embedded payload
        // is the authoritative source for the game-level records.
        var hasUnresolvedTeamCards = warnings.Any(warning =>
            warning.Contains("unresolved TBD/TBC", StringComparison.OrdinalIgnoreCase));
        if (IsEuropeanClubCompetition(historyFamily) &&
            !IsEuropeanPostSaportaTierTwo(historyFamily, variant) &&
            (games.Count < 50 || IsEuropeanSaportaCup(historyFamily, variant) && hasUnresolvedTeamCards))
        {
            var wikipediaLanguages = IsEuropeanSaportaCup(historyFamily, variant) || IsEuropeanKoracCup(historyFamily)
                ? new[] { "en" }
                : year >= 1996 ? new[] { "en", "es" } : new[] { "es" };
            var wikipediaGames = new List<BasketballProviderGame>();
            foreach (var language in wikipediaLanguages)
            {
                var languageGames = await GetWikipediaGamesAsync(
                    season,
                    language,
                    IsEuropeanSaportaCup(historyFamily, variant),
                    IsEuropeanKoracCup(historyFamily),
                    context,
                    cancellationToken,
                    warnings);
                if (languageGames.Count > wikipediaGames.Count)
                {
                    wikipediaGames = languageGames.ToList();
                }
            }

            if (year <= 1990 && !IsEuropeanSaportaCup(historyFamily, variant) && !IsEuropeanKoracCup(historyFamily))
            {
                var todorGames = await GetTodor66GamesAsync(
                    season,
                    context,
                    cancellationToken,
                    warnings);
                if (todorGames.Count > wikipediaGames.Count)
                {
                    wikipediaGames = todorGames.ToList();
                }
            }

            if (wikipediaGames.Count > games.Count)
            {
                warnings.Add($"FIBA archive was sparse or incomplete ({games.Count} games); used the richest external score table ({wikipediaGames.Count} games).");
                games = wikipediaGames;
            }
            else if (wikipediaGames.Count > 0 && hasUnresolvedTeamCards && year >= 1996)
            {
                var officialKnockoutGames = games.Where(game => !IsGroupStage(game)).ToList();
                games = wikipediaGames.Concat(officialKnockoutGames)
                    .GroupBy(game => game.SourceGameId, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToList();
                warnings.Add("FIBA archive had unresolved group-stage cards; combined the external score table with official knockout records.");
            }
        }

        return (games, false, warnings);
    }

    private async Task<IReadOnlyCollection<BasketballProviderGame>> GetTodor66GamesAsync(
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken,
        ICollection<string> warnings)
    {
        var startYear = ParseStartYear(season);
        var archiveYear = startYear + 1;
        var title = $"Men Basketball European Champions Cup {archiveYear}";
        if (!context.CanUseRequest())
        {
            warnings.Add($"Todor66 request budget reached before {title} could be fetched.");
            return [];
        }

        context.ConsumeRequest();
        var pageUrl = $"http://todor66.com/basketball/Eurocups/Men_CC_{archiveYear}.html";
        try
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await httpClient.GetAsync(pageUrl, requestTimeout.Token);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)))[..16];
            return WikipediaFibaEuropeanChampionsCupParser.ParseTodor66Games(html, season, pageUrl, DateTime.UtcNow, revision, warnings);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            warnings.Add($"Todor66 edition page timed out: {title}.");
        }
        catch (HttpRequestException exception)
        {
            warnings.Add($"Todor66 edition page could not be fetched: {title} ({exception.StatusCode?.ToString() ?? exception.Message}).");
        }
        return [];
    }

    private async Task<IReadOnlyCollection<BasketballProviderGame>> GetWikipediaGamesAsync(
        string season,
        string language,
        bool saportaCup,
        bool koracCup,
        BackfillExecutionContext context,
        CancellationToken cancellationToken,
        ICollection<string> warnings)
    {
        var startYear = ParseStartYear(season);
        var title = language.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? koracCup
                ? WikipediaFibaEuropeanChampionsCupParser.KoracWikipediaPageTitle(startYear)
                : saportaCup
                ? WikipediaFibaEuropeanChampionsCupParser.SaportaEnglishPageTitle(startYear)
                : WikipediaFibaEuropeanChampionsCupParser.EnglishPageTitle(startYear)
            : WikipediaFibaEuropeanChampionsCupParser.PageTitle(startYear);
        if (!context.CanUseRequest())
        {
            warnings.Add($"Wikipedia request budget reached before {title} could be fetched.");
            return [];
        }

        context.ConsumeRequest();
        var pagePath = $"/w/index.php?title={Uri.EscapeDataString(title)}&action=raw";
        try
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            var host = language.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en.wikipedia.org" : "es.wikipedia.org";
            using var response = await httpClient.GetAsync("https://" + host + pagePath, requestTimeout.Token);
            response.EnsureSuccessStatusCode();
            var wikitext = await response.Content.ReadAsStringAsync(cancellationToken);
            if (wikitext.Containsïô¶‰žËkºwµç@€€€€€€€ì4(€€€€€€€€€€€€€€€Ý…É¹¥¹Ì¹‘ ‰%	…µ”íÍ±ÕA…ÉÑÍlÁuô•áÁ½Í•…¸¥¹Ù…±¥ÁÉ”´ÄäÀÀ‘…Ñ”ìÕÍ•Ñ¡”•‘¥Ñ¥½¸ÍÑ…ÉÐ‘…Ñ”í™…±±‰…­…Ñ”éåååäµ54µ‘‘ô¸ˆ¤ì4(€€€€€€€€€€€€€€€…µ•…Ñ”€ô™…±±‰…­…Ñ”¹Y…±Õ”ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€Ù…ÈÍ½É•Ì€ô…É‘Q•…µÌ¥Ì¹½Ð¹Õ±°€˜˜Í±ÕA…ÉÑÌ¹1•¹Ñ €ð€Ì4(€€€€€€€€€€€€€€€€ü¹•ÜÍ¡½ÉÐýmtì…É‘Q•…µÍlÁt¹M½É”°…É‘Q•…µÍlÅt¹M½É”ô4(€€€€€€€€€€€€€€€€èA…ÉÍ•M½É•Ì¡…É¤¹Q½ÉÉ…ä ¤ì4(€€€€€€€€€€€Ù…ÈÁ¡…Í•1…‰•°€ô¥¹‘A¡…Í•1…‰•°¡…É¤ì4(€€€€€€€€€€€Ù…ÈÁ¡…Í•A…ÉÑÌ€ôÁ¡…Í•1…‰•°ü¹MÁ±¥Ð Ÿ
Üœ°€È°MÑÉ¥¹MÁ±¥Ñ=ÁÑ¥½¹Ì¹QÉ¥µ¹ÑÉ¥•Ì¤ì4(€€€€€€€€€€€Ù…ÈÍÑ…ÑÕÌ€ô¥¹‘MÑ…ÑÕÌ¡…É°Í½É•Ì¤ì4(4(€€€€€€€€€€€…µ•Ì¹‘¡¹•Ü	…Í­•Ñ‰…±±AÉ½Ù¥‘•É…µ” 4(€€€€€€€€€€€€€€€M½ÕÉ”°4(€€€€€€€€€€€€€€€Í±ÕA…ÉÑÍlÁt°4(€€€€€€€€€€€€€€€…µ•…Ñ”¹Y…±Õ”°4(€€€€€€€€€€€€€€€ÍÑ…ÑÕÌ°4(€€€€€€€€€€€€€€€¡½µ•½‘”°4(€€€€€€€€€€€€€€€¡½µ•½‘”°4(€€€€€€€€€€€€€€€…Ý…å½‘”°4(€€€€€€€€€€€€€€€…Ý…å½‘”°4(€€€€€€€€€€€€€€€Í½É•Ì¹±•µ•¹ÑÑ=É•™…Õ±Ð À¤°4(€€€€€€€€€€€€€€€Í½É•Ì¹±•µ•¹ÑÑ=É•™…Õ±Ð Ä¤°4(€€€€€€€€€€€€€€€¹•Ü	…Í­•Ñ‰…±±AÉ½Ù¥‘•É…µ•AÉ½Ù•¹…¹” 4(€€€€€€€€€€€€€€€€€€€	Õ¥±‘‰Í½±ÕÑ•UÉ°¡…µ•A…Ñ ¤°4(€€€€€€€€€€€€€€€€€€€€‰íå•…ÉôéíÍ½ÕÉ•A…Ñ¡ôˆ°4(€€€€€€€€€€€€€€€€€€€™•Ñ¡•‘ÑUÑŒ°4(€€€€€€€€€€€€€€€€€€€A…ÉÍ•ÉY•ÉÍ¥½¸°4(€€€€€€€€€€€€€€€€€€€É•Ù¥Í¥½¸¤°4(€€€€€€€€€€€€€€€¹Õ±°°4(€€€€€€€€€€€€€€€Á¡…Í•A…ÉÑÌü¹±•µ•¹ÑÑ=É•™…Õ±Ð À¤°4(€€€€€€€€€€€€€€€Á¡…Í•A…ÉÑÌü¹±•µ•¹ÑÑ=É•™…Õ±Ð Ä¤°4(€€€€€€€€€€€€€€€½Õ¹ÑÉå½‘•É½µQ•…µ%¡¡½µ•½‘”¤°4(€€€€€€€€€€€€€€€½Õ¹ÑÉå½‘•É½µQ•…µ%¡…Ý…å½‘”¤¤¤ì4(€€€€€€€ô4(4(€€€€€€€¥˜€¡µ¥ÍÍ¥¹MÑ…‰±•1¥¹­½Õ¹Ð€ø€À¤4(€€€€€€€ì4(€€€€€€€€€€€Ý…É¹¥¹Ì¹‘ ‰%	•‘¥Ñ¥½¸•áÁ½Í•íµ¥ÍÍ¥¹MÑ…‰±•1¥¹­½Õ¹Ñô…µ”…É‘ÌÝ¥Ñ¡½ÕÐÍÑ…‰±”…µ”±¥¹­ÌìÑ¡½Í”É•½É‘ÌÝ•É”¹½ÐÍå¹Ñ¡•Í¥é•¸ˆ¤ì4(€€€€€€€ô4(4(€€€€€€€¥˜€¡µ¥ÍÍ¥¹Q•…µ%‘•¹Ñ¥Ñå½Õ¹Ð€ø€À¤4(€€€€€€€ì4(€€€€€€€€€€€Ý…É¹¥¹Ì¹‘ ‰%	•‘¥Ñ¥½¸•áÁ½Í•íµ¥ÍÍ¥¹Q•…µ%‘•¹Ñ¥Ñå½Õ¹Ñô…µ”…É‘ÌÝ¥Ñ¡½ÕÐ‰½Ñ Ñ•…´¥‘•¹Ñ¥Ñ¥•ÌìÑ¡½Í”É•½É‘ÌÝ•É”¹½ÐÍå¹Ñ¡•Í¥é•¸ˆ¤ì4(€€€€€€€ô4(4(€€€€€€€¥˜€¡Õ¹É•Í½±Ù•‘Q•…µ%‘•¹Ñ¥Ñå½Õ¹Ð€ø€À¤4(€€€€€€€ì4(€€€€€€€€€€€Ý…É¹¥¹Ì¹‘ ‰%	•‘¥Ñ¥½¸•áÁ½Í•íÕ¹É•Í½±Ù•‘Q•…µ%‘•¹Ñ¥Ñå½Õ¹Ñô…µ”…É‘ÌÝ¥Ñ Õ¹É•Í½±Ù•Q	½Q	Ñ•…´¥‘•¹Ñ¥Ñ¥•ÌìÑ¡½Í”É•½É‘ÌÝ•É”Í­¥ÁÁ•¸ˆ¤ì4(€€€€€€€ô4(4(€€€€€€€€¼¼%	ÌÕÉÉ•¹Ð¡¥ÍÑ½ÉäÁ…•ÌÉ•¹‘•È½¹±äÑ¡”™¥ÉÍÐÍ•±•Ñ•É½Õ¹…Ì4(€€€€€€€€¼¼…µ”…É‘Ì°‰ÕÐ½™Ñ•¸•µ‰••Ù•ÉäÉ½Õ¹¥¸Ñ¡”Á…”ÌÍ•É¥…±¥é•‘…Ñ„¸4(€€€€€€€€¼¼Q¡¥Ì¥Ì•ÍÁ•¥…±±ä¥µÁ½ÉÑ…¹Ð™½È™É½	…Í­•Ð€ÈÀÄÜ°Ý¡•É”Ñ¡”Ù¥Í¥‰±”4(€€€€€€€€¼¼…É‘Ì…É”i½¹”€ÄÝ¡¥±”Ñ¡”•µ‰•‘‘•Á…å±½……±Í¼½¹Ñ…¥¹Ìi½¹•Ì€È´Ü°4(€€€€€€€€¼¼Á±…å½™™Ì…¹…‘‘¥Ñ¥½¹…°ÅÕ…±¥™¥•ÉÌ¸AÉ•™•ÈÑ¡”•µ‰•‘‘•É•½ÉÝ¡•¸4(€€€€€€€€¼¼‰½Ñ ™½ÉµÌ½¹Ñ…¥¸Ñ¡”Í…µ”…µ”‰•…ÕÍ”¥Ð…ÉÉ¥•ÌÍÑ…‰±”¡¥ÍÑ½É¥Œ4(€€€€€€€€¼¼Ñ•…´%Ì…¹É½Õ¹µ•Ñ…‘…Ñ„¸4(€€€€€€€Ù…È•µ‰•‘‘•‘…µ•Ì€ôA…ÉÍ•µ‰•‘‘•‘…µ•Ì 4(€€€€€€€€€€€¡Ñµ°°4(€€€€€€€€€€€™•Ñ¡•‘ÑUÑŒ°4(€€€€€€€€€€€É•Ù¥Í¥½¸°4(€€€€€€€€€€€Í½ÕÉ•A…Ñ °4(€€€€€€€€€€€å•…È°4(€€€€€€€€€€€Ý…É¹¥¹Ì°4(€€€€€€€€€€€Ý…É¹%™µÁÑäè™…±Í”¤ì4(€€€€€€€¥˜€¡•µ‰•‘‘•‘…µ•Ì¹½Õ¹Ð€ø€À¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…È…µ•Í	å%€ô…µ•Ì¹Q½¥Ñ¥½¹…Éä¡…µ”€ôø…µ”¹M½ÕÉ•…µ•%°MÑÉ¥¹½µÁ…É•È¹=É‘¥¹…°¤ì4(€€€€€€€€€€€™½É•… €¡Ù…È•µ‰•‘‘•‘…µ”¥¸•µ‰•‘‘•‘…µ•Ì¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€…µ•Í	å%‘m•µ‰•‘‘•‘…µ”¹M½ÕÉ•…µ•%‘t€ô•µ‰•‘‘•‘…µ”ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€…µ•Ì€ô…µ•Í	å%¹Y…±Õ•Ì¹Q½1¥ÍÐ ¤ì4(€€€€€€€ô4(4(€€€€€€€¥˜€¡…µ•Ì¹½Õ¹Ð€ôô€À¤4(€€€€€€€ì4(€€€€€€€€€€€Ý…É¹¥¹Ì¹‘ ‰%	Á…”•áÁ½Í•¹¼É•Í½±Ù…‰±”…µ”µ±•Ù•°É•½É‘Ìì¹¼…µ•ÌÝ•É”Íå¹Ñ¡•Í¥é•¸ˆ¤ì4(€€€€€€€ô4(4(€€€€€€€É•ÑÕÉ¸…µ•Ìì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ%I•…‘=¹±å1¥ÍÐð¡ÍÑÉ¥¹œ½‘”°Í¡½ÉÐüM½É”¤øüA…ÉÍ•…É‘Q•…µÌ¡!Ñµ±9½‘”…É¤4(€€€ì4(€€€€€€€Ù…ÈÑ•…µ9½‘•Ì€ô…É¹M•±•Ñ9½‘•Ì ˆ¸¼½‘¥Ùm½¹Ñ…¥¹Ì¡±…ÍÌ°€Ý„ÀÅ…Ù´œ¥tˆ¤ü¹Q½1¥ÍÐ ¤€üümtì4(€€€€€€€Ù…ÈÑ•…µÌ€ô¹•Ü1¥ÍÐð¡ÍÑÉ¥¹œ½‘”°Í¡½ÉÐüM½É”¤ø ¤ì4(€€€€€€€™½É•… €¡Ù…ÈÑ•…µ9½‘”¥¸Ñ•…µ9½‘•Ì¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…È½‘”€ôÑ•…µ9½‘”4(€€€€€€€€€€€€€€€€¹M•±•ÑM¥¹±•9½‘” ˆ¸¼½‘¥Ùm½¹Ñ…¥¹Ì¡±…ÍÌ°€Ý„ÀÅ…ÙÄœ¥tˆ¤¥Ììô½‘•9½‘”4(€€€€€€€€€€€€€€€€ü9½Éµ…±¥é”¡½‘•9½‘”¹%¹¹•ÉQ•áÐ¤¹Q½UÁÁ•É%¹Ù…É¥…¹Ð ¤4(€€€€€€€€€€€€€€€€èÍÑÉ¥¹œ¹µÁÑäì4(€€€€€€€€€€€Ù…ÈÍ½É•Q•áÐ€ôÑ•…µ9½‘”4(€€€€€€€€€€€€€€€€¹M•±•ÑM¥¹±•9½‘” ˆ¸¼½‘¥Ùm½¹Ñ…¥¹Ì¡±…ÍÌ°€Ý„ÀÅ…Ù¼œ¥tˆ¤¥ÌìôÍ½É•9½‘”4(€€€€€€€€€€€€€€€€ü9½Éµ…±¥é”¡Í½É•9½‘”¹%¹¹•ÉQ•áÐ¤4(€€€€€€€€€€€€€€€€èÍÑÉ¥¹œ¹µÁÑäì4(4(€€€€€€€€€€€¥˜€¡ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡½‘”¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€Ù…ÈÑ½­•¹Ì€ô9½Éµ…±¥é”¡Ñ•…µ9½‘”¹%¹¹•ÉQ•áÐ¤4(€€€€€€€€€€€€€€€€€€€€¹MÁ±¥Ð œ€œ°MÑÉ¥¹MÁ±¥Ñ=ÁÑ¥½¹Ì¹I•µ½Ù•µÁÑå¹ÑÉ¥•Ì¤ì4(€€€€€€€€€€€€€€€¥˜€¡Ñ½­•¹Ì¹1•¹Ñ €ôô€À¤4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€½¹Ñ¥¹Õ”ì4(€€€€€€€€€€€€€€€ô4(4(€€€€€€€€€€€€€€€Ù…ÈÉ•Á•…Ñ•‘½‘”€ôI••à¹5…Ñ  4(€€€€€€€€€€€€€€€€€€€ÍÑÉ¥¹œ¹½¹…Ð¡Ñ½­•¹Ì¤°4(€€€€€€€€€€€€€€€€€€€€‰x üñ½‘”ùmµi„µèÀ´åt¬¥qq¬ñ½‘”ø üñÍ½É”ùqq¬¤ˆ°4(€€€€€€€€€€€€€€€€€€€I••á=ÁÑ¥½¹Ì¹%¹½É•…Í”¤ì4(€€€€€€€€€€€€€€€½‘”€ôÉ•Á•…Ñ•‘½‘”¹MÕ•ÍÌ4(€€€€€€€€€€€€€€€€€€€€üÉ•Á•…Ñ•‘½‘”¹É½ÕÁÍl‰½‘”‰t¹Y…±Õ”¹Q½UÁÁ•É%¹Ù…É¥…¹Ð ¤4(€€€€€€€€€€€€€€€€€€€€èÑ½­•¹ÍlÁt¹Q½UÁÁ•É%¹Ù…É¥…¹Ð ¤ì4(€€€€€€€€€€€€€€€Í½É•Q•áÐ€ôÉ•Á•…Ñ•‘½‘”¹MÕ•ÍÌ€üÉ•Á•…Ñ•‘½‘”¹É½ÕÁÍl‰Í½É”‰t¹Y…±Õ”€èÑ½­•¹ÍmxÅtì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€Ù…ÈÍ½É”€ôÍ¡½ÉÐ¹QÉåA…ÉÍ”¡Í½É•Q•áÐ°½ÕÐÙ…ÈÁ…ÉÍ•‘M½É”¤€ü€¡Í¡½ÉÐü¥Á…ÉÍ•‘M½É”€è¹Õ±°ì4(€€€€€€€€€€€Ñ•…µÌ¹‘ ¡½‘”°Í½É”¤¤ì4(€€€€€€€ô4(4(€€€€€€€É•ÑÕÉ¸Ñ•…µÌ¹½Õ¹Ð€øô€È€üÑ•…µÌ¹Q…­” È¤¹Q½ÉÉ…ä ¤€è¹Õ±°ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ‰½½°%ÍU¹É•Í½±Ù•‘Q•…µ½‘”¡ÍÑÉ¥¹œ½‘”¤4(€€€€€€€€ôø½‘”¹ÅÕ…±Ì ‰Q	ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤ñð4(€€€€€€€€€€½‘”¹ÅÕ…±Ì ‰Q	ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤ì4(4(€€€ÁÉ¥Ù…Ñ”%I•…‘=¹±å½±±•Ñ¥½¸ñ	…Í­•Ñ‰…±±AÉ½Ù¥‘•É…µ”øA…ÉÍ•µ‰•‘‘•‘…µ•Ì 4(€€€€€€€ÍÑÉ¥¹œ¡Ñµ°°4(€€€€€€€…Ñ•Q¥µ”™•Ñ¡•‘ÑUÑŒ°4(€€€€€€€ÍÑÉ¥¹œÉ•Ù¥Í¥½¸°4(€€€€€€€ÍÑÉ¥¹œÍ½ÕÉ•A…Ñ °4(€€€€€€€¥¹Ðå•…È°4(€€€€€€€%½±±•Ñ¥½¸ñÍÑÉ¥¹œøÝ…É¹¥¹Ì°4(€€€€€€€‰½½°Ý…É¹%™µÁÑä€ôÑÉÕ”¤4(€€€ì4(€€€€€€€Ù…È¹½Éµ…±¥é•‘!Ñµ°€ô¡Ñµ°¹I•Á±…” ‰qqpˆˆ°€‰pˆˆ¤ì4(€€€€€€€Ù…È…µ•Ì€ô¹•Ü1¥ÍÐñ	…Í­•Ñ‰…±±AÉ½Ù¥‘•É…µ”ø ¤ì4(4(€€€€€€€Ù…È…µ•5…Ñ¡•Ì€ôI••à¹5…Ñ¡•Ì¡¹½Éµ…±¥é•‘!Ñµ°°€‰p‰…µ•%‘pˆè üñ¥ùqq¬¤ˆ°I••á=ÁÑ¥½¹Ì¹%¹½É•…Í”¤ì4(€€€€€€€™½È€¡Ù…È¥¹‘•à€ô€Àì¥¹‘•à€ð…µ•5…Ñ¡•Ì¹½Õ¹Ðì¥¹‘•à¬¬¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…È…µ•5…Ñ €ô…µ•5…Ñ¡•Ím¥¹‘•átì4(€€€€€€€€€€€Ù…ÈÉ•½É‘¹€ô¥¹‘•à€¬€Ä€ð…µ•5…Ñ¡•Ì¹½Õ¹Ð4(€€€€€€€€€€€€€€€€ü…µ•5…Ñ¡•Ím¥¹‘•à€¬€Åt¹%¹‘•à4(€€€€€€€€€€€€€€€€è¹½Éµ…±¥é•‘!Ñµ°¹1•¹Ñ ì4(€€€€€€€€€€€Ù…ÈÉ•½É€ô¹½Éµ…±¥é•‘!Ñµ±m…µ•5…Ñ ¹%¹‘•à¸¹É•½É‘¹‘tì4(€€€€€€€€€€€Ù…È¡½µ”€ôA…ÉÍ•µ‰•‘‘•‘Q•…´¡É•½É°€‰Ñ•…µˆ¤ì4(€€€€€€€€€€€Ù…È…Ý…ä€ôA…ÉÍ•µ‰•‘‘•‘Q•…´¡É•½É°€‰Ñ•…µˆ¤ì4(€€€€€€€€€€€Ù…ÈÍ½É•Ì€ôI••à¹5…Ñ ¡É•½É°€‰p‰Ñ•…µM½É•pˆè üñ¡½µ”ø´ýqq­ñ¹Õ±°¤¸¨ýp‰Ñ•…µ	M½É•pˆè üñ…Ý…äø´ýqq­ñ¹Õ±°¤ˆ°I••á=ÁÑ¥½¹Ì¹M¥¹±•±¥¹”ðI••á=ÁÑ¥½¹Ì¹%¹½É•…Í”¤ì4(€€€€€€€€€€€Ù…È‘…Ñ•5…Ñ €ôI••à¹5…Ñ ¡É•½É°€‰p‰…µ•…Ñ•Q¥µ•UQpˆépˆ üñ‘…Ñ”ùmyp‰t¬¥pˆˆ°I••á=ÁÑ¥½¹Ì¹%¹½É•…Í”¤ì4(€€€€€€€€€€€Ù…ÈÉ½Õ¹€ôI••à¹5…Ñ ¡É•½É°€‰p‰É½Õ¹‘pˆéqqì¸¨ýp‰É½Õ¹‘½‘•pˆépˆ üñ½‘”ùmyp‰t¨¥pˆ¸¨ýp‰É½Õ¹‘9…µ•pˆépˆ üñ¹…µ”ùmyp‰t¨¥pˆˆ°I••á=ÁÑ¥½¹Ì¹M¥¹±•±¥¹”ðI••á=ÁÑ¥½¹Ì¹%¹½É•…Í”¤ì4(€€€€€€€€€€€¥˜€¡¡½µ”¥Ì¹Õ±°ñð…Ý…ä¥Ì¹Õ±°ñð€…Í½É•Ì¹MÕ•ÍÌñð€…‘…Ñ•5…Ñ ¹MÕ•ÍÌñð4(€€€€€€€€€€€€€€€€……Ñ•Q¥µ”¹QÉåA…ÉÍ”¡‘…Ñ•5…Ñ ¹É½ÕÁÍl‰‘…Ñ”‰t¹Y…±Õ”°Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”°…Ñ•Q¥µ•MÑå±•Ì¹ÍÍÕµ•U¹¥Ù•ÉÍ…°°½ÕÐÙ…È‘…Ñ”¤ñð4(€€€€€€€€€€€€€€€‘…Ñ”¹e•…È€ð€ÄäÀÀ¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€½¹Ñ¥¹Õ”ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€…µ•Ì¹‘¡¹•Ü	…Í­•Ñ‰…±±AÉ½Ù¥‘•É…µ” 4(€€€€€€€€€€€€€€€M½ÕÉ”°4(€€€€€€€€€€€€€€€…µ•5…Ñ ¹É½ÕÁÍl‰¥‰t¹Y…±Õ”°4(€€€€€€€€€€€€€€€‘…Ñ”¹Q½U¹¥Ù•ÉÍ…±Q¥µ” ¤°4(€€€€€€€€€€€€€€€Í½É•Ì¹É½ÕÁÍl‰¡½µ”‰t¹Y…±Õ”€ôô€‰¹Õ±°ˆ€ü€‰Í¡•‘Õ±•ˆ€è€‰™¥¹…°ˆ°4(€€€€€€€€€€€€€€€¡½µ”¹M½ÕÉ•%°4(€€€€€€€€€€€€€€€¡½µ”¹9…µ”°4(€€€€€€€€€€€€€€€…Ý…ä¹M½ÕÉ•%°4(€€€€€€€€€€€€€€€…Ý…ä¹9…µ”°4(€€€€€€€€€€€€€€€A…ÉÍ•µ‰•‘‘•‘M½É”¡Í½É•Ì¹É½ÕÁÍl‰¡½µ”‰t¹Y…±Õ”¤°4(€€€€€€€€€€€€€€€A…ÉÍ•µ‰•‘‘•‘M½É”¡Í½É•Ì¹É½ÕÁÍl‰…Ý…ä‰t¹Y…±Õ”¤°4(€€€€€€€€€€€€€€€¹•Ü	…Í­•Ñ‰…±±AÉ½Ù¥‘•É…µ•AÉ½Ù•¹…¹” 4(€€€€€€€€€€€€€€€€€€€	Õ¥±‘‰Í½±ÕÑ•UÉ°¡	Õ¥±‘µ‰•‘‘•‘…µ•A…Ñ  4(€€€€€€€€€€€€€€€€€€€€€€€Í½ÕÉ•A…Ñ °4(€€€€€€€€€€€€€€€€€€€€€€€…µ•5…Ñ ¹É½ÕÁÍl‰¥‰t¹Y…±Õ”°4(€€€€€€€€€€€€€€€€€€€€€€€¡½µ”¹M½ÕÉ•%°4(€€€€€€€€€€€€€€€€€€€€€€€…Ý…ä¹M½ÕÉ•%¤¤°4(€€€€€€€€€€€€€€€€€€€€‰íå•…ÉôéíÍ½ÕÉ•A…Ñ¡ôˆ°4(€€€€€€€€€€€€€€€€€€€™•Ñ¡•‘ÑUÑŒ°4(€€€€€€€€€€€€€€€€€€€A…ÉÍ•ÉY•ÉÍ¥½¸°4(€€€€€€€€€€€€€€€€€€€É•Ù¥Í¥½¸¤°4(€€€€€€€€€€€€€€€¹Õ±°°4(€€€€€€€€€€€€€€€É½Õ¹¹MÕ•ÍÌ€üÉ½Õ¹¹É½ÕÁÍl‰¹…µ”‰t¹Y…±Õ”€è¹Õ±°°4(€€€€€€€€€€€€€€€É½Õ¹¹MÕ•ÍÌ€üÉ½Õ¹¹É½ÕÁÍl‰½‘”‰t¹Y…±Õ”€è¹Õ±°°4(€€€€€€€€€€€€€€€½Õ¹ÑÉå½‘•É½µQ•…µ%¡¡½µ”¹M½ÕÉ•%¤°4(€€€€€€€€€€€€€€€½Õ¹ÑÉå½‘•É½µQ•…µ%¡…Ý…ä¹M½ÕÉ•%¤¤¤ì4(€€€€€€€ô4(4(€€€€€€€¥˜€¡…µ•Ì¹½Õ¹Ð€ôô€À€˜˜Ý…É¹%™µÁÑä¤4(€€€€€€€ì4(€€€€€€€€€€€Ý…É¹¥¹Ì¹‘ ‰%	Á…”½¹Ñ…¥¹•¹¼Á…ÉÍ•…‰±”…µ”…É‘Ì½È•µ‰•‘‘•…µ”É•½É‘Ì¸ˆ¤ì4(€€€€€€€ô4(4(€€€€€€€É•ÑÕÉ¸…µ•Ìì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œµ‰•‘‘•‘Q•…´üA…ÉÍ•µ‰•‘‘•‘Q•…´¡ÍÑÉ¥¹œÉ•½É°ÍÑÉ¥¹œÁÉ½Á•ÉÑå9…µ”¤4(€€€ì4(€€€€€€€Ù…È¹•áÑAÉ½Á•ÉÑä€ôÁÉ½Á•ÉÑå9…µ”¹ÅÕ…±Ì ‰Ñ•…µˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤4(€€€€€€€€€€€€ü€‰Ñ•…µˆ4(€€€€€€€€€€€€è€‰Ñ•…µM½É”ˆì4(€€€€€€€Ù…Èµ…Ñ €ôI••à¹5…Ñ  4(€€€€€€€€€€€É•½É°4(€€€€€€€€€€€€‰qqp‰íÁÉ½Á•ÉÑå9…µ•õqqpˆéqqíì üñ‰½‘äø¸¨ü¤ üõqqp‰í¹•áÑAÉ½Á•ÉÑåõqqpˆè üéqqíì¤ü¤ˆ°4(€€€€€€€€€€€I••á=ÁÑ¥½¹Ì¹M¥¹±•±¥¹”ðI••á=ÁÑ¥½¹Ì¹%¹½É•…Í”¤ì4(€€€€€€€¥˜€ …µ…Ñ ¹MÕ•ÍÌ¤4(€€€€€€€ì4(€€€€€€€€€€€É•ÑÕÉ¸¹Õ±°ì4(€€€€€€€ô4(4(€€€€€€€Ù…È‰½‘ä€ôµ…Ñ ¹É½ÕÁÍl‰‰½‘ä‰t¹Y…±Õ”ì4(€€€€€€€Ù…È¥€ôI••à¹5…Ñ ¡‰½‘ä°€‰qqp‰Ñ•…µ%‘qqpˆè üñ¥ùqq¬¤ˆ°I••á=ÁÑ¥½¹Ì¹%¹½É•…Í”¤¹É½ÕÁÍl‰¥‰t¹Y…±Õ”ì4(€€€€€€€Ù…È½‘”€ôI••à¹5…Ñ ¡‰½‘ä°€‰qqp‰½‘•qqpˆè üéqqpˆ üñ½‘”ùmyqqp‰t¨¥qqp‰ñ¹Õ±°¤ˆ°I••á=ÁÑ¥½¹Ì¹%¹½É•…Í”¤¹É½ÕÁÍl‰½‘”‰t¹Y…±Õ”¹QÉ¥´ ¤ì4(€€€€€€€Ù…È¹…µ•5…Ñ €ôI••à¹5…Ñ ¡‰½‘ä°€‰qqp‰Í¡½ÉÑ9…µ•qqpˆéqqpˆ üñ¹…µ”ùmyqqp‰t¨¥qqpˆˆ°I••á=ÁÑ¥½¹Ì¹%¹½É•…Í”¤ì4(€€€€€€€¥˜€ …¹…µ•5…Ñ ¹MÕ•ÍÌ¤4(€€€€€€€ì4(€€€€€€€€€€€¹…µ•5…Ñ €ôI••à¹5…Ñ ¡‰½‘ä°€‰qqp‰½™™¥¥…±9…µ•qqpˆéqqpˆ üñ¹…µ”ùmyqqp‰t¨¥qqpˆˆ°I••á=ÁÑ¥½¹Ì¹%¹½É•…Í”¤ì4(€€€€€€€ô4(4(€€€€€€€Ù…ÈÍ½ÕÉ•%€ôÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡½‘”¤4(€€€€€€€€€€€€üÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡¥¤€üÍÑÉ¥¹œ¹µÁÑä€è€‰%	éí¥‘ôˆ4(€€€€€€€€€€€€è½‘”ì4(€€€€€€€Ù…È¹…µ”€ô¹…µ•5…Ñ ¹MÕ•ÍÌ€ü¹…µ•5…Ñ ¹É½ÕÁÍl‰¹…µ”‰t¹Y…±Õ”¹QÉ¥´ ¤€èÍ½ÕÉ•%ì4(€€€€€€€¥˜€¡ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡Í½ÕÉ•%¤¤4(€€€€€€€ì4(€€€€€€€€€€€É•ÑÕÉ¸¹Õ±°ì4(€€€€€€€ô4(4(€€€€€€€É•ÑÕÉ¸¹•Üµ‰•‘‘•‘Q•…´¡Í½ÕÉ•%°ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡¹…µ”¤€üÍ½ÕÉ•%€è¹…µ”¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ	Õ¥±‘µ‰•‘‘•‘…µ•A…Ñ ¡ÍÑÉ¥¹œÍ½ÕÉ•A…Ñ °ÍÑÉ¥¹œ…µ•%°ÍÑÉ¥¹œ¡½µ•½‘”°ÍÑÉ¥¹œ…Ý…å½‘”¤4(€€€ì4(€€€€€€€Ù…È•‘¥Ñ¥½¹A…Ñ €ôÍ½ÕÉ•A…Ñ ¹¹‘Í]¥Ñ  ˆ½…µ•Ìˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤4(€€€€€€€€€€€€üÍ½ÕÉ•A…Ñ¡l¸¹xÙt4(€€€€€€€€€€€€èÍ½ÕÉ•A…Ñ ¹QÉ¥µ¹ œ¼œ¤ì4(€€€€€€€É•ÑÕÉ¸€‰í•‘¥Ñ¥½¹A…Ñ¡ô½…µ•Ì½í…µ•%‘ôµí¡½µ•½‘”¹Q½UÁÁ•É%¹Ù…É¥…¹Ð ¥ôµí…Ý…å½‘”¹Q½UÁÁ•É%¹Ù…É¥…¹Ð ¥ôˆì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍ¡½ÉÐüA…ÉÍ•µ‰•‘‘•‘M½É”¡ÍÑÉ¥¹œÙ…±Õ”¤4(€€€€€€€€ôøÍ¡½ÉÐ¹QÉåA…ÉÍ”¡Ù…±Õ”°½ÕÐÙ…ÈÍ½É”¤€üÍ½É”€è¹Õ±°ì4(4(€€€ÁÉ¥Ù…Ñ”Í•…±•É•½Éµ‰•‘‘•‘Q•…´¡ÍÑÉ¥¹œM½ÕÉ•%°ÍÑÉ¥¹œ9…µ”¤ì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ…Ñ•Q¥µ”ü¥¹‘…É‘…Ñ”¡!Ñµ±9½‘”…É¤4(€€€ì4(€€€€€€€™½È€¡Ù…È¹½‘”€ô…É¹A…É•¹Ñ9½‘”ì¹½‘”¥Ì¹½Ð¹Õ±°ì¹½‘”€ô¹½‘”¹A…É•¹Ñ9½‘”¤4(€€€€€€€ì4(€€€€€€€€€€€™½È€¡Ù…ÈÍ¥‰±¥¹œ€ô¹½‘”¹AÉ•Ù¥½ÕÍM¥‰±¥¹œìÍ¥‰±¥¹œ¥Ì¹½Ð¹Õ±°ìÍ¥‰±¥¹œ€ôÍ¥‰±¥¹œ¹AÉ•Ù¥½ÕÍM¥‰±¥¹œ¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€¥˜€¡Í¥‰±¥¹œ¹9½‘•QåÁ”€„ô!Ñµ±9½‘•QåÁ”¹±•µ•¹Ð¤4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€½¹Ñ¥¹Õ”ì4(€€€€€€€€€€€€€€€ô4(4(€€€€€€€€€€€€€€€Ù…Èµ…Ñ €ôI••à¹5…Ñ ¡9½Éµ…±¥é”¡Í¥‰±¥¹œ¹%¹¹•ÉQ•áÐ¤° ‰q‰q‘ìÄ°Éômµi„µét¬q‘ìÑõqˆˆ¤ì4(€€€€€€€€€€€€€€€¥˜€¡µ…Ñ ¹MÕ•ÍÌ€˜˜…Ñ•Q¥µ”¹QÉåA…ÉÍ•á…Ð 4(€€€€€€€€€€€€€€€€€€€µ…Ñ ¹Y…±Õ”°4(€€€€€€€€€€€€€€€€€€€€‰5554åååäˆ°4(€€€€€€€€€€€€€€€€€€€Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”°4(€€€€€€€€€€€€€€€€€€€…Ñ•Q¥µ•MÑå±•Ì¹9½¹”°4(€€€€€€€€€€€€€€€€€€€½ÕÐÙ…È‘…Ñ”¤€˜˜‘…Ñ”¹e•…È€øô€ÄäÀÀ¤4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€É•ÑÕÉ¸…Ñ•Q¥µ”¹MÁ•¥™å-¥¹¡‘…Ñ”°…Ñ•Q¥µ•-¥¹¹UÑŒ¤ì4(€€€€€€€€€€€€€€€ô4(€€€€€€€€€€€ô4(€€€€€€€ô4(4(€€€€€€€É•ÑÕÉ¸¹Õ±°ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ…Ñ•Q¥µ”ü¥¹‘½µÁ•Ñ¥Ñ¥½¹MÑ…ÉÑ…Ñ”¡ÍÑÉ¥¹œ¡Ñµ°°¥¹Ðå•…È¤4(€€€ì4(€€€€€€€Ù…È¹½Éµ…±¥é•‘!Ñµ°€ô¡Ñµ°¹I•Á±…” ‰qqpˆˆ°€‰pˆˆ¤ì4(€€€€€€€Ù…Èµ…Ñ €ôI••à¹5…Ñ ¡¹½Éµ…±¥é•‘!Ñµ°°€‰•Ù•¹Ñ…Ñ•MÑ…ÉÑqqp‰qqÌ¨éqqÌ©qqpˆ üñ‘…Ñ”ùqq‘ìÑôµqq‘ìÉôµqq‘ìÉô¤ˆ°I••á=ÁÑ¥½¹Ì¹%¹½É•…Í”¤ì4(€€€€€€€¥˜€¡µ…Ñ ¹MÕ•ÍÌ€˜˜…Ñ•Q¥µ”¹QÉåA…ÉÍ•á…Ð 4(€€€€€€€€€€€µ…Ñ ¹É½ÕÁÍl‰‘…Ñ”‰t¹Y…±Õ”°4(€€€€€€€€€€€€‰åååäµ54µ‘ˆ°4(€€€€€€€€€€€Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”°4(€€€€€€€€€€€…Ñ•Q¥µ•MÑå±•Ì¹9½¹”°4(€€€€€€€€€€€½ÕÐÙ…ÈÍÑ…ÉÑ…Ñ”¤€˜˜ÍÑ…ÉÑ…Ñ”¹e•…È€øô€ÄäÀÀ¤4(€€€€€€€ì4(€€€€€€€€€€€É•ÑÕÉ¸…Ñ•Q¥µ”¹MÁ•¥™å-¥¹¡ÍÑ…ÉÑ…Ñ”°…Ñ•Q¥µ•-¥¹¹UÑŒ¤ì4(€€€€€€€ô4(4(€€€€€€€É•ÑÕÉ¸¹•Ü…Ñ•Q¥µ”¡å•…È°€Ä°€Ä°€À°€À°€À°…Ñ•Q¥µ•-¥¹¹UÑŒ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ%I•…‘=¹±å½±±•Ñ¥½¸ñÍ¡½ÉÐüøA…ÉÍ•M½É•Ì¡!Ñµ±9½‘”…É¤4(€€€ì4(€€€€€€€Ù…Èµ…Ñ¡•Ì€ôI••à¹5…Ñ¡•Ì¡9½Éµ…±¥é”¡…É¹%¹¹•ÉQ•áÐ¤° ˆ üð…q¥q‘ìÄ°Íô ü…q¤ˆ¤ì4(€€€€€€€É•ÑÕÉ¸µ…Ñ¡•Ì4(€€€€€€€€€€€€¹…ÍÐñ5…Ñ ø ¤4(€€€€€€€€€€€€¹Q…­•1…ÍÐ È¤4(€€€€€€€€€€€€¹M•±•Ð¡µ…Ñ €ôøÍ¡½ÉÐ¹QÉåA…ÉÍ”¡µ…Ñ ¹Y…±Õ”°½ÕÐÙ…ÈÍ½É”¤€ü€¡Í¡½ÉÐü¥Í½É”€è¹Õ±°¤4(€€€€€€€€€€€€¹Q½1¥ÍÐ ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œü¥¹‘A¡…Í•1…‰•°¡!Ñµ±9½‘”…É¤4(€€€ì4(€€€€€€€Ù…Èµ…Ñ €ôI••à¹5…Ñ  4(€€€€€€€€€€€9½Éµ…±¥é”¡…É¹%¹¹•ÉQ•áÐ¤°4(€€€€€€€€€€€ ˆ üñÁ¡…Í”ùmyqÔÀÁˆÝt¬¥qÔÀÁˆÜ üñÉ½Õ¹ø¸¨ü¤ üô üé¥¹…±ñM¡•‘Õ±•‘ñA½ÍÑÁ½¹•‘ñ…¹•±±•¤¤ˆ°4(€€€€€€€€€€€I••á=ÁÑ¥½¹Ì¹%¹½É•…Í”¤ì4(€€€€€€€¥˜€¡µ…Ñ ¹MÕ•ÍÌ¤4(€€€€€€€ì4(€€€€€€€€€€€É•ÑÕÉ¸€‰íµ…Ñ ¹É½ÕÁÍl‰Á¡…Í”‰t¹Y…±Õ”¹QÉ¥´ ¥õqÔÀÁˆÝíµ…Ñ ¹É½ÕÁÍl‰É½Õ¹‰t¹Y…±Õ”¹QÉ¥´ ¥ôˆì4(€€€€€€€ô4(4(€€€€€€€Ù…ÈÁ¡…Í”€ô…É¹M•±•Ñ9½‘•Ì ˆ¸¼½‘¥Øˆ¤ü4(€€€€€€€€€€€€¹M•±•Ð¡à€ôø9½Éµ…±¥é”¡à¹%¹¹•ÉQ•áÐ¤¤4(€€€€€€€€€€€€¹¥ÉÍÑ=É•™…Õ±Ð¡à€ôøà¹1•¹Ñ €ðô€àÀ€˜˜4(€€€€€€€€€€€€€€€€¡à¹¹‘Í]¥Ñ  ‰I½Õ¹ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤ñð4(€€€€€€€€€€€€€€€€à¹¹‘Í]¥Ñ  ‰¥¹…±Ìˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤¤¤ì4(€€€€€€€É•ÑÕÉ¸Á¡…Í”¥Ì¹Õ±°€ü¹Õ±°€è€‰íÁ¡…Í•õqÔÀÁˆÜˆì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ¥¹‘MÑ…ÑÕÌ¡!Ñµ±9½‘”…É°%I•…‘=¹±å½±±•Ñ¥½¸ñÍ¡½ÉÐüøÍ½É•Ì¤4(€€€ì4(€€€€€€€Ù…ÈÑ•áÐ€ô9½Éµ…±¥é”¡…É¹%¹¹•ÉQ•áÐ¤ì4(€€€€€€€Ù…ÈÍÑ…ÑÕÌ€ô¹•Ýmtì€‰¥¹…°ˆ°€‰M¡•‘Õ±•ˆ°€‰A½ÍÑÁ½¹•ˆ°€‰…¹•±±•ˆô4(€€€€€€€€€€€€¹¥ÉÍÑ=É•™…Õ±Ð¡…¹‘¥‘…Ñ”€ôøÑ•áÐ¹½¹Ñ…¥¹Ì¡…¹‘¥‘…Ñ”°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤¤ì4(€€€€€€€É•ÑÕÉ¸€¡ÍÑ…ÑÕÌ€üü€¡Í½É•Ì¹±°¡à€ôøà¹!…ÍY…±Õ”¤€ü€‰¥¹…°ˆ€è€‰M¡•‘Õ±•ˆ¤¤¹Q½1½Ý•É%¹Ù…É¥…¹Ð ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑÉ¥¹œ	Õ¥±‘‰Í½±ÕÑ•UÉ°¡ÍÑÉ¥¹œÁ…Ñ ¤4(€€€€€€€€ôø¡ÑÑÁ±¥•¹Ð¹	…Í•‘‘É•ÍÌ¥Ì¹Õ±°€üÁ…Ñ €è¹•ÜUÉ¤¡¡ÑÑÁ±¥•¹Ð¹	…Í•‘‘É•ÍÌ°Á…Ñ ¤¹Q½MÑÉ¥¹œ ¤ì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ9½Éµ…±¥é•A…Ñ ¡ÍÑÉ¥¹œÙ…±Õ”¤4(€€€ì4(€€€€€€€¥˜€¡UÉ¤¹QÉåÉ•…Ñ”¡Ù…±Õ”°UÉ¥-¥¹¹‰Í½±ÕÑ”°½ÕÐÙ…È…‰Í½±ÕÑ”¤¤4(€€€€€€€ì4(€€€€€€€€€€€É•ÑÕÉ¸…‰Í½±ÕÑ”¹‰Í½±ÕÑ•A…Ñ ì4(€€€€€€€ô4(4(€€€€€€€É•ÑÕÉ¸Ù…±Õ”¹MÁ±¥Ð œüœ°€È¥lÁtì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ9½Éµ…±¥é”¡ÍÑÉ¥¹œÙ…±Õ”¤4(€€€€€€€€ôø!Ñµ±¹Ñ¥Ñä¹•¹Ñ¥Ñ¥é”¡I••à¹I•Á±…”¡Ù…±Õ”° ‰qÌ¬ˆ°€ˆ€ˆ¤¤¹QÉ¥´ ¤ì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ¥¹ÐA…ÉÍ•MÑ…ÉÑe•…È¡ÍÑÉ¥¹œÍ•…Í½¸¤4(€€€ì4(€€€€€€€Ù…Èµ…Ñ €ôI••à¹5…Ñ ¡Í•…Í½¸° ‰qˆ ÄåðÈÀ¥q‘ìÉõqˆˆ¤ì4(€€€€€€€É•ÑÕÉ¸µ…Ñ ¹MÕ•ÍÌ€˜˜¥¹Ð¹QÉåA…ÉÍ”¡µ…Ñ ¹Y…±Õ”°½ÕÐÙ…Èå•…È¤€üå•…È€èÑ¡É½Ü¹•ÜÉÕµ•¹Ñá•ÁÑ¥½¸ ‰%	Í•…Í½¸€íÍ•…Í½¹ôœ¡…Ì¹¼™½ÕÈµ‘¥¥Ðå•…È¸ˆ°¹…µ•½˜¡Í•…Í½¸¤¤ì4(€€€ô4)ô4(