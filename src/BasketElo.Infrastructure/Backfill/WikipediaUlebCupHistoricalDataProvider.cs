using System.Security.Cryptography;
using System.Text;
using BasketElo.Domain.Backfill;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Imports the six ULEB Cup editions before the competition adopted the modern
/// EuroCup naming. Wikipedia's edition tables are used for game-level scores;
/// official ULEB/Euroleague media records remain the validation reference.
/// </summary>
public sealed class WikipediaUlebCupHistoricalDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "wikipedia-uleb-cup";
    public const string ParserVersion = "wikipedia-uleb-cup-wikitext-v1";
    public const string ValidationReferenceUrl = "https://mediacentre.euroleague.net/uploads/EuroleagueCore/pastmatchups/round4017.pdf";

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!country.Equals("Europe", StringComparison.OrdinalIgnoreCase) ||
            !leagueName.Equals("ULEB Cup", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        return Task.FromResult<BasketballProviderLeague?>(
            new BasketballProviderLeague(Source, "uleb-cup", "ULEB Cup", "EUR", "literal"));
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var startYear = ParseStartYear(season);
        if (startYear is < 2002 or > 2007)
        {
            warnings.Add($"ULEB Cup coverage is configured for 2002-2003 through 2007-2008; {season} is outside that range.");
            return ([], false, warnings);
        }

        var title = EditionPageTitle(startYear);
        var games = await LoadEditionAsync(
            "en.wikipedia.org",
            title,
            season,
            context,
            warnings,
            cancellationToken);

        // Several later English editions publish standings without the underlying
        // score matrices. The German edition pages retain those matrices, so use
        // them as a transparent fallback when the English page is sparse.
        if (games.Count < 150)
        {
            var germanTitle = GermanEditionPageTitle(startYear);
            var germanGames = await LoadEditionAsync(
                "de.wikipedia.org",
                germanTitle,
                season,
                context,
                warnings,
                cancellationToken);
            if (germanGames.Count >= games.Count)
            {
                warnings.Add($"English Wikipedia was sparse ({games.Count} game(s)); using the richer German edition page ({germanGames.Count} game(s)).");
                games = germanGames;
            }
            else if (germanGames.Count > 0)
            {
                var existingKeys = games.Select(BuildMatchKey).ToHashSet(StringComparer.Ordinal);
                var added = germanGames.Where(game => existingKeys.Add(BuildMatchKey(game))).ToArray();
                if (added.Length > 0)
                {
                    games.AddRange(added);
                    warnings.Add($"German Wikipedia added {added.Length} score-matrix game(s) to the English edition data.");
                }
            }
        }

        warnings.Add($"Wikipedia parsed {games.Count} distinct ULEB Cup game(s) for {season}; validation reference: {ValidationReferenceUrl}.");
        return (games.OrderBy(game => game.GameDateTimeUtc).ThenBy(game => game.SourceGameId, StringComparer.Ordinal).ToArray(), false, warnings);
    }

    internal static string EditionPageTitle(int startYear)
        => startYear is >= 2002 and <= 2007
            ? $"{startYear}\u2013{(startYear + 1) % 100:00} ULEB Cup"
            : throw new ArgumentOutOfRangeException(nameof(startYear), startYear, "ULEB Cup coverage is configured for 2002-2007.");

    internal static string GermanEditionPageTitle(int startYear)
        => startYear is >= 2002 and <= 2007
            ? $"ULEB Cup {startYear}/{(startYear + 1) % 100:00}"
            : throw new ArgumentOutOfRangeException(nameof(startYear), startYear, "ULEB Cup coverage is configured for 2002-2007.");

    private async Task<List<BasketballProviderGame>> LoadEditionAsync(
        string host,
        string title,
        string season,
        BackfillExecutionContext context,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!context.CanUseRequest())
        {
            warnings.Add($"Wikipedia request budget reached before the {host} page {title} could be fetched.");
            return [];
        }

        context.ConsumeRequest();
        var pageUrl = $"https://{host}/wiki/{Uri.EscapeDataString(title).Replace("%20", "_", StringComparison.Ordinal)}";
        var rawUri = new Uri($"https://{host}/w/index.php?title={Uri.EscapeDataString(title)}&action=raw");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await httpClient.GetAsync(rawUri, timeout.Token);
        response.EnsureSuccessStatusCode();
        var wikitext = await response.Content.ReadAsStringAsync(cancellationToken);
        if (wikitext.Contains("#REDIRECT", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Wikipedia page was not found: {host}/{title}.");
            return [];
        }

        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(wikitext)))[..16];
        var games = WikipediaFibaEuropeanChampionsCupParser.ParseGames(
            wikitext,
            season,
            pageUrl,
            DateTime.UtcNow,
            revision,
            warnings,
            Source,
            ParserVersion,
            "wiki-uleb",
            preserveRoundRobinMatrixHomeAway: true,
            parseUlebFinalFormats: true).ToList();

        if (games.Count < 100 && context.CanUseRequest())
        {
            context.ConsumeRequest();
            var renderedUri = new Uri(pageUrl);
            using var renderedResponse = await httpClient.GetAsync(renderedUri, cancellationToken);
            renderedResponse.EnsureSuccessStatusCode();
            var renderedHtml = await renderedResponse.Content.ReadAsStringAsync(cancellationToken);
            var matrixGames = WikipediaFibaEuropeanChampionsCupParser.ParseHtmlMatrixGames(
                renderedHtml,
                season,
                pageUrl,
                DateTime.UtcNow,
                revision,
                warnings,
                Source,
                ParserVersion,
                "wiki-uleb",
                preserveRoundRobinMatrixHomeAway: true);
            var existingKeys = games.Select(BuildMatchKey).ToHashSet(StringComparer.Ordinal);
            var added = matrixGames.Where(game => existingKeys.Add(BuildMatchKey(game))).ToArray();
            if (added.Length > 0)
            {
                games.AddRange(added);
                warnings.Add($"{host} raw page was sparse; added {added.Length} rendered score-matrix game(s).");
            }
        }

        warnings.Add($"{host} parsed {games.Count} ULEB Cup game(s) for {season}.");
        return games;
    }

    private static string BuildMatchKey(BasketballProviderGame game)
        => $"{game.SourceHomeTeamId}|{game.SourceAwayTeamId}|{game.HomeScore}|{game.AwayScore}|{game.CompetitionPhase}|{game.CompetitionRound}";

    private static int ParseStartYear(string season)
    {
        var separator = season.IndexOf('-', StringComparison.Ordinal);
        var value = separator > 0 ? season[..separator] : season;
        return int.TryParse(value, out var year)
            ? year
            : throw new ArgumentException($"Season '{season}' has no four-digit start year.", nameof(season));
    }
}
