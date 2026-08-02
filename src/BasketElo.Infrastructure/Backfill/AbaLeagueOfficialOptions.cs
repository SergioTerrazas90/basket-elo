namespace BasketElo.Infrastructure.Backfill;

public sealed class AbaLeagueOfficialOptions
{
    public const string SectionName = "AbaLeagueOfficial";

    public string BaseUrl { get; set; } = "https://www.aba-liga.com";

    public string UserAgent { get; set; } = "BasketElo historical-ingest/1.0";

    public int MinRequestIntervalMilliseconds { get; set; } = 250;
}
