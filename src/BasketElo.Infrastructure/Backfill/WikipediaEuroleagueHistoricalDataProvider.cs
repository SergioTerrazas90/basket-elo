using System.Security.Cryptography;
using System.Text;
using BasketElo.Domain.Backfill;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Imports the ULEB EuroLeague seasons between the 2000-01 split and the
/// beginning of the existing API-Sports historical segment.
/// </summary>
public sealed class WikipediaEuroleagueHistoricalDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "wikipedia-euroleague";
    public const string ParserVersion = "wikipedia-euroleague-historical-wikitext-v1";

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!country.Equals("Europe", StringComparison.OrdinalIgnoreCase) ||
            !leagueName.Equals("Euroleague", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        return Task.FromResult<BasketballProviderLeague?>(
            new BasketballProviderLeague(Source, "euroleague-historical", "Euroleague", "EUR", "literal"));
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var startYear = ParseStartYear(season);
        if (startYear is < 2000 or > 2007)
        {
            warnings.Add($"Wikipedia historical Euroleague coverage is configured for 2000-2001 through 2007-2008; {season} is outside that range.");
            return ([], false, warnings);
        }

        if (!context.CanUseRequest())
        {
            warnings.Add($"Wikipedia request budget reached before the {season} Euroleague page could be fetched.");
            return ([], false, warnings);
        }

        var title = WikipediaFibaEuropeanChampionsCupParser.EnglishPageTitle(startYear);
        context.ConsumeRequest();
        var pagePath = $"/w/index.php?title={Uri.EscapeDataString(title)}&action=raw";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await httpClient.GetAsync(pagePath, timeout.Token);
        response.EnsureSuccessStatusCode();
        var wikitext = await response.Content.ReadAsStringAsync(cancellationToken);
        if (wikitext.Contains("#REDIRECT", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Wikipedia page was not found: {title}.");
            return ([], false, warnings);
        }

        var pageUrl = $"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(title).Replace("%20", "_", StringComparison.Ordinal)}";
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
            "wiki-euroleague");

        if (games.Count == 0)
        {
            warnings.Add($"Wikipedia returned no game-level results for {title}.");
        }

        if (games.Count < 80 && context.CanUseRequest())
        {
            context.ConsumeRequest();
            var renderedPath = $"/wiki/{Uri.EscapeDataString(title).Replace("%20", "_", StringComparison.Ordinal)}";
            using var renderedResponse = await httpClient.GetAsync(renderedPath, cancellationToken);
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
                "wiki-euroleague");
            if (matrixGames.Count > 0)
            {
                var existingKeys = games
                    .Select(BuildMatchKey)
                    .ToHashSet(StringComparer.Ordinal);
                games = games
                    .Concat(matrixGames.Where(game => existingKeys.Add(BuildMatchKey(game))))
                    .ToArray();
                warnings.Add($"Wikipedia raw page was sparse; added {matrixGames.Count} rendered score-matrix game(s).");
            }
        }

        return (games, false, warnings);
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
