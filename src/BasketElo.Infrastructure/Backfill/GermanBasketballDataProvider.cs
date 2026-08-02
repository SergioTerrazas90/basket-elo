using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Official German men's top-flight results from the easyCredit BBL archive API.
/// The API's BBL competition feed contains regular-season and postseason games
/// from the reviewed 1975-1976 season onward.
/// </summary>
public sealed class GermanBasketballDataProvider(
    HttpClient httpClient,
    IOptions<GermanBasketballOptions> options) : IBasketballDataProvider
{
    public const string Source = "german-official";
    public const string ParserVersion = "easycredit-bbl-api-v1";
    public const string InferredParserVersion = "easycredit-bbl-api-v2-round-inferred";
    public const string HertenInferredParserVersion = "easycredit-bbl-api-v3-roster-inferred";
    public const string PaderbornInferredParserVersion = "easycredit-bbl-api-v4-roster-inferred";
    public const string BramscheDortmundInferredParserVersion = "easycredit-bbl-api-v5-schedule-inferred";
    public const string Bramsche1991InferredParserVersion = "easycredit-bbl-api-v6-roster-inferred";
    public const string Bramsche1990InferredParserVersion = "easycredit-bbl-api-v7-roster-inferred";
    public const string Hagen1989InferredParserVersion = "easycredit-bbl-api-v8-roster-inferred";
    public const string Hagen1988InferredParserVersion = "easycredit-bbl-api-v9-roster-inferred";
    public const string HistoricalPostseasonDateInferredParserVersion = "easycredit-bbl-api-v10-postseason-date-inferred";
    public const string HistoricalPostseasonTeamAndDateInferredParserVersion = "easycredit-bbl-api-v11-postseason-team-date-inferred";
    public const string Osnabrueck1986InferredParserVersion = "easycredit-bbl-api-v12-roster-inferred";
    public const string Osnabrueck1985InferredParserVersion = "easycredit-bbl-api-v13-roster-inferred";
    public const string HistoricalRosterInferredParserVersion = "easycredit-bbl-api-v14-historical-roster-inferred";
    public const string Historical1975RosterInferredParserVersion = "easycredit-bbl-api-v15-historical-roster-inferred";

    private const string LeagueSourceId = "BBL";
    private const string ApiKey = "publicWebUser";
    private const string ApiSecretHeader = "x-api-secret";
    private const string ApiKeyHeader = "x-api-key";
    private const int FirstSeason = 1975;
    private const int LastHistoricalSeason = 2007;
    private const string InferenceSeason = "1996-1997";
    private const string InferredTeamId = "422";
    private const string InferredTeamName = "SG Braunschweig";
    private const string HertenInferenceSeason = "1995-1996";
    private const string HertenInferredTeamId = "herten-1995";
    private const string HertenInferredTeamName = "TuS Herten";
    private const string PaderbornInferenceSeason = "1994-1995";
    private const string PaderbornInferredTeamId = "paderborn-1994";
    private const string PaderbornInferredTeamName = "Forbo Paderborn 91";
    private const string Bramsche1991InferenceSeason = "1991-1992";
    private const string Bramsche1991InferredTeamId = "bramsche-1991";
    private const string Bramsche1991InferredTeamName = "TuS Bramsche";
    private const string Bramsche1990InferenceSeason = "1990-1991";
    private const string Bramsche1990InferredTeamId = "bramsche-1990";
    private const string Bramsche1990InferredTeamName = "TuS Bramsche";
    private const string Hagen1989InferenceSeason = "1989-1990";
    private const string Hagen1989InferredTeamId = "tsv-hagen-1860-1989";
    private const string Hagen1989InferredTeamName = "TSV Hagen 1860";
    private const string Hagen1988InferenceSeason = "1988-1989";
    private const string Hagen1988InferredTeamId = "tsv-hagen-1860-1988";
    private const string Hagen1988InferredTeamName = "TSV Hagen 1860";
    private const string Osnabrueck1986InferenceSeason = "1986-1987";
    private const string Osnabrueck1986InferredTeamId = "bc-giants-osnabrueck-1986";
    private const string Osnabrueck1986InferredTeamName = "BC Giants Osnabrück";
    private const string Osnabrueck1985InferenceSeason = "1985-1986";
    private const string Osnabrueck1985InferredTeamId = "bc-giants-osnabrueck-1985";
    private const string Osnabrueck1985InferredTeamName = "BC Giants Osnabrück";
    private const string Hagen18601985InferredTeamId = "tsv-hagen-1860-1985";
    private const string Hagen18601985InferredTeamName = "TSV Hagen 1860";
    private const string Historical1975InferenceSeason = "1975-1976";

    private static readonly IReadOnlyDictionary<string, (string TeamId, string TeamName)> HistoricalSingleMissingTeams =
        new Dictionary<string, (string TeamId, string TeamName)>
        {
            ["1976-1977"] = ("bc-usc-muenchen-1976", "BC/USC München"),
            ["1977-1978"] = ("tus-aschaffenburg-1977", "TuS Aschaffenburg"),
            ["1978-1979"] = ("tus-aschaffenburg-1978", "TuS Aschaffenburg"),
            ["1980-1981"] = ("hamburger-tb-1980", "Hamburger TB"),
            ["1982-1983"] = ("fc-schalke-04-1982", "FC Schalke 04"),
            ["1983-1984"] = ("bc-giants-osnabrueck-1983", "BC Giants Osnabrück")
        };

    private static readonly IReadOnlyDictionary<string, (string? HomeKey, string? AwayKey)> HistoricalMultiMissingFixtures =
        new Dictionary<string, (string? HomeKey, string? AwayKey)>
        {
            ["30967"] = ("hamburger", null), ["30971"] = ("frankfurt", null),
            ["30973"] = (null, "hamburger"), ["30976"] = (null, "frankfurt"),
            ["30977"] = ("hamburger", null), ["30981"] = ("frankfurt", null),
            ["30983"] = (null, "hamburger"), ["30985"] = (null, "frankfurt"),
            ["30987"] = ("hamburger", null), ["30989"] = ("frankfurt", null),
            ["30993"] = ("hamburger", "frankfurt"),
            ["30997"] = ("hamburger", null), ["31000"] = (null, "frankfurt"),
            ["31002"] = (null, "hamburger"), ["31003"] = (null, "frankfurt"),
            ["31007"] = ("hamburger", null), ["31008"] = ("frankfurt", null),
            ["31015"] = (null, "frankfurt"), ["31016"] = (null, "hamburger"),
            ["31017"] = ("hamburger", null), ["31018"] = ("frankfurt", null),
            ["31025"] = (null, "frankfurt"), ["31026"] = (null, "hamburger"),
            ["31027"] = ("hamburger", null), ["31028"] = ("frankfurt", null),
            ["31034"] = (null, "frankfurt"), ["31035"] = (null, "hamburger"),
            ["31038"] = ("frankfurt", "hamburger"),
            ["31043"] = (null, "hamburger"), ["31046"] = ("frankfurt", null),
            ["31047"] = ("hamburger", null), ["31048"] = ("frankfurt", null),
            ["31053"] = (null, "hamburger"), ["31056"] = (null, "frankfurt"),

            ["28480"] = ("art", null), ["28484"] = (null, "giants"),
            ["28487"] = ("art", null), ["28489"] = (null, "giants"),
            ["28493"] = ("art", null), ["28494"] = (null, "giants"),
            ["28496"] = (null, "art"), ["28498"] = ("giants", null),
            ["28501"] = (null, "giants"), ["28502"] = ("art", null),
            ["28507"] = ("art", null), ["28509"] = ("giants", null),
            ["28510"] = (null, "giants"), ["28512"] = (null, "art"),
            ["28517"] = ("giants", "art"),
            ["28520"] = ("art", null), ["28521"] = (null, "giants"),
            ["28525"] = (null, "art"), ["28528"] = ("giants", null),
            ["28531"] = ("art", null), ["28533"] = (null, "giants"),
            ["28536"] = ("giants", null), ["28537"] = (null, "art"),
            ["28541"] = (null, "giants"), ["28543"] = ("art", null),
            ["28545"] = (null, "art"), ["28546"] = ("giants", null),
            ["28550"] = (null, "giants"), ["28551"] = (null, "art"),
            ["28558"] = ("art", null), ["28559"] = ("giants", null),
            ["28562"] = ("art", "giants"),
            ["28565"] = (null, "art"), ["28569"] = ("giants", null),
            ["2000529"] = ("giants", null), ["2000531"] = (null, "giants"),
            ["2000535"] = ("giants", null), ["2000537"] = (null, "giants"),
            ["2000541"] = ("giants", null), ["2000545"] = (null, "giants"),
            ["2000552"] = (null, "giants"), ["2000556"] = ("giants", null),

            // 1975-1976: SG BC/USC München (M), ADB Koblenz (K), RuWa Dellwig (R).
            ["31328"] = ("adb-koblenz-1975", "ruwa-dellwig-1975"),
            ["31330"] = (null, "bc-usc-muenchen-1975"),
            ["31333"] = ("ruwa-dellwig-1975", null),
            ["31336"] = ("adb-koblenz-1975", null),
            ["31334"] = (null, "adb-koblenz-1975"),
            ["31338"] = ("adb-koblenz-1975", null),
            ["31386"] = ("ruwa-dellwig-1975", "bc-usc-muenchen-1975"),
            ["31341"] = (null, "adb-koblenz-1975"),
            ["31342"] = (null, "ruwa-dellwig-1975"),
            ["31343"] = (null, "bc-usc-muenchen-1975"),
            ["31347"] = ("ruwa-dellwig-1975", null),
            ["31349"] = ("bc-usc-muenchen-1975", null),
            ["31348"] = (null, "adb-koblenz-1975"),
            ["31354"] = ("adb-koblenz-1975", "bc-usc-muenchen-1975"),
            ["31355"] = (null, "ruwa-dellwig-1975"),
            ["31357"] = ("ruwa-dellwig-1975", null),
            ["31359"] = ("bc-usc-muenchen-1975", null),
            ["31356"] = (null, "adb-koblenz-1975"),
            ["31362"] = ("ruwa-dellwig-1975", null),
            ["31364"] = ("adb-koblenz-1975", null),
            ["31363"] = ("bc-usc-muenchen-1975", null),
            ["31368"] = (null, "ruwa-dellwig-1975"),
            ["31367"] = (null, "bc-usc-muenchen-1975"),
            ["31366"] = (null, "adb-koblenz-1975"),
            ["31371"] = ("ruwa-dellwig-1975", null),
            ["31374"] = ("adb-koblenz-1975", null),
            ["31373"] = ("bc-usc-muenchen-1975", null),
            ["31379"] = ("adb-koblenz-1975", null),
            ["31380"] = (null, "ruwa-dellwig-1975"),
            ["31378"] = (null, "bc-usc-muenchen-1975"),
            ["31383"] = ("ruwa-dellwig-1975", null),
            ["31381"] = ("bc-usc-muenchen-1975", "adb-koblenz-1975"),
            ["31389"] = ("adb-koblenz-1975", null),
            ["31390"] = (null, "bc-usc-muenchen-1975"),
            ["31388"] = (null, "ruwa-dellwig-1975"),
            ["31393"] = ("ruwa-dellwig-1975", "adb-koblenz-1975"),
            ["31395"] = ("bc-usc-muenchen-1975", null),
            ["31399"] = ("bc-usc-muenchen-1975", "ruwa-dellwig-1975"),
            ["31400"] = (null, "adb-koblenz-1975"),
            ["31404"] = (null, "ruwa-dellwig-1975"),
            ["31405"] = ("adb-koblenz-1975", null),
            ["31406"] = (null, "bc-usc-muenchen-1975"),
            ["31409"] = (null, "bc-usc-muenchen-1975"),
            ["31410"] = (null, "adb-koblenz-1975"),
            ["31411"] = (null, "ruwa-dellwig-1975"),
            ["31412"] = ("ruwa-dellwig-1975", null),
            ["31415"] = ("adb-koblenz-1975", null),
            ["31414"] = ("bc-usc-muenchen-1975", null)
        };

    private static readonly IReadOnlyDictionary<string, (string TeamId, string TeamName)> HistoricalMultiMissingTeams =
        new Dictionary<string, (string TeamId, string TeamName)>
        {
            ["bc-usc-muenchen-1975"] = ("bc-usc-muenchen-1975", "SG BC/USC München"),
            ["adb-koblenz-1975"] = ("adb-koblenz-1975", "ADB Koblenz"),
            ["ruwa-dellwig-1975"] = ("ruwa-dellwig-1975", "RuWa Dellwig"),
            ["hamburger"] = ("hamburger-tb-1979", "Hamburger TB"),
            ["frankfurt"] = ("eintracht-frankfurt-1979", "Eintracht Frankfurt"),
            ["giants"] = ("bc-giants-osnabrueck-1984", "BC Giants Osnabrück"),
            ["art"] = ("art-duesseldorf-1984", "ART Düsseldorf")
        };
    private const string HistoricalPostseasonDateInferenceSeason = "1987-1988";

    private static readonly IReadOnlyDictionary<string, (string TeamId, string TeamName)> HistoricalPostseasonInferredTeams =
        new Dictionary<string, (string TeamId, string TeamName)>
        {
            ["2000805"] = (Hagen1989InferredTeamId, Hagen1989InferredTeamName),
            ["2000806"] = (Hagen1989InferredTeamId, Hagen1989InferredTeamName),
            ["2000807"] = (Hagen1989InferredTeamId, Hagen1989InferredTeamName),
            ["2000813"] = (Bramsche1990InferredTeamId, Bramsche1990InferredTeamName),
            ["2000814"] = (Bramsche1990InferredTeamId, Bramsche1990InferredTeamName)
        };

    private static readonly IReadOnlyDictionary<string, string> Historical1985MissingTeams =
        new Dictionary<string, string>
        {
            ["28572"] = "hagen", ["28574"] = "osnabrueck",
            ["28576"] = "hagen", ["28581"] = "osnabrueck",
            ["28586"] = "osnabrueck", ["28585"] = "hagen",
            ["28598"] = "osnabrueck", ["28597"] = "hagen",
            ["28604"] = "osnabrueck", ["28601"] = "hagen",
            ["28606"] = "hagen", ["28609"] = "osnabrueck",
            ["28611"] = "osnabrueck", ["28613"] = "hagen",
            ["28618"] = "osnabrueck", ["28633"] = "hagen",
            ["28622"] = "hagen", ["28625"] = "osnabrueck",
            ["28627"] = "osnabrueck", ["28629"] = "hagen",
            ["28634"] = "osnabrueck", ["28638"] = "hagen",
            ["28640"] = "osnabrueck", ["28642"] = "hagen",
            ["28653"] = "osnabrueck", ["28658"] = "hagen"
        };
    private const string BramscheInferenceSeason = "1992-1993";
    private const string BramscheInferredTeamId = "bramsche-1992";
    private const string BramscheInferredTeamName = "BG Bramsche/Osnabrück";
    private const string DortmundInferredTeamId = "dortmund-1992";
    private const string DortmundInferredTeamName = "SVD 49 Dortmund";

    private static readonly IReadOnlyDictionary<string, (string? HomeTeam, string? AwayTeam)> Inferred1992Fixtures =
        new Dictionary<string, (string? HomeTeam, string? AwayTeam)>
        {
            ["29582"] = ("bramsche", null), ["29674"] = (null, "dortmund"),
            ["29583"] = ("bramsche", null), ["29584"] = (null, "dortmund"),
            ["29593"] = (null, "bramsche"), ["29594"] = ("dortmund", null),
            ["29596"] = ("bramsche", null), ["29597"] = (null, "dortmund"),
            ["29602"] = ("bramsche", null), ["29603"] = (null, "dortmund"),
            ["29617"] = (null, "bramsche"), ["29616"] = (null, "dortmund"),
            ["29609"] = (null, "bramsche"), ["29612"] = ("dortmund", null),
            ["29624"] = ("bramsche", "dortmund"),
            ["29626"] = ("bramsche", null), ["29630"] = (null, "dortmund"),
            ["29633"] = (null, "bramsche"), ["29636"] = ("dortmund", null),
            ["29637"] = ("bramsche", "dortmund"),
            ["29644"] = ("dortmund", null), ["29645"] = ("bramsche", null),
            ["29650"] = (null, "bramsche"), ["29651"] = ("dortmund", null),
            ["29652"] = ("bramsche", null), ["29653"] = (null, "dortmund"),
            ["29662"] = ("bramsche", null), ["29663"] = (null, "dortmund"),
            ["29665"] = (null, "bramsche"), ["29668"] = ("dortmund", null),
            ["29671"] = ("bramsche", null),
            ["29675"] = (null, "dortmund"), ["29678"] = (null, "bramsche"),
            ["29681"] = ("dortmund", null), ["29687"] = ("bramsche", null),
            ["29689"] = (null, "dortmund"), ["29694"] = ("bramsche", null),
            ["29695"] = (null, "dortmund"), ["29700"] = (null, "dortmund"),
            ["29697"] = ("bramsche", null), ["29702"] = (null, "bramsche"),
            ["29704"] = ("dortmund", null), ["29710"] = ("bramsche", "dortmund"),
            ["29714"] = (null, "bramsche"), ["29716"] = ("dortmund", null),
            ["29720"] = ("dortmund", null), ["29725"] = ("bramsche", null),
            ["29728"] = (null, "dortmund"), ["29731"] = (null, "bramsche"),
            ["29737"] = ("dortmund", null), ["29735"] = (null, "bramsche"),
            ["29740"] = ("bramsche", "dortmund"),
            ["29746"] = (null, "dortmund"), ["29748"] = (null, "bramsche"),
            ["29751"] = (null, "dortmund"), ["29754"] = ("bramsche", null),
            ["29758"] = (null, "dortmund"), ["29761"] = ("bramsche", null),
            ["29765"] = (null, "bramsche"), ["29768"] = (null, "dortmund")
        };

    private static readonly Regex NextDataScriptRegex = new(
        "<script[^>]*id=[\\\"']__NEXT_DATA__[\\\"'][^>]*>(?<json>.*?)</script>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private string? apiSecret;
    private DateTime lastRequestUtc = DateTime.MinValue;

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(country, "Germany", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        var normalizedName = leagueName.Trim().ToLowerInvariant();
        return Task.FromResult<BasketballProviderLeague?>(normalizedName switch
        {
            "bbl" or "basketball bundesliga" or "basketball-bundesliga" =>
                new(Source, LeagueSourceId, "BBL", "DE", "start_year"),
            _ => null
        });
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(league.SourceLeagueId, LeagueSourceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("German official provider only supports the BBL league.");
        }

        var startYear = SeasonLabelNormalizer.ParseStartYear(season);
        if (startYear is < FirstSeason or > LastHistoricalSeason)
        {
            throw new ArgumentException(
                $"German BBL historical coverage supports seasons from {FirstSeason}-{FirstSeason + 1} through {LastHistoricalSeason}-{LastHistoricalSeason + 1}.",
                nameof(season));
        }

        var warnings = new List<string>();
        if (!context.CanUseRequest())
        {
            return (Array.Empty<BasketballProviderGame>(), true, ["The request budget was exhausted before authenticating with the official BBL archive."]);
        }

        var secret = await GetApiSecretAsync(context, cancellationToken);
        var games = new List<BasketballProviderGame>();
        var fetchedAtUtc = DateTime.UtcNow;
        var page = 1;
        var totalPages = 1;

        do
        {
            if (!context.CanUseRequest())
            {
                warnings.Add($"The official BBL archive has additional pages for {season}, but the request budget stopped the import after page {page - 1}.");
                return (games, true, warnings);
            }

            var apiUri = BuildApiUri(startYear, page);
            using var response = await SendAsync(apiUri, secret, context, cancellationToken);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = ParsePayload(payload, season, apiUri.ToString(), fetchedAtUtc);
            games.AddRange(parsed.Games);
            warnings.AddRange(parsed.Warnings);
            totalPages = Math.Max(1, parsed.TotalPages);
            page++;
        }
        while (page <= totalPages);

        if (games.Count == 0)
        {
            warnings.Add($"The official BBL archive returned no final BBL games for {season}.");
        }

        return (games, false, warnings);
    }

    public static IReadOnlyCollection<BasketballProviderGame> ParseGames(
        string payload,
        string season,
        string sourceUrl,
        DateTime fetchedAtUtc)
        => ParsePayload(payload, season, sourceUrl, fetchedAtUtc).Games;

    private async Task<string> GetApiSecretAsync(
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(apiSecret))
        {
            return apiSecret;
        }

        var authUri = new Uri(new Uri(options.Value.OfficialBaseUrl), options.Value.AuthPagePath);
        using var response = await SendAsync(authUri, secret: null, context, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var match = NextDataScriptRegex.Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException("The official BBL archive page did not expose its Next.js bootstrap data.");
        }

        using var document = JsonDocument.Parse(match.Groups["json"].Value);
        if (!document.RootElement.TryGetProperty("props", out var props) ||
            !props.TryGetProperty("pageProps", out var pageProps) ||
            !pageProps.TryGetProperty("key", out var keyElement) ||
            string.IsNullOrWhiteSpace(keyElement.GetString()))
        {
            throw new InvalidOperationException("The official BBL archive page did not expose a usable API credential.");
        }

        apiSecret = keyElement.GetString();
        return apiSecret!;
    }

    private async Task<HttpResponseMessage> SendAsync(
        Uri uri,
        string? secret,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        return await BackfillHttpRetryPolicy.SendAsync(
            async retryCancellationToken =>
            {
                await WaitForRateLimitAsync(retryCancellationToken);
                context.ConsumeRequest();

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd(options.Value.UserAgent);
                request.Headers.TryAddWithoutValidation(ApiKeyHeader, ApiKey);
                if (!string.IsNullOrWhiteSpace(secret))
                {
                    request.Headers.TryAddWithoutValidation(ApiSecretHeader, secret);
                }

                return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, retryCancellationToken);
            },
            options.Value.MaxTransientRetries,
            options.Value.RetryBaseDelayMilliseconds,
            cancellationToken);
    }

    private async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(0, options.Value.MinRequestIntervalMilliseconds));
        var remaining = interval - (DateTime.UtcNow - lastRequestUtc);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken);
        }

        lastRequestUtc = DateTime.UtcNow;
    }

    private Uri BuildApiUri(int startYear, int page)
    {
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"games?currentPage={page}&pageSize={Math.Max(1, options.Value.PageSize)}&gameType=FINISHED&seasonId={startYear}&competition={LeagueSourceId}");
        return new Uri(new Uri(options.Value.ApiBaseUrl), query);
    }

    private static (IReadOnlyCollection<BasketballProviderGame> Games, IReadOnlyCollection<string> Warnings, int TotalPages) ParsePayload(
        string payload,
        string season,
        string sourceUrl,
        DateTime fetchedAtUtc)
    {
        var response = JsonSerializer.Deserialize<BblGamesResponse>(payload, JsonOptions)
            ?? throw new InvalidOperationException("The official BBL archive returned an empty response.");
        var games = new List<BasketballProviderGame>();
        var nonOfficial = 0;
        var scoreless = 0;
        var malformed = 0;
        var inferred = 0;
        var hertenInferred = 0;
        var paderbornInferred = 0;
        var bramscheDortmundInferred = 0;
        var bramsche1991Inferred = 0;
        var bramsche1990Inferred = 0;
        var hagen1989Inferred = 0;
        var hagen1988Inferred = 0;
        var osnabrueck1986Inferred = 0;
        var osnabrueck1985Inferred = 0;
        var historicalRosterInferred = 0;
        var postseasonDateInferred = 0;
        var postseasonTeamInferred = 0;
        var roundDates = BuildRoundDates(response.Items ?? []);
        var postseasonFallbackDates = UsesHistoricalPostseasonDateInference(season)
            ? BuildHistoricalPostseasonFallbackDates(response.Items ?? [])
            : new Dictionary<string, DateTimeOffset>();

        foreach (var item in response.Items ?? [])
        {
            if (!string.Equals(item.Status, "OFFICIAL", StringComparison.OrdinalIgnoreCase))
            {
                nonOfficial++;
                continue;
            }

            if (item.Result?.HomeTeamFinalScore is null || item.Result.GuestTeamFinalScore is null)
            {
                scoreless++;
                continue;
            }

            var homeTeam = item.HomeTeam;
            var guestTeam = item.GuestTeam;
            var hasHomeTeam = HasTeam(homeTeam);
            var hasGuestTeam = HasTeam(guestTeam);
            var hasScheduledTime = DateTimeOffset.TryParse(
                item.ScheduledTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var scheduledTime);
            var inferredGame = false;
            var inferredHertenGame = false;
            var inferredPaderbornGame = false;
            var inferredBramscheDortmundGame = false;
            var inferredBramsche1991Game = false;
            var inferredBramsche1990Game = false;
            var inferredHagen1989Game = false;
            var inferredHagen1988Game = false;
            var inferredOsnabrueck1986Game = false;
            var inferredOsnabrueck1985Game = false;
            var inferredHistoricalRosterGame = false;
            var inferredPostseasonDate = false;
            var inferredPostseasonTeam = false;

            if (UsesHistoricalPostseasonDateInference(season) &&
                !hasScheduledTime &&
                item.Id is not null &&
                postseasonFallbackDates.TryGetValue(item.Id, out var inferredPostseasonScheduledTime))
            {
                scheduledTime = inferredPostseasonScheduledTime;
                hasScheduledTime = true;
                inferredPostseasonDate = true;
                postseasonDateInferred++;
            }

            if (UsesHistoricalPostseasonDateInference(season) &&
                TryResolveHistoricalPostseasonTeam(item, out var inferredPostseasonHomeTeam, out var inferredPostseasonGuestTeam))
            {
                homeTeam = inferredPostseasonHomeTeam;
                guestTeam = inferredPostseasonGuestTeam;
                hasHomeTeam = HasTeam(homeTeam);
                hasGuestTeam = HasTeam(guestTeam);
                inferredPostseasonTeam = true;
                postseasonTeamInferred++;
            }

            if (string.Equals(season, Osnabrueck1986InferenceSeason, StringComparison.Ordinal) &&
                string.Equals(item.Stage, "MAIN_ROUND", StringComparison.OrdinalIgnoreCase) &&
                hasHomeTeam != hasGuestTeam &&
                hasScheduledTime &&
                TryResolve1986OsnabrueckGame(item, out var inferred1986HomeTeam, out var inferred1986GuestTeam))
            {
                homeTeam = inferred1986HomeTeam;
                guestTeam = inferred1986GuestTeam;
                hasHomeTeam = HasTeam(homeTeam);
                hasGuestTeam = HasTeam(guestTeam);
                inferredOsnabrueck1986Game = true;
                osnabrueck1986Inferred++;
            }

            if (!inferredOsnabrueck1986Game &&
                string.Equals(season, Osnabrueck1985InferenceSeason, StringComparison.Ordinal) &&
                string.Equals(item.Stage, "MAIN_ROUND", StringComparison.OrdinalIgnoreCase) &&
                TryResolve1985MissingTeamGame(item, out var inferred1985HomeTeam, out var inferred1985GuestTeam))
            {
                homeTeam = inferred1985HomeTeam;
                guestTeam = inferred1985GuestTeam;
                hasHomeTeam = HasTeam(homeTeam);
                hasGuestTeam = HasTeam(guestTeam);
                inferredOsnabrueck1985Game = true;
                osnabrueck1985Inferred++;
            }

            if (!inferredOsnabrueck1986Game &&
                !inferredOsnabrueck1985Game &&
                TryResolveHistoricalMissingTeamGame(item, season, out var inferredHistoricalHomeTeam, out var inferredHistoricalGuestTeam))
            {
                homeTeam = inferredHistoricalHomeTeam;
                guestTeam = inferredHistoricalGuestTeam;
                hasHomeTeam = HasTeam(homeTeam);
                hasGuestTeam = HasTeam(guestTeam);
                inferredHistoricalRosterGame = true;
                historicalRosterInferred++;
            }

            if (!inferredOsnabrueck1986Game &&
                string.Equals(season, Hagen1988InferenceSeason, StringComparison.Ordinal) &&
                string.Equals(item.Stage, "MAIN_ROUND", StringComparison.OrdinalIgnoreCase) &&
                hasHomeTeam != hasGuestTeam &&
                hasScheduledTime &&
                TryResolve1988HagenGame(item, out var inferred1988HomeTeam, out var inferred1988GuestTeam))
            {
                homeTeam = inferred1988HomeTeam;
                guestTeam = inferred1988GuestTeam;
                hasHomeTeam = HasTeam(homeTeam);
                hasGuestTeam = HasTeam(guestTeam);
                inferredHagen1988Game = true;
                hagen1988Inferred++;
            }

            if (!inferredOsnabrueck1986Game &&
                !inferredHagen1988Game &&
                string.Equals(season, Hagen1989InferenceSeason, StringComparison.Ordinal) &&
                string.Equals(item.Stage, "MAIN_ROUND", StringComparison.OrdinalIgnoreCase) &&
                hasHomeTeam != hasGuestTeam &&
                hasScheduledTime &&
                TryResolve1989HagenGame(item, out var inferred1989HomeTeam, out var inferred1989GuestTeam))
            {
                homeTeam = inferred1989HomeTeam;
                guestTeam = inferred1989GuestTeam;
                hasHomeTeam = HasTeam(homeTeam);
                hasGuestTeam = HasTeam(guestTeam);
                inferredHagen1989Game = true;
                hagen1989Inferred++;
            }

            if (!inferredOsnabrueck1986Game &&
                !inferredHagen1988Game &&
                !inferredHagen1989Game &&
                string.Equals(season, Bramsche1990InferenceSeason, StringComparison.Ordinal) &&
                string.Equals(item.Stage, "MAIN_ROUND", StringComparison.OrdinalIgnoreCase) &&
                hasHomeTeam != hasGuestTeam &&
                hasScheduledTime &&
                TryResolve1990BramscheGame(item, out var inferred1990HomeTeam, out var inferred1990GuestTeam))
            {
                homeTeam = inferred1990HomeTeam;
                guestTeam = inferred1990GuestTeam;
                hasHomeTeam = HasTeam(homeTeam);
                hasGuestTeam = HasTeam(guestTeam);
                inferredBramsche1990Game = true;
                bramsche1990Inferred++;
            }

            if (!inferredOsnabrueck1986Game &&
                !inferredHagen1988Game &&
                !inferredHagen1989Game &&
                !inferredBramsche1990Game &&
                string.Equals(season, Bramsche1991InferenceSeason, StringComparison.Ordinal) &&
                string.Equals(item.Stage, "MAIN_ROUND", StringComparison.OrdinalIgnoreCase) &&
                hasHomeTeam != hasGuestTeam &&
                hasScheduledTime &&
                TryResolve1991BramscheGame(item, out var inferred1991HomeTeam, out var inferred1991GuestTeam))
            {
                homeTeam = inferred1991HomeTeam;
                guestTeam = inferred1991GuestTeam;
                hasHomeTeam = HasTeam(homeTeam);
                hasGuestTeam = HasTeam(guestTeam);
                inferredBramsche1991Game = true;
                bramsche1991Inferred++;
            }

            if (!inferredOsnabrueck1986Game &&
                !inferredHagen1988Game &&
                !inferredHagen1989Game &&
                !inferredBramsche1990Game &&
                !inferredBramsche1991Game &&
                string.Equals(season, BramscheInferenceSeason, StringComparison.Ordinal) &&
                string.Equals(item.Stage, "MAIN_ROUND", StringComparison.OrdinalIgnoreCase) &&
                TryResolve1992BramscheDortmundGame(item, out var inferred1992HomeTeam, out var inferred1992GuestTeam))
            {
                homeTeam = inferred1992HomeTeam;
                guestTeam = inferred1992GuestTeam;
                hasHomeTeam = HasTeam(homeTeam);
                hasGuestTeam = HasTeam(guestTeam);
                inferredBramscheDortmundGame = true;
                bramscheDortmundInferred++;
            }

            if (!inferredOsnabrueck1986Game &&
                !inferredHagen1988Game &&
                !inferredHagen1989Game &&
                !inferredBramsche1990Game &&
                !inferredBramsche1991Game &&
                !inferredBramscheDortmundGame &&
                string.Equals(season, PaderbornInferenceSeason, StringComparison.Ordinal) &&
                string.Equals(item.Stage, "MAIN_ROUND", StringComparison.OrdinalIgnoreCase) &&
                hasHomeTeam != hasGuestTeam &&
                hasScheduledTime &&
                TryResolve1994PaderbornGame(
                    item,
                    out var paderbornHomeTeam,
                    out var paderbornGuestTeam))
            {
                homeTeam = paderbornHomeTeam;
                guestTeam = paderbornGuestTeam;
                hasHomeTeam = HasTeam(homeTeam);
                hasGuestTeam = HasTeam(guestTeam);
                inferredPaderbornGame = true;
                paderbornInferred++;
            }

            if (!inferredOsnabrueck1986Game &&
                !inferredHagen1988Game &&
                !inferredHagen1989Game &&
                !inferredBramsche1990Game &&
                !inferredBramsche1991Game &&
                !inferredBramscheDortmundGame &&
                !inferredPaderbornGame &&
                string.Equals(season, HertenInferenceSeason, StringComparison.Ordinal) &&
                string.Equals(item.Stage, "MAIN_ROUND", StringComparison.OrdinalIgnoreCase) &&
                hasHomeTeam != hasGuestTeam &&
                hasScheduledTime &&
                TryResolve1995HertenGame(
                    item,
                    out var hertenHomeTeam,
                    out var hertenGuestTeam))
            {
                homeTeam = hertenHomeTeam;
                guestTeam = hertenGuestTeam;
                hasHomeTeam = HasTeam(homeTeam);
                hasGuestTeam = HasTeam(guestTeam);
                inferredHertenGame = true;
                hertenInferred++;
            }

            if (!inferredOsnabrueck1986Game &&
                !inferredHagen1988Game &&
                !inferredHagen1989Game &&
                !inferredBramsche1990Game &&
                !inferredBramsche1991Game &&
                !inferredBramscheDortmundGame &&
                !inferredPaderbornGame &&
                !inferredHertenGame &&
                string.Equals(season, InferenceSeason, StringComparison.Ordinal) &&
                string.Equals(item.Stage, "MAIN_ROUND", StringComparison.OrdinalIgnoreCase) &&
                hasHomeTeam != hasGuestTeam &&
                TryResolve1996BraunschweigGame(
                    item,
                    hasScheduledTime ? scheduledTime : null,
                    roundDates,
                    out var inferredHomeTeam,
                    out var inferredGuestTeam,
                    out var inferredScheduledTime))
            {
                homeTeam = inferredHomeTeam;
                guestTeam = inferredGuestTeam;
                scheduledTime = inferredScheduledTime;
                hasHomeTeam = HasTeam(homeTeam);
                hasGuestTeam = HasTeam(guestTeam);
                hasScheduledTime = true;
                inferredGame = true;
                inferred++;
            }

            if (!hasHomeTeam || !hasGuestTeam || !hasScheduledTime)
            {
                malformed++;
                continue;
            }

            var homeId = homeTeam!.TeamId ?? homeTeam.SeasonTeamId!;
            var awayId = guestTeam!.TeamId ?? guestTeam.SeasonTeamId!;
            var stage = item.Stage ?? "MAIN_ROUND";
            games.Add(new BasketballProviderGame(
                Source,
                item.SourceId ?? item.Id ?? $"bbl:{season}:{games.Count + 1}",
                scheduledTime.UtcDateTime,
                "finished",
                homeId,
                homeTeam.Name!,
                awayId,
                guestTeam.Name!,
                ToShort(item.Result.HomeTeamFinalScore.Value),
                ToShort(item.Result.GuestTeamFinalScore.Value),
                new BasketballProviderGameProvenance(
                    sourceUrl,
                    season,
                    fetchedAtUtc,
                    inferredPostseasonTeam
                        ? HistoricalPostseasonTeamAndDateInferredParserVersion
                        : inferredPostseasonDate
                            ? HistoricalPostseasonDateInferredParserVersion
                            : inferredOsnabrueck1985Game
                                ? Osnabrueck1985InferredParserVersion
                            : inferredOsnabrueck1986Game
                                ? Osnabrueck1986InferredParserVersion
                            : inferredHistoricalRosterGame
                                ? season == Historical1975InferenceSeason
                                    ? Historical1975RosterInferredParserVersion
                                    : HistoricalRosterInferredParserVersion
                            : inferredHagen1988Game
                            ? Hagen1988InferredParserVersion
                            : inferredHagen1989Game
                            ? Hagen1989InferredParserVersion
                            : inferredBramsche1990Game
                            ? Bramsche1990InferredParserVersion
                            : inferredBramsche1991Game
                            ? Bramsche1991InferredParserVersion
                            : inferredBramscheDortmundGame
                            ? BramscheDortmundInferredParserVersion
                            : inferredPaderbornGame
                        ? PaderbornInferredParserVersion
                            : inferredHertenGame
                                ? HertenInferredParserVersion
                                : inferredGame
                                    ? InferredParserVersion
                                    : ParserVersion,
                    inferredPostseasonTeam
                        ? $"{item.Id};inferred-team={(!HasTeam(item.HomeTeam) ? homeId : awayId)};inferred-date-after-regular-season;stage={item.Stage}"
                        : inferredPostseasonDate
                            ? $"{item.Id};inferred-date-after-regular-season;stage={item.Stage}"
                            : inferredOsnabrueck1985Game
                                ? $"{item.Id};inferred-team-from-12-team-1985-1986-roster"
                            : inferredOsnabrueck1986Game
                                ? $"{item.Id};inferred-team={Osnabrueck1986InferredTeamId};roster=11-team-1986-1987"
                            : inferredHistoricalRosterGame
                                ? $"{item.Id};inferred-historical-roster-season={season}"
                            : inferredHagen1988Game
                            ? $"{item.Id};inferred-team={Hagen1988InferredTeamId};roster=12-team-1988-1989"
                            : inferredHagen1989Game
                            ? $"{item.Id};inferred-team={Hagen1989InferredTeamId};roster=12-team-1989-1990"
                            : inferredBramsche1990Game
                            ? $"{item.Id};inferred-team={Bramsche1990InferredTeamId};roster=12-team-1990-1991"
                            : inferredBramsche1991Game
                            ? $"{item.Id};inferred-team={Bramsche1991InferredTeamId};roster=12-team-1991-1992"
                            : inferredBramscheDortmundGame
                            ? $"{item.Id};inferred-1992-teams={homeId},{awayId}"
                            : inferredPaderbornGame
                            ? $"{item.Id};inferred-team={PaderbornInferredTeamId};inferred-roster=12-team-1994-1995"
                            : inferredHertenGame
                                ? $"{item.Id};inferred-team={HertenInferredTeamId};inferred-roster=14-team-1995-1996"
                                : inferredGame
                                    ? $"{item.Id};inferred-team={InferredTeamId};inferred-date-from-round;api-date={item.ScheduledTime}"
                                    : item.Id),
                null,
                CompetitionPhaseFor(stage),
                CompetitionRoundFor(stage),
                "DE",
                "DE"));
        }

        var warnings = new List<string>();
        if (nonOfficial > 0)
        {
            warnings.Add($"Skipped {nonOfficial} non-official BBL records for {season}.");
        }
        if (scoreless > 0)
        {
            warnings.Add($"Skipped {scoreless} official BBL records without final scores for {season}.");
        }
        if (malformed > 0)
        {
            warnings.Add($"Skipped {malformed} official BBL records with incomplete teams or dates for {season}.");
        }
        if (inferred > 0)
        {
            warnings.Add($"Inferred {inferred} official {InferenceSeason} fixtures involving {InferredTeamName} (team {InferredTeamId}) from the unique season schedule; original API dates are retained in provenance.");
        }
        if (hertenInferred > 0)
        {
            warnings.Add($"Inferred {hertenInferred} official {HertenInferenceSeason} fixtures involving {HertenInferredTeamName} from the official 14-team season roster; the source omitted Herten from one team field.");
        }
        if (paderbornInferred > 0)
        {
            warnings.Add($"Inferred {paderbornInferred} official {PaderbornInferenceSeason} fixtures involving {PaderbornInferredTeamName} from the official 12-team season roster; the source omitted Paderborn from one team field.");
        }
        if (bramscheDortmundInferred > 0)
        {
            warnings.Add($"Inferred {bramscheDortmundInferred} official {BramscheInferenceSeason} fixtures involving {BramscheInferredTeamName} and {DortmundInferredTeamName} from the 12-team round schedule and published season records; the source omitted both teams from 64 team fields.");
        }
        if (bramsche1991Inferred > 0)
        {
            warnings.Add($"Inferred {bramsche1991Inferred} official {Bramsche1991InferenceSeason} fixtures involving {Bramsche1991InferredTeamName} from the official 12-team season roster; the source omitted Bramsche from one team field.");
        }
        if (bramsche1990Inferred > 0)
        {
            warnings.Add($"Inferred {bramsche1990Inferred} official {Bramsche1990InferenceSeason} fixtures involving {Bramsche1990InferredTeamName} from the official 12-team season roster; the source omitted Bramsche from one team field.");
        }
        if (hagen1989Inferred > 0)
        {
            warnings.Add($"Inferred {hagen1989Inferred} official {Hagen1989InferenceSeason} fixtures involving {Hagen1989InferredTeamName} from the official 12-team season roster; the source omitted the second Hagen team from one team field.");
        }
        if (hagen1988Inferred > 0)
        {
            warnings.Add($"Inferred {hagen1988Inferred} official {Hagen1988InferenceSeason} fixtures involving {Hagen1988InferredTeamName} from the official 12-team season roster; the source omitted the second Hagen team from one team field.");
        }
        if (osnabrueck1986Inferred > 0)
        {
            warnings.Add($"Inferred {osnabrueck1986Inferred} official {Osnabrueck1986InferenceSeason} fixtures involving {Osnabrueck1986InferredTeamName} from the official 11-team season roster; the source omitted Osnabrueck from one team field.");
        }
        if (osnabrueck1985Inferred > 0)
        {
            warnings.Add($"Inferred {osnabrueck1985Inferred} official {Osnabrueck1985InferenceSeason} team fields involving {Osnabrueck1985InferredTeamName} and {Hagen18601985InferredTeamName} from the archived 12-team season schedule; the source omitted these teams from the feed.");
        }
        if (historicalRosterInferred > 0)
        {
            warnings.Add($"Inferred {historicalRosterInferred} official historical fixtures from published season rosters and round-robin records; the source omitted one or more team fields.");
        }
        if (postseasonDateInferred > 0)
        {
            warnings.Add($"Inferred dates for {postseasonDateInferred} official {season} postseason records with null source dates; records were ordered by playoff stage and spaced one day apart after the regular season.");
        }
        if (postseasonTeamInferred > 0)
        {
            warnings.Add($"Inferred opponents for {postseasonTeamInferred} official {season} postseason records from the published playoff bracket; the source omitted one team field.");
        }

        return (games, warnings, Math.Max(1, response.TotalPages));
    }

    private static string CompetitionPhaseFor(string stage) => stage.ToUpperInvariant() switch
    {
        "MAIN_ROUND" => "Regular Season",
        "PLAYINS" or "QUALIFICATION" => "Play-In / Qualification",
        "ROUND_OF_8" or "SEMI_FINALS" or "FINALS" => "Playoffs",
        _ => "Other"
    };

    private static string CompetitionRoundFor(string stage) => stage.ToUpperInvariant() switch
    {
        "MAIN_ROUND" => "Regular Season",
        "PLAYINS" => "Play-In",
        "QUALIFICATION" => "Qualification",
        "ROUND_OF_8" => "Quarterfinals",
        "SEMI_FINALS" => "Semifinals",
        "FINALS" => "Finals",
        _ => stage
    };

    private static short ToShort(int value) => checked((short)value);

    private static bool HasTeam(BblTeam? team) =>
        team is not null &&
        !string.IsNullOrWhiteSpace(team.Name) &&
        !string.IsNullOrWhiteSpace(team.TeamId ?? team.SeasonTeamId);

    private static bool TryResolve1995HertenGame(
        BblGame item,
        out BblTeam homeTeam,
        out BblTeam guestTeam)
    {
        var missingHome = !HasTeam(item.HomeTeam);
        var missingGuest = !HasTeam(item.GuestTeam);
        if (missingHome == missingGuest)
        {
            homeTeam = null!;
            guestTeam = null!;
            return false;
        }

        var inferredTeam = new BblTeam
        {
            TeamId = HertenInferredTeamId,
            SeasonTeamId = HertenInferredTeamId,
            Name = HertenInferredTeamName
        };
        homeTeam = missingHome ? inferredTeam : item.HomeTeam!;
        guestTeam = missingGuest ? inferredTeam : item.GuestTeam!;
        return true;
    }

    private static bool TryResolve1994PaderbornGame(
        BblGame item,
        out BblTeam homeTeam,
        out BblTeam guestTeam)
    {
        var missingHome = !HasTeam(item.HomeTeam);
        var missingGuest = !HasTeam(item.GuestTeam);
        if (missingHome == missingGuest)
        {
            homeTeam = null!;
            guestTeam = null!;
            return false;
        }

        var inferredTeam = new BblTeam
        {
            TeamId = PaderbornInferredTeamId,
            SeasonTeamId = PaderbornInferredTeamId,
            Name = PaderbornInferredTeamName
        };
        homeTeam = missingHome ? inferredTeam : item.HomeTeam!;
        guestTeam = missingGuest ? inferredTeam : item.GuestTeam!;
        return true;
    }

    private static bool TryResolve1992BramscheDortmundGame(
        BblGame item,
        out BblTeam homeTeam,
        out BblTeam guestTeam)
    {
        var sourceId = item.Id ?? item.SourceId;
        if (string.IsNullOrWhiteSpace(sourceId) ||
            !Inferred1992Fixtures.TryGetValue(sourceId, out var inference))
        {
            homeTeam = null!;
            guestTeam = null!;
            return false;
        }

        homeTeam = inference.HomeTeam is null
            ? item.HomeTeam!
            : CreateInferred1992Team(inference.HomeTeam);
        guestTeam = inference.AwayTeam is null
            ? item.GuestTeam!
            : CreateInferred1992Team(inference.AwayTeam);
        return HasTeam(homeTeam) && HasTeam(guestTeam);
    }

    private static bool TryResolve1991BramscheGame(
        BblGame item,
        out BblTeam homeTeam,
        out BblTeam guestTeam)
    {
        var missingHome = !HasTeam(item.HomeTeam);
        var missingGuest = !HasTeam(item.GuestTeam);
        if (missingHome == missingGuest)
        {
            homeTeam = null!;
            guestTeam = null!;
            return false;
        }

        var inferredTeam = new BblTeam
        {
            TeamId = Bramsche1991InferredTeamId,
            SeasonTeamId = Bramsche1991InferredTeamId,
            Name = Bramsche1991InferredTeamName
        };
        homeTeam = missingHome ? inferredTeam : item.HomeTeam!;
        guestTeam = missingGuest ? inferredTeam : item.GuestTeam!;
        return true;
    }

    private static bool TryResolve1990BramscheGame(
        BblGame item,
        out BblTeam homeTeam,
        out BblTeam guestTeam)
    {
        var missingHome = !HasTeam(item.HomeTeam);
        var missingGuest = !HasTeam(item.GuestTeam);
        if (missingHome == missingGuest)
        {
            homeTeam = null!;
            guestTeam = null!;
            return false;
        }

        var inferredTeam = new BblTeam
        {
            TeamId = Bramsche1990InferredTeamId,
            SeasonTeamId = Bramsche1990InferredTeamId,
            Name = Bramsche1990InferredTeamName
        };
        homeTeam = missingHome ? inferredTeam : item.HomeTeam!;
        guestTeam = missingGuest ? inferredTeam : item.GuestTeam!;
        return true;
    }

    private static bool TryResolve1989HagenGame(
        BblGame item,
        out BblTeam homeTeam,
        out BblTeam guestTeam)
    {
        var missingHome = !HasTeam(item.HomeTeam);
        var missingGuest = !HasTeam(item.GuestTeam);
        if (missingHome == missingGuest)
        {
            homeTeam = null!;
            guestTeam = null!;
            return false;
        }

        var inferredTeam = new BblTeam
        {
            TeamId = Hagen1989InferredTeamId,
            SeasonTeamId = Hagen1989InferredTeamId,
            Name = Hagen1989InferredTeamName
        };
        homeTeam = missingHome ? inferredTeam : item.HomeTeam!;
        guestTeam = missingGuest ? inferredTeam : item.GuestTeam!;
        return true;
    }

    private static bool TryResolve1988HagenGame(
        BblGame item,
        out BblTeam homeTeam,
        out BblTeam guestTeam)
    {
        var missingHome = !HasTeam(item.HomeTeam);
        var missingGuest = !HasTeam(item.GuestTeam);
        if (missingHome == missingGuest)
        {
            homeTeam = null!;
            guestTeam = null!;
            return false;
        }

        var inferredTeam = new BblTeam
        {
            TeamId = Hagen1988InferredTeamId,
            SeasonTeamId = Hagen1988InferredTeamId,
            Name = Hagen1988InferredTeamName
        };
        homeTeam = missingHome ? inferredTeam : item.HomeTeam!;
        guestTeam = missingGuest ? inferredTeam : item.GuestTeam!;
        return true;
    }

    private static bool TryResolve1986OsnabrueckGame(
        BblGame item,
        out BblTeam homeTeam,
        out BblTeam guestTeam)
    {
        var missingHome = !HasTeam(item.HomeTeam);
        var missingGuest = !HasTeam(item.GuestTeam);
        if (missingHome == missingGuest)
        {
            homeTeam = null!;
            guestTeam = null!;
            return false;
        }

        var inferredTeam = new BblTeam
        {
            TeamId = Osnabrueck1986InferredTeamId,
            SeasonTeamId = Osnabrueck1986InferredTeamId,
            Name = Osnabrueck1986InferredTeamName
        };
        homeTeam = missingHome ? inferredTeam : item.HomeTeam!;
        guestTeam = missingGuest ? inferredTeam : item.GuestTeam!;
        return true;
    }

    private static bool TryResolve1985MissingTeamGame(
        BblGame item,
        out BblTeam homeTeam,
        out BblTeam guestTeam)
    {
        var sourceId = item.Id ?? item.SourceId;
        var missingHome = !HasTeam(item.HomeTeam);
        var missingGuest = !HasTeam(item.GuestTeam);
        if (sourceId is null)
        {
            homeTeam = null!;
            guestTeam = null!;
            return false;
        }

        if (sourceId == "28590")
        {
            homeTeam = Create1985InferredTeam("osnabrueck");
            guestTeam = Create1985InferredTeam("hagen");
            return true;
        }

        if (sourceId == "28651")
        {
            homeTeam = Create1985InferredTeam("hagen");
            guestTeam = Create1985InferredTeam("osnabrueck");
            return true;
        }

        if (!Historical1985MissingTeams.TryGetValue(sourceId, out var missingTeam) ||
            missingHome == missingGuest)
        {
            homeTeam = null!;
            guestTeam = null!;
            return false;
        }

        var inferredTeam = Create1985InferredTeam(missingTeam);
        homeTeam = missingHome ? inferredTeam : item.HomeTeam!;
        guestTeam = missingGuest ? inferredTeam : item.GuestTeam!;
        return true;
    }

    private static BblTeam Create1985InferredTeam(string key) => key switch
    {
        "osnabrueck" => new BblTeam
        {
            TeamId = Osnabrueck1985InferredTeamId,
            SeasonTeamId = Osnabrueck1985InferredTeamId,
            Name = Osnabrueck1985InferredTeamName
        },
        "hagen" => new BblTeam
        {
            TeamId = Hagen18601985InferredTeamId,
            SeasonTeamId = Hagen18601985InferredTeamId,
            Name = Hagen18601985InferredTeamName
        },
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown 1985 inferred team.")
    };

    private static bool TryResolveHistoricalMissingTeamGame(
        BblGame item,
        string season,
        out BblTeam homeTeam,
        out BblTeam guestTeam)
    {
        var sourceId = item.Id ?? item.SourceId;
        var missingHome = !HasTeam(item.HomeTeam);
        var missingGuest = !HasTeam(item.GuestTeam);
        if (sourceId is null)
        {
            homeTeam = null!;
            guestTeam = null!;
            return false;
        }

        if (HistoricalMultiMissingFixtures.TryGetValue(sourceId, out var fixture))
        {
            if (missingHome && missingGuest && fixture.HomeKey is not null && fixture.AwayKey is not null)
            {
                homeTeam = CreateHistoricalMissingTeam(fixture.HomeKey);
                guestTeam = CreateHistoricalMissingTeam(fixture.AwayKey);
                return true;
            }

            if (missingHome && fixture.HomeKey is not null && !missingGuest)
            {
                homeTeam = CreateHistoricalMissingTeam(fixture.HomeKey);
                guestTeam = item.GuestTeam!;
                return true;
            }

            if (missingGuest && fixture.AwayKey is not null && !missingHome)
            {
                homeTeam = item.HomeTeam!;
                guestTeam = CreateHistoricalMissingTeam(fixture.AwayKey);
                return true;
            }
        }

        if (!HistoricalSingleMissingTeams.TryGetValue(season, out var inferred) ||
            missingHome == missingGuest)
        {
            homeTeam = null!;
            guestTeam = null!;
            return false;
        }

        var inferredTeam = new BblTeam
        {
            TeamId = inferred.TeamId,
            SeasonTeamId = inferred.TeamId,
            Name = inferred.TeamName
        };
        homeTeam = missingHome ? inferredTeam : item.HomeTeam!;
        guestTeam = missingGuest ? inferredTeam : item.GuestTeam!;
        return true;
    }

    private static BblTeam CreateHistoricalMissingTeam(string key)
    {
        var inferred = HistoricalMultiMissingTeams[key];
        return new BblTeam
        {
            TeamId = inferred.TeamId,
            SeasonTeamId = inferred.TeamId,
            Name = inferred.TeamName
        };
    }

    private static BblTeam CreateInferred1992Team(string key) => key switch
    {
        "bramsche" => new BblTeam
        {
            TeamId = BramscheInferredTeamId,
            SeasonTeamId = BramscheInferredTeamId,
            Name = BramscheInferredTeamName
        },
        "dortmund" => new BblTeam
        {
            TeamId = DortmundInferredTeamId,
            SeasonTeamId = DortmundInferredTeamId,
            Name = DortmundInferredTeamName
        },
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown 1992 inferred team.")
    };

    private static bool UsesHistoricalPostseasonDateInference(string season) =>
        season is "1982-1983" or "1984-1985" or "1985-1986" or "1987-1988" or "1988-1989" or "1989-1990" or "1990-1991";

    private static bool TryResolveHistoricalPostseasonTeam(
        BblGame item,
        out BblTeam homeTeam,
        out BblTeam guestTeam)
    {
        var sourceId = item.Id ?? item.SourceId;
        var missingHome = !HasTeam(item.HomeTeam);
        var missingGuest = !HasTeam(item.GuestTeam);
        if (sourceId is null ||
            !HistoricalPostseasonInferredTeams.TryGetValue(sourceId, out var inferred) ||
            missingHome == missingGuest)
        {
            homeTeam = null!;
            guestTeam = null!;
            return false;
        }

        var inferredTeam = new BblTeam
        {
            TeamId = inferred.TeamId,
            SeasonTeamId = inferred.TeamId,
            Name = inferred.TeamName
        };
        homeTeam = missingHome ? inferredTeam : item.HomeTeam!;
        guestTeam = missingGuest ? inferredTeam : item.GuestTeam!;
        return true;
    }

    private static IReadOnlyDictionary<string, DateTimeOffset> BuildHistoricalPostseasonFallbackDates(
        IReadOnlyCollection<BblGame> items)
    {
        var regularSeasonDates = items
            .Where(item => string.Equals(item.Stage, "MAIN_ROUND", StringComparison.OrdinalIgnoreCase))
            .Select(item => DateTimeOffset.TryParse(
                item.ScheduledTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var date)
                ? date.UtcDateTime.Date
                : (DateTime?)null)
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .ToArray();

        if (regularSeasonDates.Length == 0)
        {
            return new Dictionary<string, DateTimeOffset>();
        }

        var fallbackDates = new Dictionary<string, DateTimeOffset>();
        var stageEnd = regularSeasonDates.Max();
        foreach (var stage in new[] { "PLAYINS", "QUALIFICATION", "ROUND_OF_8", "SEMI_FINALS", "FINALS" })
        {
            var stageItems = items
                .Where(item => string.Equals(item.Stage, stage, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var knownStageDates = stageItems
                .Select(item => DateTimeOffset.TryParse(
                    item.ScheduledTime,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var date)
                    ? date.UtcDateTime.Date
                    : (DateTime?)null)
                .Where(date => date.HasValue)
                .Select(date => date!.Value)
                .ToArray();
            var missingStageItems = stageItems
                .Where(item => string.IsNullOrWhiteSpace(item.ScheduledTime) && item.Id is not null)
                .ToArray();
            var knownStageEnd = knownStageDates.Length > 0 ? knownStageDates.Max() : stageEnd;
            var nextDate = stage == "ROUND_OF_8"
                ? stageEnd.AddDays(1)
                : (stageEnd > knownStageEnd ? stageEnd : knownStageEnd).AddDays(1);

            foreach (var item in missingStageItems)
            {
                fallbackDates[item.Id!] = new DateTimeOffset(
                    nextDate.Year,
                    nextDate.Month,
                    nextDate.Day,
                    12,
                    0,
                    0,
                    TimeSpan.Zero);
                nextDate = nextDate.AddDays(1);
            }

            var inferredStageEnd = missingStageItems.Length > 0 ? nextDate.AddDays(-1) : stageEnd;
            stageEnd = new[] { stageEnd, knownStageEnd, inferredStageEnd }.Max();
        }

        return fallbackDates;
    }

    private static IReadOnlyDictionary<int, HashSet<DateOnly>> BuildRoundDates(
        IReadOnlyCollection<BblGame> items)
    {
        var dates = new Dictionary<int, HashSet<DateOnly>>();
        foreach (var item in items)
        {
            if (!string.Equals(item.Status, "OFFICIAL", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(item.Stage, "MAIN_ROUND", StringComparison.OrdinalIgnoreCase) ||
                item.MatchDay is null ||
                !HasTeam(item.HomeTeam) ||
                !HasTeam(item.GuestTeam) ||
                !DateTimeOffset.TryParse(item.ScheduledTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            {
                continue;
            }

            if (!dates.TryGetValue(item.MatchDay.Value, out var roundDates))
            {
                roundDates = [];
                dates[item.MatchDay.Value] = roundDates;
            }

            roundDates.Add(DateOnly.FromDateTime(Normalize1996SeasonDate(date).UtcDateTime));
        }

        return dates;
    }

    private static bool TryResolve1996BraunschweigGame(
        BblGame item,
        DateTimeOffset? sourceDate,
        IReadOnlyDictionary<int, HashSet<DateOnly>> roundDates,
        out BblTeam homeTeam,
        out BblTeam guestTeam,
        out DateTimeOffset scheduledTime)
    {
        var missingHome = !HasTeam(item.HomeTeam);
        var missingGuest = !HasTeam(item.GuestTeam);
        if (missingHome == missingGuest || item.MatchDay is null)
        {
            homeTeam = null!;
            guestTeam = null!;
            scheduledTime = default;
            return false;
        }

        var candidateDate = sourceDate.HasValue
            ? Normalize1996SeasonDate(sourceDate.Value)
            : (DateTimeOffset?)null;
        roundDates.TryGetValue(item.MatchDay.Value, out var uniqueRoundDates);
        if (candidateDate is null && (uniqueRoundDates is null || uniqueRoundDates.Count != 1))
        {
            homeTeam = null!;
            guestTeam = null!;
            scheduledTime = default;
            return false;
        }

        if (roundDates.TryGetValue(item.MatchDay.Value, out var dates) &&
            candidateDate is not null &&
            !dates.Contains(DateOnly.FromDateTime(candidateDate.Value.UtcDateTime)) &&
            dates.Count == 1)
        {
            candidateDate = ReplaceDate(candidateDate.Value, dates.Single());
        }

        if (candidateDate is null)
        {
            candidateDate = new DateTimeOffset(
                uniqueRoundDates!.Single().ToDateTime(TimeOnly.MinValue),
                TimeSpan.Zero);
        }

        var inferredTeam = new BblTeam
        {
            TeamId = InferredTeamId,
            SeasonTeamId = InferredTeamId,
            Name = InferredTeamName
        };
        homeTeam = missingHome ? inferredTeam : item.HomeTeam!;
        guestTeam = missingGuest ? inferredTeam : item.GuestTeam!;
        scheduledTime = candidateDate.Value;
        return true;
    }

    private static DateTimeOffset Normalize1996SeasonDate(DateTimeOffset sourceDate)
    {
        var expectedYear = sourceDate.Month >= 7 ? 1996 : 1997;
        return new DateTimeOffset(
            expectedYear,
            sourceDate.Month,
            sourceDate.Day,
            sourceDate.Hour,
            sourceDate.Minute,
            sourceDate.Second,
            TimeSpan.Zero);
    }

    private static DateTimeOffset ReplaceDate(DateTimeOffset source, DateOnly date)
        => new(date.ToDateTime(TimeOnly.FromTimeSpan(source.TimeOfDay)), TimeSpan.Zero);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new StringOrNumberJsonConverter());
        return options;
    }

    private sealed class BblGamesResponse
    {
        public List<BblGame>? Items { get; set; }
        public int TotalPages { get; set; }
    }

    private sealed class BblGame
    {
        public string? Id { get; set; }
        public string? SourceId { get; set; }
        public string? Status { get; set; }
        public string? Stage { get; set; }
        public string? ScheduledTime { get; set; }
        public int? MatchDay { get; set; }
        public BblTeam? HomeTeam { get; set; }
        public BblTeam? GuestTeam { get; set; }
        public BblResult? Result { get; set; }
    }

    private sealed class BblTeam
    {
        public string? TeamId { get; set; }
        public string? SeasonTeamId { get; set; }
        public string? Name { get; set; }
    }

    private sealed class BblResult
    {
        public int? HomeTeamFinalScore { get; set; }
        public int? GuestTeamFinalScore { get; set; }
    }

    private sealed class StringOrNumberJsonConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.GetInt64().ToString(CultureInfo.InvariantCulture),
                JsonTokenType.Null => null,
                _ => throw new JsonException($"Expected a string or number but found {reader.TokenType}.")
            };
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }
}
