namespace BasketElo.Infrastructure.Backfill;

public sealed class AcbArchiveOptions
{
    public const string SectionName = "AcbArchive";

    public string AvailabilityBaseUrl { get; set; } = "https://archive.org/wayback/available";
    public string ReplayBaseUrl { get; set; } = "https://web.archive.org/web";
    public string ArchiveRoot { get; set; } = "data/acb-archive";
    public bool NetworkAccessEnabled { get; set; }
    public string UserAgent { get; set; } = "BasketElo historical-ingest/1.0";
    public int MaxTransientRetries { get; set; } = 3;
    public int RetryBaseDelayMilliseconds { get; set; } = 500;
    public int MinRequestIntervalMilliseconds { get; set; } = 1000;
}
