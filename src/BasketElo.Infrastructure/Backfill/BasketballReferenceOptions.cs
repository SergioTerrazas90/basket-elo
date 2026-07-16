namespace BasketElo.Infrastructure.Backfill;

public class BasketballReferenceOptions
{
    public const string SectionName = "BasketballReference";

    public string BaseUrl { get; set; } = "https://www.basketball-reference.com/";
    public string ArchiveRoot { get; set; } = "data/basketball-reference";
    public bool NetworkAccessEnabled { get; set; }
    public string? PermissionReference { get; set; }
    public string UserAgent { get; set; } = "BasketElo/1.0 (authorized historical import)";
    public int MinRequestIntervalSeconds { get; set; } = 10;
    public int MaxTransientRetries { get; set; } = 3;
    public int RetryBaseDelayMilliseconds { get; set; } = 500;
}
