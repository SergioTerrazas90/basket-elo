namespace BasketElo.Infrastructure.Backfill;

public class ApiSportsOptions
{
    public const string SectionName = "ApiSports";

    public string BaseUrl { get; set; } = "https://v1.basketball.api-sports.io";
    public string? ApiKey { get; set; }
    public int MinSecondsBetweenRequests { get; set; } = 7;
    public int MaxTransientRetries { get; set; } = 3;
    public int RetryBaseDelayMilliseconds { get; set; } = 500;
}
