using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Historical Yugoslav / Serbia and Montenegro / Serbian-area men's top-flight
/// results. The provider combines SerbianSport, Pearl Basket, and historical
/// Wikipedia score matrices, with OCR reconciliation remaining explicit.
/// </summary>
public sealed class SerbianHistoricalBasketballDataProvider(
    HttpClient httpClient,
    IOptions<SerbianHistoricalOptions> options) : IBasketballDataProvider
{
    public const string Source = "serbian-historical";
    public const string ParserVersion = "serbian-historical-v2";
    public const string LeagueSourceId = "serbian-area-top-flight";
    public const string CupLeagueSourceId = "yugoslav-cup";

    private static readonly IReadOnlyDictionary<int, SeasonArchive> Archives =
        new Dictionary<int, SeasonArchive>
        {
            [2000] = new("2000-2001", [new("272-winston-yuba-liga", "Winston Yuba Liga", "Regular Season"), new("273-play-off", "Play-Off", "Playoffs")]),
            [2001] = new("2001-2002", [new("271-vinston-juba-liga", "Vinston Yuba Liga", "Regular Season"), new("274-play-off", "Play-Off", "Playoffs")]),
            [2002] = new("2002-2003", [new("105-frikom-yuba-liga", "Frikom Yuba Liga", "Regular Season"), new("106-playoff", "Playoff", "Playoffs")]),
            [2003] = new("2003-2004", [new("31-efes-pils-yuba-liga", "Efes Pils Yuba Liga", "Regular Season"), new("107-efes-pils-superliga", "Efes Pils Superliga", "Super League"), new("108-playoff", "Playoff", "Playoffs")]),
            [2004] = new("2004-2005", [new("29-efes-pils-yuba-liga", "Efes Pils Yuba Liga", "Regular Season"), new("30-efes-pils-superliga", "Efes Pils Superliga", "Super League"), new("109-playoff", "Playoff", "Playoffs")]),
            [2005] = new("2005-2006", [new("11-sinalko-prva-liga", "Sinalko Prva Liga", "Regular Season"), new("22-grupa-a", "Sinalko Super Liga - Grupa A", "Super League"), new("23-grupa-b", "Sinalko Super Liga - Grupa B", "Super League"), new("32-play-off", "Play Off", "Playoffs")]),
            [2006] = new("2006-2007", [new("48-nasa-sinalko-liga", "Naša Sinalko Liga", "Regular Season"), new("85-superliga", "Superliga", "Super League"), new("110-playoff", "Playoff", "Playoffs")]),
            [2007] = new("2007-2008", [new("120-kosarkaska-liga-srbije", "Košarkaška liga Srbije", "Regular Season"), new("175-swisslion-superliga-srbije", "Swisslion Superliga Srbije", "Super League"), new("179-play-off", "Play-Off", "Playoffs")])
        };

    private static readonly IReadOnlyDictionary<string, string> CanonicalTeamAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PARTIZAN"] = "Partizan",
            ["CRVENAZVEZDA"] = "Crvena zvezda",
            ["CZVEZDA"] = "Crvena zvezda",
            ["FMP"] = "FMP",
            ["FMPZELEZNIK"] = "FMP",
            ["HEMOFARM"] = "Hemofarm",
            ["VOJVODINA"] = "Vojvodina",
            ["NISVOJVODINA"] = "Vojvodina",
            ["BUDUCNOST"] = "Budućnost",
            ["LOVCEN"] = "Lovćen",
            ["PRIMORKA"] = "Primorka",
            ["MORNAR"] = "Mornar",
            ["SPARTAK"] = "Spartak",
            ["SPARTAKSUBOTICA"] = "Spartak",
            ["ZDRAVLJE"] = "Zdravlje",
            ["SLOGA"] = "Sloga",
            ["BOBANIKSLOGA"] = "Bobanik",
            ["RADNICKI"] = "Radnički",
            ["RADNICKIJP"] = "Radnički JP",
            ["RADNICKIINVEST"] = "Radnički Invest",
            ["RADNICKI034GROUP"] = "Radnički 034 Group",
            ["BORAC"] = "Borac",
            ["BEOPETROL"] = "Beopetrol",
            ["OKKBEOGRAD"] = "OKK Beograd",
            ["LAVOVI063"] = "Lavovi 063",
            ["MASINAC"] = "Mašinac",
            ["ZASTAVA"] = "Zastava",
            ["BEOGRAD"] = "Beograd",
            ["CIBONA"] = "Cibona",
            ["ZADAR"] = "Zadar",
            ["JUGOPLASTIKA"] = "Jugoplastika",
            ["OLIMPIJA"] = "Olimpija",
            ["BOSNA"] = "Bosna",
            ["SLOBODADITA"] = "Sloboda Dita",
            ["BORACBANJALUKA"] = "Borac Banja Luka",
            ["BORACBL"] = "Borac Banja Luka",
            ["BOROVICABORAC"] = "Borac Banja Luka",
            ["SLOGAKRALJEVO"] = "Sloga",
            ["ZORKA"] = "Zorka",
            ["IVAZORKA"] = "Zorka",
            ["IVA"] = "Zorka",
            ["PROFIKOLOR"] = "Profikolor",
            ["PRIVREDNABANKA"] = "Privredna banka Novi Sad",
            ["PRIVREDNABANKANOVISAD"] = "Privredna banka Novi Sad",
            ["IVAUNIKOM"] = "Iva Unikom",
            ["INFOSRTM"] = "Infos RTM",
            ["BEOBANKA"] = "Beobanka",
            ["BEOVUK"] = "Beovuk",
            ["BEOVUKBEMO"] = "Beovuk",
            ["BFC"] = "BFC Beocin",
            ["BFCCEOIN"] = "BFC Beocin",
            ["BFCCEOČIN"] = "BFC Beocin",
            ["BIGENEX"] = "Big Eneks Metalac",
            ["BIGENEKS"] = "Big Eneks Metalac",
            ["BIGENEKSMETALAC"] = "Big Eneks Metalac",
            ["BIGENEXMETALAC"] = "Big Eneks Metalac",
            ["BOBANIK"] = "Bobanik",
            ["BOROVICA"] = "Borovica",
            ["BORACCAK"] = "Borac Cacak",
            ["BORACNEKTAR"] = "Borac Nektar",
            ["FAGAR"] = "Fagar",
            ["IBON"] = "Ibon",
            ["IBONNIKSIC"] = "Ibon",
            ["JUGOTES"] = "Jugotes",
            ["JUGOTESTNN"] = "Jugotes",
            ["KOLUBARA"] = "Kolubara",
            ["MLADOST"] = "Mladost",
            ["MLADOSTSRBOS"] = "Mladost",
            ["NAPNOVISAD"] = "NAP Novi Sad",
            ["NAPREDAK"] = "Napredak",
            ["OKKKIKINDA"] = "OKK Kikinda",
            ["OKKSABAC"] = "OKK Sabac",
            ["PEMONTPROLETER"] = "Pemont Proleter",
            ["PROLETER"] = "Pemont Proleter",
            ["RADNICKICIP"] = "Radnicki CIP",
            ["RADNICKIKRAGUJEVAC"] = "Radnicki Kragujevac",
            ["RAJBANKA"] = "Raj Banka",
            ["SREMTIFANI"] = "Srem",
            ["TEMKONIKSIC"] = "Temko",
            ["VOJVODINAPANSPED"] = "Vojvodina",
            ["IVAKORMILO"] = "Iva Omega",
            ["IVAOMEGA"] = "Iva Omega",
            ["SLOBODAUZICE"] = "Sloboda Uzice"
            , ["UZICE"] = "Sloboda Uzice"
            , ["UZICENIMI"] = "Sloboda Uzice"
        };

    private static DateTime _lastRequestUtc = DateTime.MinValue;
    private static readonly SemaphoreSlim RequestGate = new(1, 1);

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!country.Equals("Serbia", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        if (leagueName.Equals("Yugoslav Cup", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(
                new(Source, CupLeagueSourceId, "Yugoslav Cup", "RS", "start_year"));
        }

        if (!leagueName.Equals("First League", StringComparison.OrdinalIgnoreCase) &&
            !leagueName.Equals("Yugoslav / Serbia and Montenegro / Serbia top flight", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        return Task.FromResult<BasketballProviderLeague?>(
            new(Source, LeagueSourceId, "First League", "RS", "start_year"));
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (league.SourceLeagueId == CupLeagueSourceId)
        {
            return await GetCupGamesAsync(season, cancellationToken, context);
        }

        if (league.SourceLeagueId != LeagueSourceId)
        {
            throw new InvalidOperationException($"Serbian historical provider does not support '{league.SourceLeagueId}'.");
        }

        var startYear = SeasonLabelNormalizer.ParseStartYear(season);
        if (startYear < 2000)
        {
            return await GetLegacyGamesAsync(season, startYear, context, cancellationToken);
        }

        if (!Archives.TryGetValue(startYear, out var archive) ||
            !archive.Season.Equals(season, StringComparison.OrdinalIgnoreCase))
        {
            return ([], false,
            [
                $"No complete game-level Serbian-area top-flight source is configured for {season}.",
                "The 1945-1991 federation archive and the 1991-2000 historical range remain source gaps; no standings-only data is imported."
            ]);
        }

        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.Ordinal);
        var warnings = new List<string>
        {
            "SerbianSport.net is used as the reviewed public fallback archive for this season; reconcile against federation or club archives before treating the season as authoritative.",
            "Many regular-season pages do not publish a date for each game. The provider uses the source startDate when present and a deterministic round date otherwise."
        };
        var hasMorePages = false;

        foreach (var stage in archive.Stages)
        {
            var firstPage = $"/league/{stage.Path}/round/1";
            if (!context.CanUseRequest())
            {
                return (games.Values.OrderBy(x => x.GameDateTimeUtc).ToArray(), true, warnings);
            }

            var firstDocument = await FetchAsync(firstPage, context, cancellationToken);
            var roundCount = ParseRoundCount(firstDocument.Content);
            if (roundCount == 0)
            {
                warnings.Add($"Could not discover rounds for {stage.Name} ({stage.Path}).");
                continue;
            }

            for (var round = 1; round <= roundCount; round++)
            {
                string content;
                DateTime fetchedAtUtc;
                string revision;
                if (round == 1)
                {
                    content = firstDocument.Content;
                    fetchedAtUtc = firstDocument.FetchedAtUtc;
                    revision = firstDocument.Revision;
                }
                else
                {
                    if (!context.CanUseRequest())
                    {
                        hasMorePages = true;
                        break;
                    }

                    var page = await FetchAsync($"/league/{stage.Path}/round/{round}", context, cancellationToken);
                    content = page.Content;
                    fetchedAtUtc = page.FetchedAtUtc;
                    revision = page.Revision;
                }

                foreach (var game in ParseRoundPage(content, archive.Season, startYear, stage, round, fetchedAtUtc, revision, warnings))
                {
                    games[game.SourceGameId] = game;
                }
            }

            if (hasMorePages)
            {
                break;
            }
        }

        if (games.Count == 0)
        {
            warnings.Add($"No completed games were parsed for Serbian historical season {season}.");
        }

        return (games.Values.OrderBy(x => x.GameDateTimeUtc).ThenBy(x => x.SourceGameId).ToArray(), hasMorePages, warnings);
    }

    private async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetLegacyGamesAsync(
        string season,
        int startYear,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        if (!TryGetLegacySource(season, startYear, out var source))
        {
            return ([], false,
            [
                $"No legacy source is configured for {season}.",
                "The 1992-1993 season requires Borba OCR reconciliation; standings-only material is not imported."
            ]);
        }

        if (!context.CanUseRequest())
        {
            return ([], true, [$"Request budget reached before {source.Kind} could be fetched for {season}."]);
        }

        if (source.Kind == LegacySourceKind.Borba)
        {
            if (startYear == 1994 && source.Supplemental is not null)
            {
                var partizanopedia1994Phases = new HashSet<string>(["Regular Season", "Playoffs"], StringComparer.OrdinalIgnoreCase);
                var supplementalSchedule = await GetPartizanopediaGamesAsync(source.Supplemental, season, context, cancellationToken, partizanopedia1994Phases);
                return (
                    supplementalSchedule.Games,
                    false,
                    [
                        "The 1994-1995 Borba OCR reconstruction is retained in the database from the reviewed partial import; this rerun uses the direct Partizanopedia schedule so the documented playoff route can be reconciled without repeating the unbounded newspaper crawl.",
                        .. supplementalSchedule.Warnings
                    ]);
            }

            var borba = await GetBorbaGamesAsync(season, startYear, context, cancellationToken);
            if (source.Supplemental is null || borba.HasMorePages || !context.CanUseRequest())
            {
                return borba;
            }

            var includedPhases = startYear is 1991 or 1992 or 1993 or 1994
                ? new HashSet<string>(["Regular Season", "Playoffs"], StringComparer.OrdinalIgnoreCase)
                : null;
            var supplemental = await GetPartizanopediaGamesAsync(source.Supplemental, season, context, cancellationToken, includedPhases);
            return (
                MergeGames(borba.Games, supplemental.Games),
                false,
                borba.Warnings.Concat(supplemental.Warnings).ToArray());
        }

        if (source.Kind == LegacySourceKind.Partizanopedia)
        {
            var supplementalPage = await FetchAbsoluteAsync(source.Url, context, cancellationToken);
            var supplementalGames = SerbianHistoricalLegacyParser.ParsePartizanopedia(supplementalPage.Content, season, source.Url, CanonicalTeamName, TeamCountryCode, Source, supplementalPage.FetchedAtUtc);
            return (supplementalGames, false, [source.Warning, $"Partizanopedia supplies a club-level schedule for {supplementalGames.Count} games; this is supplemental rather than a complete league reconstruction."]);
        }

        var page = await FetchAbsoluteAsync(source.Url, context, cancellationToken, source.Kind == LegacySourceKind.PearlBasket);
        IReadOnlyCollection<BasketballProviderGame> games = source.Kind == LegacySourceKind.PearlBasket
            ? SerbianHistoricalLegacyParser.ParsePearlBasket(page.Content, season, source.Url, CanonicalTeamName, TeamCountryCode, Source, page.FetchedAtUtc)
            : source.IsSerbianWikipedia
                ? SerbianHistoricalLegacyParser.ParseSerbianWikipediaRoundResults(page.Content, season, source.Url, CanonicalTeamName, TeamCountryCode, Source, page.FetchedAtUtc)
                : SerbianHistoricalLegacyParser.ParseWikipediaRaw(page.Content, season, source.Url, CanonicalTeamName, TeamCountryCode, Source, page.FetchedAtUtc);

        warnings.Add(source.Warning);
        if (games.Count == 0)
        {
            warnings.Add($"No complete game-level results were parsed from {source.Url}.");
        }
        else if (source.ExpectedGameCount > 0 && games.Count < source.ExpectedGameCount)
        {
            warnings.Add($"Parsed {games.Count} games; the source advertises approximately {source.ExpectedGameCount} regular-season games, so this season requires reconciliation.");
        }

        if (source.Supplemental is not null)
        {
            if (!context.CanUseRequest())
            {
                warnings.Add("Request budget reached before the Partizanopedia supplemental schedule could be fetched.");
            }
            else
            {
                var includedPhases = startYear is 1991 or 1992 or 1993 or 1994
                    ? new HashSet<string>(["Regular Season", "Playoffs"], StringComparer.OrdinalIgnoreCase)
                    : null;
                var supplemental = await GetPartizanopediaGamesAsync(source.Supplemental, season, context, cancellationToken, includedPhases);
                games = ApplyKnownScheduleDates(games, supplemental.Games);
                games = MergeGames(games, supplemental.Games);
                warnings.AddRange(supplemental.Warnings);
            }
        }

        if (startYear == 1991)
        {
            var verifiedPlayoffs = await GetVerified1991PlayoffGamesAsync(season, context, cancellationToken);
            games = MergeGames(games, verifiedPlayoffs.Games);
            warnings.AddRange(verifiedPlayoffs.Warnings);
        }

        return (games, false, warnings);
    }

    private async Task<(IReadOnlyCollection<BasketballProviderGame> Games, IReadOnlyCollection<string> Warnings)> GetVerified1991PlayoffGamesAsync(
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var sources = new[]
        {
            new VerifiedBorbaResultSource(
                "https://pretraziva.rs/show/borba/1992-04-22/27",
                new DateTime(1992, 4, 21, 12, 0, 0, DateTimeKind.Utc),
                "Rabotnički",
                "Crvena zvezda",
                85,
                82,
                "Semifinal Game 1"),
            new VerifiedBorbaResultSource(
                "https://pretraziva.rs/anzeigen/borba/1992-04-25/32",
                new DateTime(1992, 4, 24, 12, 0, 0, DateTimeKind.Utc),
                "Crvena zvezda",
                "Rabotnički",
                89,
                69,
                "Semifinal Game 2")
        };

        var games = new List<BasketballProviderGame>();
        var warnings = new List<string>();
        foreach (var source in sources)
        {
            if (!context.CanUseRequest())
            {
                warnings.Add($"Request budget stopped the verified Borba playoff supplement after {games.Count} game(s).");
                break;
            }

            try
            {
                var page = await FetchAbsoluteAsync(source.Url, context, cancellationToken);
                games.AddRange(SerbianHistoricalLegacyParser.ParseBorbaVerifiedResult(
                    page.Content,
                    season,
                    source.Url,
                    source.GameDateUtc,
                    source.HomeTeam,
                    source.AwayTeam,
                    source.HomeScore,
                    source.AwayScore,
                    source.Round,
                    TeamCountryCode,
                    Source,
                    page.FetchedAtUtc));
            }
            catch (HttpRequestException exception)
            {
                warnings.Add($"Verified Borba playoff page {source.Url} was skipped: {exception.Message}");
            }
        }

        warnings.Add($"Borba verifies {games.Count} Crvena zvezda–Rabotnički semifinal game(s) for 1991-1992; the deciding-game score is still unresolved.");
        return (games, warnings);
    }

    private async Task<(IReadOnlyCollection<BasketballProviderGame> Games, IReadOnlyCollection<string> Warnings)> GetPartizanopediaGamesAsync(
        LegacySource source,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? includedPhases = null)
    {
        var page = await FetchAbsoluteAsync(source.Url, context, cancellationToken);
        var games = SerbianHistoricalLegacyParser.ParsePartizanopedia(
            page.Content,
            season,
            source.Url,
            CanonicalTeamName,
            TeamCountryCode,
            Source,
            page.FetchedAtUtc,
            includedPhases);
        return (
            games,
            [
                source.Warning,
                $"Partizanopedia added {games.Count} club-level result(s); this is a supplement, not a complete league/tournament reconstruction."
            ]);
    }

    private async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetCupGamesAsync(
        string season,
        CancellationToken cancellationToken,
        BackfillExecutionContext context)
    {
        if (!int.TryParse(season[..4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var startYear) ||
            startYear is < 1973 or > 1999)
        {
            return ([], false, [$"No Yugoslav Cup source is configured for {season}."]);
        }

        if (!context.CanUseRequest())
        {
            return ([], true, ["Request budget reached before the Partizanopedia Yugoslav Cup archive could be fetched."]);
        }

        var source = CreatePartizanopediaSource(startYear);
        var page = await FetchAbsoluteAsync(source.Url, context, cancellationToken);
        var games = SerbianHistoricalLegacyParser.ParsePartizanopedia(
            page.Content,
            season,
            source.Url,
            CanonicalTeamName,
            TeamCountryCode,
            Source,
            page.FetchedAtUtc,
            new HashSet<string>(["Cup"], StringComparer.OrdinalIgnoreCase));
        // Partizanopedia restarts its ordinal within each section.  Keep cup
        // source IDs distinct from league/playoff IDs because the database
        // uniqueness key is provider-wide, not competition-scoped.
        games = games
            .Select(game => game with { SourceGameId = $"{game.SourceGameId}:cup" })
            .ToArray();

        return (
            games,
            false,
            [
                $"Partizanopedia supplies Partizan's documented Yugoslav Cup route for {season}; complete tournament coverage is not configured.",
                $"Parsed {games.Count} Yugoslav Cup game(s) from the club archive."
            ]);
    }

    private static IReadOnlyCollection<BasketballProviderGame> MergeGames(
        IEnumerable<BasketballProviderGame> primary,
        IEnumerable<BasketballProviderGame> supplemental)
    {
        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.Ordinal);
        foreach (var game in primary.Concat(supplemental))
        {
            games.TryAdd(GameIdentityKey(game), game);
        }

        return games.Values
            .OrderBy(game => game.GameDateTimeUtc)
            .ThenBy(game => game.SourceGameId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyCollection<BasketballProviderGame> ApplyKnownScheduleDates(
        IEnumerable<BasketballProviderGame> primary,
        IEnumerable<BasketballProviderGame> datedSchedule)
    {
        var dates = datedSchedule
            .GroupBy(GameIdentityKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().GameDateTimeUtc, StringComparer.Ordinal);

        return primary
            .Select(game => dates.TryGetValue(GameIdentityKey(game), out var date)
                ? game with { GameDateTimeUtc = date, CompetitionRound = "Published schedule" }
                : game)
            .ToArray();
    }

    private static string GameIdentityKey(BasketballProviderGame game)
    {
        var first = $"{MergeTeamKey(game.HomeTeamName)}:{game.HomeScore?.ToString(CultureInfo.InvariantCulture) ?? "?"}";
        var second = $"{MergeTeamKey(game.AwayTeamName)}:{game.AwayScore?.ToString(CultureInfo.InvariantCulture) ?? "?"}";
        return string.CompareOrdinal(first, second) <= 0 ? $"{first}|{second}" : $"{second}|{first}";
    }

    private static string MergeTeamKey(string value)
        => NormalizeKey(value) switch
        {
            "PARTIZANINEX" => "PARTIZAN",
            "PRVIPARTIZAN" => "UZICE",
            "SLOBODA" or "SLOBODATUZLA" or "SLOBODADITA" => "SLOBODA",
            "RABOTNICKIGODEL" => "RABOTNICKI",
            "RADNICKIBEOGRAD" or "RADNICKIBELGRADE" => "RADNICKI",
            "INFOSRTM" => "INFOSRTM",
            _ => NormalizeKey(value)
        };

    private async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetBorbaGamesAsync(
        string season,
        int startYear,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var from = startYear == 1994 ? "1994-10-01" : $"{startYear}-08-01";
        var to = $"{startYear + 1}-06-30";
        IReadOnlyCollection<string> searchTerms = startYear == 1994
            ? ["KOSARKA", "PLAVA", "BOROVICA"]
            : ["YUBA LIGA"];
        var links = new Dictionary<string, SerbianHistoricalLegacyParser.BorbaLink>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>
        {
            "Borba OCR pages are used to reconstruct the season; publication dates are used as deterministic game dates and each result requires reconciliation against another archive."
        };

        foreach (var searchTerm in searchTerms)
        {
            for (var startRow = 0; ; startRow += 100)
            {
                if (!context.CanUseRequest())
                {
                    warnings.Add($"Request budget stopped Borba search discovery after {links.Count} candidate page(s).");
                    return ([], true, warnings);
                }

                var pageSuffix = startRow == 0 ? string.Empty : $"&startrow={startRow}";
                var url = new Uri(new Uri(options.Value.BorbaBaseUrl),
                    $"search?search={Uri.EscapeDataString(searchTerm)}&results=100&sort=date_asc&path=borba&dateFrom={from}&dateTo={to}{pageSuffix}").ToString();
                FetchedPage search;
                try
                {
                    search = await FetchAbsoluteAsync(url, context, cancellationToken);
                }
                catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
                {
                    warnings.Add($"Borba search temporarily returned 403 for '{searchTerm}' at start row {startRow}; continuing with the other search terms.");
                    break;
                }
                catch (HttpRequestException exception)
                {
                    warnings.Add($"Borba search failed for '{searchTerm}' at start row {startRow}: {exception.Message}");
                    break;
                }
                foreach (var link in SerbianHistoricalLegacyParser.ParseBorbaLinks(search.Content))
                {
                    links[link.Url] = link;
                }

                var total = SerbianHistoricalLegacyParser.ParseBorbaSearchTotal(search.Content);
                if (total <= 0 || startRow + 100 >= total)
                {
                    break;
                }
            }
        }

        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.Ordinal);
        foreach (var link in links.Values.OrderBy(link => link.PublicationDateUtc).ThenBy(link => link.Url, StringComparer.Ordinal))
        {
            if (!context.CanUseRequest())
            {
                warnings.Add($"Request budget stopped Borba traversal after {games.Count} parsed games.");
                return (games.Values.ToArray(), true, warnings);
            }

            FetchedPage page;
            try
            {
                page = await FetchAbsoluteAsync(link.Url, context, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                warnings.Add($"Borba page {link.Url} was skipped: {exception.Message}");
                continue;
            }
            foreach (var game in SerbianHistoricalLegacyParser.ParseBorbaText(
                page.Content,
                season,
                link.Url,
                link.PublicationDateUtc,
                CanonicalTeamName,
                TeamCountryCode,
                Source,
                page.FetchedAtUtc))
            {
                games[GameIdentityKey(game)] = game;
            }
        }

        if (games.Count == 0)
        {
            warnings.Add($"Borba search returned {links.Count} candidate page(s), but no team-score lines were parsed for {season}.");
        }
        else
        {
            warnings.Add($"Parsed {games.Count} distinct Borba result(s) from {links.Count} newspaper page(s); this is a partial reconstruction until all rounds are reconciled.");
        }

        return (games.Values.OrderBy(game => game.GameDateTimeUtc).ThenBy(game => game.SourceGameId, StringComparer.Ordinal).ToArray(), false, warnings);
    }

    private async Task<FetchedPage> FetchAbsoluteAsync(string url, BackfillExecutionContext context, CancellationToken cancellationToken, bool windows1252 = false)
    {
        await RequestGate.WaitAsync(cancellationToken);
        try
        {
            var interval = Math.Max(0, options.Value.MinRequestIntervalMilliseconds);
            var wait = interval - (DateTime.UtcNow - _lastRequestUtc).TotalMilliseconds;
            if (wait > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(wait), cancellationToken);
            }

            context.ConsumeRequest();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var userAgent = Uri.TryCreate(url, UriKind.Absolute, out var absoluteUrl) &&
                absoluteUrl.Host.Contains("wikipedia.org", StringComparison.OrdinalIgnoreCase)
                ? "BasketElo/1.0 (contact admin)"
                : options.Value.UserAgent;
            request.Headers.UserAgent.ParseAdd(userAgent);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                using var response = await httpClient.SendAsync(request, timeout.Token);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
                var content = windows1252
                    ? DecodeWindows1252(bytes)
                    : Encoding.UTF8.GetString(bytes);
                _lastRequestUtc = DateTime.UtcNow;
                return new(content, _lastRequestUtc, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..16]);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new HttpRequestException($"Request timed out after 45 seconds: {url}", null, HttpStatusCode.RequestTimeout);
            }
        }
        finally
        {
            RequestGate.Release();
        }
    }

    private static string DecodeWindows1252(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252).GetString(bytes);
    }

    private bool TryGetLegacySource(string season, int startYear, out LegacySource source)
    {
        if (startYear is >= 1973 and <= 1990)
        {
            var suffix = (startYear + 1).ToString(CultureInfo.InvariantCulture)[^2..];
            source = new(
                LegacySourceKind.PearlBasket,
                new Uri(new Uri(options.Value.PearlBasketBaseUrl), $"JU{suffix}.htm").ToString(),
                "Pearl Basket supplies dated round-by-round results; imported phases follow the archive's published order.",
                0);
            return true;
        }

        if (startYear == 1992)
        {
            const string serbianTitle = "%D0%9F%D1%80%D0%B2%D0%B0_%D0%BB%D0%B8%D0%B3%D0%B0_%D0%A1%D0%A0_%D0%88%D1%83%D0%B3%D0%BE%D1%81%D0%BB%D0%B0%D0%B2%D0%B8%D1%98%D0%B5_%D1%83_%D0%BA%D0%BE%D1%88%D0%B0%D1%80%D1%86%D0%B8_1992%2F93.";
            source = new(
                LegacySourceKind.Wikipedia,
                new Uri(new Uri(options.Value.SerbianWikipediaBaseUrl), $"w/index.php?title={serbianTitle}&action=raw").ToString(),
                "Serbian Wikipedia publishes the 1992-1993 round-by-round result tables; 206 scored regular-season games are available, while 22 scheduled cells remain blank and require reconciliation.",
                228,
                CreatePartizanopediaSource(startYear),
                true);
            return true;
        }

        var title = startYear switch
        {
            1991 => "1991%E2%80%9392_YUBA_League",
            1993 => "1993%E2%80%9394_YUBA_League",
            1995 => "1995%E2%80%9396_YUBA_League",
            1996 => "1996%E2%80%9397_YUBA_League",
            1997 => "1997%E2%80%9398_YUBA_League",
            1998 => "1998%E2%80%9399_YUBA_League",
            1999 => "1999%E2%80%932000_YUBA_League",
            _ => string.Empty
        };
        if (!string.IsNullOrWhiteSpace(title))
        {
            var partizanopedia = startYear is >= 1991 and <= 1995
                ? CreatePartizanopediaSource(startYear)
                : null;
            source = new(
                LegacySourceKind.Wikipedia,
                new Uri(new Uri(options.Value.WikipediaBaseUrl), $"w/index.php?title={title}&action=raw").ToString(),
                "Wikipedia's historical Sports results matrix is used; dates are deterministic source-order dates and missing matrix cells remain a reconciliation warning.",
                startYear switch
                {
                    1996 or 1997 => 182,
                    1998 or 1999 => 132,
                    1991 => 132,
                    1993 => 192,
                    1995 => 244,
                    _ => 0
                },
                partizanopedia);
            return true;
        }

        if (startYear == 1992)
        {
            source = new(
                LegacySourceKind.Borba,
                string.Empty,
                "Borba OCR reconstruction source.",
                0,
                CreatePartizanopediaSource(startYear));
            return true;
        }

        if (startYear == 1994)
        {
            source = new(
                LegacySourceKind.Borba,
                string.Empty,
                "Borba OCR pages are reconciled against the published 1994-1995 YUBA standings and Partizanopedia club schedule.",
                448,
                CreatePartizanopediaSource(startYear));
            return true;
        }

        source = null!;
        return false;
    }

    private LegacySource CreatePartizanopediaSource(int startYear)
    {
        var seasonSuffix = $"{startYear}-{((startYear + 1) % 100):D2}";
        return new(
            LegacySourceKind.Partizanopedia,
            new Uri(new Uri(options.Value.PartizanopediaBaseUrl), $"{seasonSuffix}%20kosarka.html").ToString(),
            $"Partizanopedia's {startYear}-{startYear + 1} archive is used as a club-level league-schedule supplement.",
            0);
    }

    public static int ParseRoundCount(string html)
    {
        var matches = Regex.Matches(html, @"switchRound\((?<round>\d+)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return matches.Count == 0
            ? 1
            : matches.Max(match => int.TryParse(match.Groups["round"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var round) ? round : 1);
    }

    public static IReadOnlyCollection<BasketballProviderGame> ParseRoundPage(
        string html,
        string season,
        int startYear,
        ArchiveStage stage,
        int round,
        DateTime fetchedAtUtc,
        string revision,
        ICollection<string>? warnings = null)
    {
        warnings ??= new List<string>();
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var games = new List<BasketballProviderGame>();

        foreach (var row in document.DocumentNode.SelectNodes("//div[contains(concat(' ', normalize-space(@class), ' '), ' game-row ')][@data-id]") ?? Enumerable.Empty<HtmlNode>())
        {
            var script = row.SelectSingleNode(".//script[@type='application/ld+json']");
            if (script is null)
            {
                warnings.Add($"SerbianSport game row {row.GetAttributeValue("data-id", "?")} has no JSON-LD event payload.");
                continue;
            }

            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(script.InnerText);
            }
            catch (JsonException)
            {
                warnings.Add($"SerbianSport game row {row.GetAttributeValue("data-id", "?")} has invalid JSON-LD.");
                continue;
            }

            using (json)
            {
                var root = json.RootElement;
                var home = GetNestedString(root, "homeTeam", "name");
                var away = GetNestedString(root, "awayTeam", "name");
                var description = GetString(root, "description");
                var score = Regex.Match(description, @"(?<home>\d+)\s*:\s*(?<away>\d+)", RegexOptions.CultureInvariant);
                if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away) || !score.Success ||
                    !short.TryParse(score.Groups["home"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var homeScore) ||
                    !short.TryParse(score.Groups["away"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var awayScore))
                {
                    warnings.Add($"SerbianSport game row {row.GetAttributeValue("data-id", "?")} has incomplete teams or final score.");
                    continue;
                }

                var sourceId = row.GetAttributeValue("data-id", string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(sourceId))
                {
                    continue;
                }

                var canonicalHome = CanonicalTeamName(home);
                var canonicalAway = CanonicalTeamName(away);
                var sourceUrl = GetString(root, "url");
                var gameDate = ParseSourceDate(GetString(root, "startDate")) ?? InferRoundDate(startYear, round, stage.Phase);
                var homeCode = TeamCountryCode(canonicalHome);
                var awayCode = TeamCountryCode(canonicalAway);
                games.Add(new BasketballProviderGame(
                    Source,
                    $"srbijasport-{sourceId}",
                    gameDate,
                    "finished",
                    SourceTeamId(canonicalHome),
                    canonicalHome,
                    SourceTeamId(canonicalAway),
                    canonicalAway,
                    homeScore,
                    awayScore,
                    new BasketballProviderGameProvenance(
                        string.IsNullOrWhiteSpace(sourceUrl) ? null : sourceUrl,
                        season,
                        fetchedAtUtc,
                        ParserVersion,
                        revision),
                    null,
                    stage.Phase,
                    stage.Phase == "Playoffs" ? $"Playoff round {round}" : $"Round {round}",
                    homeCode,
                    awayCode));
            }
        }

        return games;
    }

    private async Task<FetchedPage> FetchAsync(string path, BackfillExecutionContext context, CancellationToken cancellationToken)
    {
        await RequestGate.WaitAsync(cancellationToken);
        try
        {
            var interval = Math.Max(0, options.Value.MinRequestIntervalMilliseconds);
            var wait = interval - (DateTime.UtcNow - _lastRequestUtc).TotalMilliseconds;
            if (wait > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(wait), cancellationToken);
            }

            context.ConsumeRequest();
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.UserAgent.ParseAdd(options.Value.UserAgent);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            _lastRequestUtc = DateTime.UtcNow;
            return new(content, _lastRequestUtc, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..16]);
        }
        finally
        {
            RequestGate.Release();
        }
    }

    private static DateTime? ParseSourceDate(string value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.UtcDateTime
            : null;

    private static DateTime InferRoundDate(int startYear, int round, string phase)
    {
        var month = phase == "Playoffs" ? 5 : 10;
        var day = phase == "Playoffs" ? 1 : 1;
        return new DateTime(startYear, month, day, 12, 0, 0, DateTimeKind.Utc).AddDays((round - 1) * 7);
    }

    private static string CanonicalTeamName(string value)
    {
        var key = NormalizeKey(value);
        if (CanonicalTeamAliases.TryGetValue(key, out var canonical))
        {
            return canonical;
        }

        foreach (var (prefix, name) in new[]
        {
            ("PARTIZAN", "Partizan"),
            ("CRVENAZVEZDA", "Crvena zvezda"),
            ("FMP", "FMP"),
            ("BUDUCNOST", "Budućnost"),
            ("VOJVODINA", "Vojvodina"),
            ("SPARTAK", "Spartak")
        })
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                return name;
            }
        }

        return value.Trim();
    }

    private static string TeamCountryCode(string team)
        => NormalizeKey(team) switch
        {
            "BUDUCNOST" or "LOVCEN" or "PRIMORKA" or "MORNAR" => "ME",
            "CIBONA" or "ZADAR" or "JUGOPLASTIKA" or "NOVIZAGREB" => "HR",
            "OLIMPIJA" => "SI",
            "BOSNA" or "SLOBODADITA" or "BORACBANJALUKA" => "BA",
            _ => "RS"
        };

    private static string SourceTeamId(string team)
        => $"serbia-club:{NormalizeKey(team).ToLowerInvariant()}";

    private static string NormalizeKey(string value)
        => string.Concat(TransliterateSerbian(value ?? string.Empty)
            .Normalize(NormalizationForm.FormD)
            .Where(character => char.IsLetterOrDigit(character) &&
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark))
            .ToUpperInvariant();

    private static string TransliterateSerbian(string value)
        => string.Concat(value.Select(character => character switch
        {
            'А' => "A", 'Б' => "B", 'В' => "V", 'Г' => "G", 'Д' => "D", 'Ђ' => "Dj", 'Е' => "E", 'Ж' => "Z", 'З' => "Z",
            'И' => "I", 'Ј' => "J", 'К' => "K", 'Л' => "L", 'Љ' => "Lj", 'М' => "M", 'Н' => "N", 'Њ' => "Nj", 'О' => "O",
            'П' => "P", 'Р' => "R", 'С' => "S", 'Т' => "T", 'Ћ' => "C", 'У' => "U", 'Ф' => "F", 'Х' => "H", 'Ц' => "C",
            'Ч' => "C", 'Џ' => "Dz", 'Ш' => "S", 'а' => "a", 'б' => "b", 'в' => "v", 'г' => "g", 'д' => "d", 'ђ' => "dj",
            'е' => "e", 'ж' => "z", 'з' => "z", 'и' => "i", 'ј' => "j", 'к' => "k", 'л' => "l", 'љ' => "lj", 'м' => "m",
            'н' => "n", 'њ' => "nj", 'о' => "o", 'п' => "p", 'р' => "r", 'с' => "s", 'т' => "t", 'ћ' => "c", 'у' => "u",
            'ф' => "f", 'х' => "h", 'ц' => "c", 'ч' => "c", 'џ' => "dz", 'ш' => "s", _ => character.ToString()
        }));

    private static string GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static string GetNestedString(JsonElement element, string parentName, string propertyName)
        => element.TryGetProperty(parentName, out var parent) && parent.ValueKind == JsonValueKind.Object
            ? GetString(parent, propertyName)
            : string.Empty;

    private sealed record FetchedPage(string Content, DateTime FetchedAtUtc, string Revision);
    private sealed record VerifiedBorbaResultSource(
        string Url,
        DateTime GameDateUtc,
        string HomeTeam,
        string AwayTeam,
        short HomeScore,
        short AwayScore,
        string Round);
    private enum LegacySourceKind { PearlBasket, Wikipedia, Borba, Partizanopedia }
    private sealed record LegacySource(
        LegacySourceKind Kind,
        string Url,
        string Warning,
        int ExpectedGameCount,
        LegacySource? Supplemental = null,
        bool IsSerbianWikipedia = false);
    private sealed record SeasonArchive(string Season, IReadOnlyCollection<ArchiveStage> Stages);
    public sealed record ArchiveStage(string Path, string Name, string Phase);
}
