namespace BasketElo.Infrastructure.Backfill;

public sealed class FrenchHistoricalOptions
{
    public const string SectionName = "FrenchHistorical";

    public string BasketArchivesBaseUrl { get; set; } = "http://www.basketarchives.fr/";

    public string TheSportsBaseUrl { get; set; } = "https://www.the-sports.org/";

    public string WikipediaBaseUrl { get; set; } = "https://fr.wikipedia.org/";

    public string UserAgent { get; set; } = "BasketElo historical-ingest/1.0 (issue 125)";

    public int MinRequestIntervalMilliseconds { get; set; } = 100;

    public int MaxTransientRetries { get; set; } = 3;

    public int RetryBaseDelayMilliseconds { get; set; } = 500;
}
