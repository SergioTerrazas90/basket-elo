namespace BasketElo.Infrastructure.Backfill;

public sealed class GermanBasketballOptions
{
    public const string SectionName = "GermanBasketball";

    public string OfficialBaseUrl { get; set; } = "https://www.easycredit-bbl.de/";

    public string ApiBaseUrl { get; set; } = "https://api.basketball-bundesliga.de/";

    // The page exposes the current public-web API secret in __NEXT_DATA__.
    // It is fetched at runtime and deliberately not stored in configuration.
    public string AuthPagePath { get; set; } = "teams/413/2006";

    public string UserAgent { get; set; } = "BasketElo historical-ingest/1.0 (issue 129)";

    public int PageSize { get; set; } = 1000;

    public int MinRequestIntervalMilliseconds { get; set; } = 100;

    public int MaxTransientRetries { get; set; } = 3;

    public int RetryBaseDelayMilliseconds { get; set; } = 500;
}
