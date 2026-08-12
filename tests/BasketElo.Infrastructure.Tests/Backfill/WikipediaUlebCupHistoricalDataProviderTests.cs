using System.Net;
using BasketElo.Domain.Backfill;
using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class WikipediaUlebCupHistoricalDataProviderTests
{
    [Theory]
    [InlineData(2002, "2002Ã¢â‚¬â€œ03 ULEB Cup")]
    [InlineData(2007, "2007Ã¢â‚¬â€œ08 ULEB Cup")]
    public void BuildsEditionPageTitles(int startYear, string expected)
    {
        Assert.StartsWith(startYear.ToString(), expected);
        var expectedTitle = $"{startYear}\u2013{(startYear + 1) % 100:00} ULEB Cup";
        Assert.Equal(expectedTitle, WikipediaUlebCupHistoricalDataProvider.EditionPageTitle(startYear));
    }

    [Theory]
    [InlineData(2003, "ULEB Cup 2003/04")]
    [InlineData(2007, "ULEB Cup 2007/08")]
    public void BuildsGermanEditionPageTitles(int startYear, string expected)
    {
        Assert.Equal(expected, WikipediaUlebCupHistoricalDataProvider.GermanEditionPageTitle(startYear));
    }

    [Fact]
    public async Task ParsesTwoLegFinalAndKeepsCompetitionSeparate()
    {
        var handler = new FixtureHandler();
        var provider = new WikipediaUlebCupHistoricalDataProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://en.wikipedia.org")
        });

        var league = await provider.ResolveLeagueAsync("Europe", "ULEB Cup", new BackfillExecutionContext(0, 0), CancellationToken.None);
        var result = await provider.GetGamesAsync(league!, "2002-2003", new BackfillExecutionContext(2, 0), CancellationToken.None);

        Assert.Equal(2, result.Games.Count);
        Assert.Contains(result.Games, game => game.HomeScore == 78 && game.AwayScore == 90);
        Assert.Contains(result.Games, game => game.HomeScore == 78 && game.AwayScore == 76);
        Assert.All(result.Games, game =>
        {
            Assert.Equal(WikipediaUlebCupHistoricalDataProvider.Source, game.Source);
            Assert.Equal(WikipediaUlebCupHistoricalDataProvider.ParserVersion, game.Provenance?.ParserVersion);
        });
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task UsesGermanEditionWhenEnglishPageIsSparse()
    {
        var handler = new FixtureHandler();
        var provider = new WikipediaUlebCupHistoricalDataProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://en.wikipedia.org")
        });

        var league = await provider.ResolveLeagueAsync("Europe", "ULEB Cup", new BackfillExecutionContext(0, 0), CancellationToken.None);
        var result = await provider.GetGamesAsync(league!, "2003-2004", new BackfillExecutionContext(4, 0), CancellationToken.None);

        Assert.Equal(4, result.Games.Count);
        Assert.Contains(result.Warnings, warning => warning.Contains("richer German edition page", StringComparison.Ordinal));
        Assert.Equal(4, handler.RequestCount);
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var isRenderedPage = request.RequestUri?.AbsolutePath.StartsWith("/wiki/", StringComparison.Ordinal) == true;
            var isGerman = request.RequestUri?.Host.Equals("de.wikipedia.org", StringComparison.OrdinalIgnoreCase) == true;
            var content = isRenderedPage
                ? "<html><body><table></table></body></html>"
                : isGerman
                    ? """
                      == Finals ==
                      {{TwoLegResult|[[Krka]]|SLO|156-166|[[Pamesa Valencia]]|ESP|78-90|76-78}}
                      {{TwoLegResult|[[Gran Canaria]]|ESP|160-150|[[Hapoel Jerusalem]]|ISR|80-70|80-80}}
                      """
                    : """
                  | duration = 15 October 2002 Ã¢â‚¬â€œ 24 April 2003
                  == Finals ==
                  {{TwoLegResult|[[Krka]]|SLO|156-166|[[Pamesa Valencia]]|ESP|78-90|76-78}}
                  """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }
}
