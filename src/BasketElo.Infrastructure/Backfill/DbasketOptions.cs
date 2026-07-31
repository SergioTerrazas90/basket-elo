namespace BasketElo.Infrastructure.Backfill;

public sealed class DbasketOptions
{
    public const string SectionName = "Dbasket";

    public string BaseUrl { get; set; } = "https://dbasket.net";
    public bool NetworkAccessEnabled { get; set; }
    public string UserAgent { get; set; } = "BasketElo historical-ingest/1.0";
    public int MaxTransientRetries { get; set; } = 3;
    public int RetryBaseDelayMilliseconds { get; set; } = 500;
    public int MinRequestIntervalMilliseconds { get; set; } = 250;
}
