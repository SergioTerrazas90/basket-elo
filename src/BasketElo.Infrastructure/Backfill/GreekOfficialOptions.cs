namespace BasketElo.Infrastructure.Backfill;

public sealed class GreekOfficialOptions
{
    public const string SectionName = "GreekOfficial";

    public string EsakeBaseUrl { get; set; } = "https://www.esake.gr/";

    public string EokBaseUrl { get; set; } = "https://www.basket.gr/";

    public string Bitzenis1996Url { get; set; } = "https://bitzenis.gr/retro/bask.htm";

    public string WaybackBaseUrl { get; set; } = "https://web.archive.org/";

    public string GreekWikipedia1992Url { get; set; } =
        "https://el.wikipedia.org/wiki/%CE%A0%CF%81%CF%89%CF%84%CE%AC%CE%B8%CE%BB%CE%B7%CE%BC%CE%B1_%CE%BA%CE%B1%CE%BB%CE%B1%CE%B8%CE%BF%CF%83%CF%86%CE%B1%CE%AF%CF%81%CE%B9%CF%83%CE%B7%CF%82_%CE%911_%CE%B5%CE%B8%CE%BD%CE%B9%CE%BA%CE%AE%CF%82_%CE%BA%CE%B1%CF%84%CE%B7%CE%B3%CE%BF%CF%81%CE%AF%CE%B1%CF%82_%CE%B1%CE%BD%CE%B4%CF%81%CF%8E%CE%BD_1992-1993";

    public string Olympiacos1992ScheduleUrl { get; set; } =
        "https://www.olympiacosbc.gr/el/agones/ellada/programma/1992.html";

    public string UserAgent { get; set; } = "BasketElo historical-ingest/1.0 (issue 126)";

    public int MinRequestIntervalMilliseconds { get; set; } = 100;

    public int MaxTransientRetries { get; set; } = 3;

    public int RetryBaseDelayMilliseconds { get; set; } = 500;
}
