using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

public sealed class AcbArchiveBasketballDataProvider(
    HttpClient httpClient,
    IOptions<AcbArchiveOptions> options) : IBasketballDataProvider
{
    public const string Source = "acb-wayback";
    public const string ParserVersion = "acb-wayback-v1";
    private const string OriginalHost = "www.acb.com";

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var league = string.Equals(country, "Spain", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(leagueName, "ACB", StringComparison.OrdinalIgnoreCase)
                ? new BasketballProviderLeague(Source, "ACB", "ACB", "ES", "start_year")
                : null;
        return Task.FromResult(league);
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(league.Source, Source, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(league.SourceLeagueId, "ACB", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ACB archive provider only supports Spain: ACB.");
        }

        var seasonSource = GetSeasonSource(season);
        var games = new List<BasketballProviderGame>();
        var warnings = new List<string>();

        for (var number = seasonSource.FirstGameNumber; number <= seasonSource.LastGameNumber; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.CanUseRequest())
            {
                warnings.Add($"Request budget reached after {games.Count} parsed games; remaining source IDs were not attempted.");
                break;
            }
            var sourceGameKey = $"LACB{number}";
            var originalUrl = $"https://{OriginalHost}/fichas/{sourceGameKey}.php";
            var capture = await FindCaptureAsync(originalUrl, context, cancellationToken);
            if (capture is null)
            {
                warnings.Add($"{sourceGameKey}: no archived ACB page was found.");
                continue;
            }

            var replayUrl = BuildReplayUrl(capture.Timestamp, originalUrl);
            var html = await FetchPageAsync(replayUrl, context, cancellationToken);
            if (html is null)
            {
                warnings.Add($"{sourceGameKey}: archived page could not be fetched.");
                continue;
            }

            if (!TryParseGame(html, sourceGameKey, seasonSource.Season, replayUrl, capture.Timestamp, out var game, out var warning))
            {
                warnings.Add($"{sourceGameKey}: {warning}");
                continue;
            }

            games.Add(game!);
        }

        return (games, false, warnings);
    }

    internal static string BuildReplayUrl(string timestamp, string originalUrl) =>
        $"https://web.archive.org/web/{timestamp}id_/{originalUrl}";

    public static bool TryParseGame(
        string html,
        string sourceGameKey,
        string season,
        string sourceUrl,
        string sourceRevision,
        out BasketballProviderGame? game,
        out string warning)
    {
        game = null;
        warning = string.Empty;
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var titleTable = document.DocumentNode.SelectSingleNode("//div[contains(@class,'titulopartido')]//table");
        var titleCells = titleTable?.SelectNodes(".//tr[1]/td")?.ToList();
        var scoreCells = titleTable?.SelectNodes(".//tr[2]/td")?.ToList();
        var metadata = document.DocumentNode.SelectSingleNode("//table[contains(@class,'estadisticas')]//tr[1]/td[1]");
        if (titleCells is null || titleCells.Count < 2 || scoreCells is null || scoreCells.Count < 2 || metadata is null)
        {
            warning = "expected ACB title/score/date structure was not found";
            return false;
        }

        var homeName = CleanText(titleCells[0].InnerText).TrimEnd('|').Trim();
        var awayName = CleanText(titleCells[1].InnerText);
        var homeScore = ParseScore(scoreCells[0].InnerText);
        var awayScore = ParseScore(scoreCells[1].InnerText);
        var metadataText = CleanText(metadata.InnerText);
        var dateText = ExtractDate(metadataText);
        if (string.IsNullOrWhiteSpace(homeName) || string.IsNullOrWhiteSpace(awayName) ||
            !DateTime.TryParseExact(dateText, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            warning = $"invalid teams or date in '{metadataText}'";
            return false;
        }

        if (!homeScore.HasValue || !awayScore.HasValue)
        {
            warning = "final score is missing";
            return false;
        }

        var fetchedAt = DateTime.UtcNow;
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html))).ToLowerInvariant();
        game = new BasketballProviderGame(
            Source,
            sourceGameKey,
            DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Utc),
            "finished",
            TeamId(homeName),
            homeName,
            TeamId(awayName),
            awayName,
            homeScore,
            awayScore,
            new BasketballProviderGameProvenance(sourceUrl, season, fetchedAt, ParserVersion, revision));
        return true;
    }

    private async Task<Capture?> FindCaptureAsync(string originalUrl, BackfillExecutionContext context, CancellationToken cancellationToken)
    {
        if (!options.Value.NetworkAccessEnabled)
        {
            throw new InvalidOperationException("ACB archive network access is disabled; enable AcbArchive:NetworkAccessEnabled for the historical import.");
        }

        var uri = $"{options.Value.AvailabilityBaseUrl}?url={Uri.EscapeDataString(originalUrl)}&timestamp=20180101";
        using var response = await SendAsync(uri, context, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("archived_snapshots", out var snapshots) ||
            !snapshots.TryGetProperty("closest", out var closest) ||
            !closest.TryGetProperty("available", out var available) || !available.GetBoolean() ||
            !closest.TryGetProperty("timestamp", out var timestamp))
        {
            return null;
        }

        return new Capture(timestamp.GetString() ?? string.Empty);
    }

    private async Task<string?> FetchPageAsync(string url, BackfillExecutionContext context, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(url, context, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(string url, BackfillExecutionContext context, CancellationToken cancellationToken)
    {
        return await BackfillHttpRetryPolicy.SendAsync(
            async retryCancellationToken =>
            {
                if (!context.CanUseRequest())
                {
                    throw new InvalidOperationException("ACB archive request budget reached.");
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
    }

    private static SeasonSource GetSeasonSource(string season)
    {
        var canonical = SeasonLabelNormalizer.ToFullSeasonLabel(season);
        var pieces = canonical.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length != 2 || !int.TryParse(pieces[0], out var startYear) ||
            !int.TryParse(pieces[1], out var endYear) || endYear != startYear + 1 || startYear is < 1985 or > 2007)
        {
            throw new ArgumentException("ACB archive coverage currently supports seasons 1985-1986 through 2007-2008.", nameof(season));
        }

        var first = startYear switch
        {
            1985 => 30001,
            1986 => 31001,
            1987 => 32001,
            1988 => 33001,
            1989 => 34001,
            1990 => 35001,
            1991 => 36001,
            1992 => 37001,
            1993 => 38001,
            1994 => 39001,
            1995 => 40001,
            1996 => 41001,
            1997 => 42001,
            1998 => 43001,
            1999 => 44001,
            2000 => 45001,
            2001 => 46001,
            2002 => 47001,
            2003 => 48001,
            2004 => 49001,
            2005 => 50001,
            2006 => 51001,
            2007 => 52001,
            _ => throw new ArgumentOutOfRangeException(nameof(season))
        };
        var last = startYear switch
        {
            1985 => 30257,
            1986 => 31262,
            1987 => 32264,
            1988 => 33492,
            1989 => 34487,
            1990 => 35494,
            1991 => 36498,
            1992 => 37401,
            1993 => 38347,
            1994 => 39417,
            1995 => 40415,
            1996 => 41351,
            1997 => 42350,
            1998 => 43339,
            1999 => 44341,
            2000 => 45339,
            2001 => 46339,
            2002 => 47339,
            2003 => 48341,
            2004 => 49341,
            2005 => 50339,
            2006 => 51340,
            2007 => 52327,
            _ => throw new ArgumentOutOfRangeException(nameof(season))
        };
        return new SeasonSource(canonical, first, last);
    }

    private static string ExtractDate(string metadata) =>
        metadata.Split('|', StringSplitOptions.TrimEntries).FirstOrDefault(x => x.Count(c => c == '/') == 2) ?? string.Empty;

    private static short? ParseScore(string text) =>
        short.TryParse(CleanText(text).Trim('|').Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var score) ? score : null;

    private static string CleanText(string value) =>
        HtmlEntity.DeEntitize(value).Replace('\u00A0', ' ').Trim();

    private static string TeamId(string name) => new(name.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private sealed record Capture(string Timestamp);
    private sealed record SeasonSource(string Season, int FirstGameNumber, int LastGameNumber);
}
