namespace BasketElo.Infrastructure.Backfill;

public sealed class GreekOfficialOptions
{
    public const string SectionName = "GreekOfficial";

    public string EsakeBaseUrl { get; set; } = "https://www.esake.gr/";

    public string EokBaseUrl { get; set; } = "https://www.basket.gr/";

    public string Bitzenis1996Url { get; set; } = "https://bitzenis.gr/retro/bask.htm";

    public string WaybackBaseUrl { get; set; } = "https://web.archive.org/";

    public string UserAgent { get; set; } = "BasketElo historical-ingest/1.0 (issue 126)";

    public int MinRequestIntervalMilliseconds { get; set; } = 100;

    public int MaxTransientRetries { get; set; } = 3;

    public int RetryBaseDelayMilliseconds { get; set; } = 500;
}
