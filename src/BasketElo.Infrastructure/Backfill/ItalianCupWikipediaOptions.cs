namespace BasketElo.Infrastructure.Backfill;

public sealed class ItalianCupWikipediaOptions
{
    public const string SectionName = "ItalianCupWikipedia";

    public string BaseUrl { get; set; } = "https://it.wikipedia.org";
    public string UserAgent { get; set; } =
        "BasketElo historical-ingest/1.0 (https://github.com/SergioTerrazas90/basket-elo)";
    public int MinRequestIntervalMilliseconds { get; set; } = 1000;
}
