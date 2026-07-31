namespace BasketElo.Infrastructure.Backfill;

public sealed class LbaOfficialOptions
{
    public const string SectionName = "LbaOfficial";

    public string BaseUrl { get; set; } = "https://www.legabasket.it";
    public string UserAgent { get; set; } = "BasketElo historical-ingest/1.0";
    public int MaxTransientRetries { get; set; } = 3;
    public int RetryBaseDelayMilliseconds { get; set; } = 500;
    public int MinRequestIntervalMilliseconds { get; set; } = 100;
}
