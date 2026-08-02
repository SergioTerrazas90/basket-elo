namespace BasketElo.Infrastructure.Backfill;

public sealed class TurkishBasketballOptions
{
    public const string SectionName = "TurkishBasketball";

    public string BaseUrl { get; set; } = "https://bsl.tblstat.net/";

    public string UserAgent { get; set; } = "BasketElo historical-ingest/1.0 (issue 128)";

    public int MinRequestIntervalMilliseconds { get; set; } = 100;
}
