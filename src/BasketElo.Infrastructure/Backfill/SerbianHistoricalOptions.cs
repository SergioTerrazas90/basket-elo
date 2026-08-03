namespace BasketElo.Infrastructure.Backfill;

public sealed class SerbianHistoricalOptions
{
    public const string SectionName = "SerbianHistorical";

    public string BaseUrl { get; set; } = "https://srbijasport.net";

    public string PearlBasketBaseUrl { get; set; } = "https://pearlbasket.altervista.org/";

    public string WikipediaBaseUrl { get; set; } = "https://en.wikipedia.org/";

    public string SerbianWikipediaBaseUrl { get; set; } = "https://sr.wikipedia.org/";

    public string ItalianWikipediaBaseUrl { get; set; } = "https://it.wikipedia.org/";

    public string BorbaBaseUrl { get; set; } = "https://pretraziva.rs/";

    public string PartizanopediaBaseUrl { get; set; } = "https://www.partizanopedia.rs/";

    public string UserAgent { get; set; } = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    public int MinRequestIntervalMilliseconds { get; set; } = 250;
}
