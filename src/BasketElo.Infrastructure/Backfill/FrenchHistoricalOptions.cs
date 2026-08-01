namespace BasketElo.Infrastructure.Backfill;

public sealed class FrenchHistoricalOptions
{
    public const string SectionName = "FrenchHistorical";

    public string BasketArchivesBaseUrl { get; set; } = "http://www.basketarchives.fr/";

    public string TheSportsBaseUrl { get; set; } = "https://www.the-sports.org/";

    public string LEquipeBaseUrl { get; set; } = "https://www.lequipe.fr/";

    public string WikipediaBaseUrl { get; set; } = "https://fr.wikipedia.org/";

    public string GallicaBaseUrl { get; set; } = "https://gallica.bnf.fr/";

    public string? GallicaCacheDirectory { get; set; }

    public bool GallicaCacheOnly { get; set; }

    public string UserAgent { get; set; } = "BasketElo historical-ingest/1.0 (issue 125)";

    public int MinRequestIntervalMilliseconds { get; set; } = 100;

    public int GallicaMinRequestIntervalMilliseconds { get; set; } = 1600;

    public int GallicaMaxTransientRetries { get; set; } = 5;

    public int GallicaRetryBaseDelayMilliseconds { get; set; } = 2000;

    public bool GallicaAllowIncompleteResults { get; set; }

    public int MaxTransientRetries { get; set; } = 3;

    public int RetryBaseDelayMilliseconds { get; set; } = 500;
}
