using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

public sealed class DbasketAcbBasketballDataProvider(
    HttpClient httpClient,
    IOptions<DbasketOptions> options) : IBasketballDataProvider
{
    public const string Source = "acb-dbasket";
    public const string ParserVersion = "acb-dbasket-v1";

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
            throw new InvalidOperationException("DBasket provider only supports Spain: ACB.");
        }

        if (!options.Value.NetworkAccessEnabled)
        {
            throw new InvalidOperationException("DBasket network access is disabled; enable Dbasket:NetworkAccessEnabled for the historical import.");
        }

        var seasonSlug = SeasonSlug(season);
        var seasonUrl = $"{options.Value.BaseUrl.TrimEnd('/')}/seasons/acb/{seasonSlug}";
        var warnings = new List<string>();
        var games = new List<BasketballProviderGame>();
        var seasonHtml = await FetchAsync(seasonUrl, context, cancellationToken);
        if (seasonHtml is null)
        {
            warnings.Add($"{seasonSlug}: season page could not be fetched.");
            return (games, false, warnings);
        }

        var document = new HtmlDocument();
        document.LoadHtml(seasonHtml);
        var rounds = ParseRounds(document, seasonSlug);
        foreach (var round in rounds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.CanUseRequest())
            {
                warnings.Add($"Request budget reached after {games.Count} parsed games; remaining DBasket rounds were not attempted.");
                break;
            }

            var roundUrl = $"{options.Value.BaseUrl.TrimEnd('/')}/seasons/acb/{seasonSlug}/{round.Number}";
            var roundHtml = await FetchAsync(roundUrl, context, cancellationToken);
            if (roundHtml is null)
            {
                warnings.Add($"Round {round.Number}: page could not be fetched.");
                continue;
            }

            foreach (var piece in ParseGamePieces(roundHtml))
            {
                if (TryParseGame(piece, season, round.Type, roundUrl, out var game, out var warning))
                {
                    games.Add(game!);
                }
                else
                {
                    warnings.Add($"Round {round.Number}: {warning}");
                }
            }
        }

        return (games, false, warnings);
    }

    public static bool TryParseGame(
        string html,
        string season,
        string phase,
        string sourceUrl,
        out BasketballProviderGame? game,
        out string warning)
    {
        game = null;
        warning = string.Empty;
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var piece = document.DocumentNode.SelectSingleNode("//div[contains(@class,'jornada-piece')]");
        return piece is not null
            ? TryParseGame(piece, season, phase, sourceUrl, out game, out warning)
            : Fail("expected DBasket game block was not found", out game, out warning);
    }

    private static bool TryParseGame(
        HtmlNode piece,
        string season,
        string phase,
        string sourceUrl,
        out BasketballProviderGame? game,
        out string warning)
    {
        game = null;
        warning = string.Empty;
        var dateText = CleanText(piece.SelectSingleNode(".//li[contains(@class,'round-header-item-highlight-two')]")?.InnerText ?? string.Empty);
        var result = piece.SelectSingleNode(".//table[contains(@class,'resultado')]");
        var firstRow = result?.SelectSingleNode(".//tr[1]");
        var teams = firstRow?.SelectNodes(".//img[@alt]")?.Select(x => CleanText(x.GetAttributeValue("alt", string.Empty))).ToList();
        var scores = firstRow?.SelectNodes(".//td[contains(@class,'celda-resultado')]")?.Select(x => ParseScore(x.InnerText)).ToList();
        var formAction = piece.SelectSingleNode(".//form[@action]")?.GetAttributeValue("action", string.Empty);
        if (teams is null || teams.Count < 2 || scores is null || scores.Count < 2 ||
            string.IsNullOrWhiteSpace(formAction) || !DateTime.TryParseExact(dateText, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            warning = "expected DBasket teams, scores, source identifier, or date was not found";
            return false;
        }

        if (!scores[0].HasValue || !scores[1].HasValue)
        {
            warning = "final score is missing";
            return false;
        }

        var sourceGameId = formAction.Trim('/');
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(piece.OuterHtml))).ToLowerInvariant();
        game = new BasketballProviderGame(
            Source,
            sourceGameId,
            DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Utc),
            "finished",
            TeamId(teams[0]),
            teams[0],
            TeamId(teams[1]),
            teams[1],
            scores[0],
            scores[1],
            new BasketballProviderGameProvenance(sourceUrl, season, DateTime.UtcNow, ParserVersion, revision));
        return true;
    }

    private async Task<string?> FetchAsync(string url, BackfillExecutionContext context, CancellationToken cancellationToken)
    {
        using var response = await BackfillHttpRetryPolicy.SendAsync(
            async retryCancellationToken =>
            {
                if (!context.CanUseRequest())
                {
                    throw new InvalidOperationException("DBasket request budget reached.");
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

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static IReadOnlyCollection<RoundSource> ParseRounds(HtmlDocument document, string seasonSlug)
    {
        return document.DocumentNode
            .SelectNodes($"//a[contains(@href, '/seasons/acb/{seasonSlug}/')]/ancestor::tr[1]")?
            .Select(row =>
            {
                var href = row.SelectSingleNode(".//a[@href]")?.GetAttributeValue("href", string.Empty) ?? string.Empty;
                var numberText = href.TrimEnd('/').Split('/').LastOrDefault();
                return int.TryParse(numberText, out var number)
                    ? new RoundSource(number, CleanText(row.SelectSingleNode("./td[2]")?.InnerText ?? string.Empty))
                    : null;
            })
            .Where(x => x is not null)
            .Cast<RoundSource>()
            .DistinctBy(x => x.Number)
            .OrderBy(x => x.Number)
            .ToList() ?? [];
    }

    private static IEnumerable<HtmlNode> ParseGamePieces(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return (IEnumerable<HtmlNode>?)document.DocumentNode.SelectNodes("//div[contains(@class,'jornada-piece')]") ?? Array.Empty<HtmlNode>();
    }

    private static string SeasonSlug(string season)
    {
        var canonical = SeasonLabelNormalizer.ToFullSeasonLabel(season);
        var pieces = canonical.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length != 2 || !int.TryParse(pieces[0], out var startYear) ||
            !int.TryParse(pieces[1], out var endYear) || endYear != startYear + 1 || startYear is < 1983 or > 2007)
        {
            throw new ArgumentException("DBasket ACB coverage supports seasons 1983-1984 through 2007-2008.", nameof(season));
        }

        return $"{startYear}-{endYear.ToString()[^2..]}";
    }

    private static short? ParseScore(string text) =>
        short.TryParse(CleanText(text), NumberStyles.Integer, CultureInfo.InvariantCulture, out var score) ? score : null;

    private static string CleanText(string value) =>
        HtmlEntity.DeEntitize(value).Replace('\u00A0', ' ').Trim();

    private static string TeamId(string name) => new(name.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static bool Fail(string message, out BasketballProviderGame? game, out string warning)
    {
        game = null;
        warning = message;
        return false;
    }

    private sealed record RoundSource(int Number, string Type);
}
