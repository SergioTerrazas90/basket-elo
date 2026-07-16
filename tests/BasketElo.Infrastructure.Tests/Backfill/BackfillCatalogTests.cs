using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class BackfillCatalogTests
{
    [Fact]
    public void NbaIsOneCanonicalCompetitionFromInauguralSeasonThroughCurrentCatalogEnd()
    {
        var catalog = new BackfillCatalog();
        var nba = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == BasketballReferenceBasketballDataProvider.Source &&
            league.Country == "United States" &&
            league.LeagueName == "NBA");

        Assert.Equal("United States: NBA", nba.DisplayName);
        Assert.Equal("1946-1947", nba.StartSeason);
        Assert.Null(nba.ProviderLeagues);

        var seasons = catalog.GetSeasonsForLeague(nba).ToList();
        var currentStartYear = DateTime.UtcNow.Month >= 7 ? DateTime.UtcNow.Year : DateTime.UtcNow.Year - 1;
        Assert.Equal("1946-1947", seasons[0]);
        Assert.Equal($"{currentStartYear}-{currentStartYear + 1}", seasons[^1]);
        Assert.Equal(currentStartYear - 1946 + 1, seasons.Count);
        Assert.DoesNotContain(seasons, season => season.StartsWith("BAA", StringComparison.Ordinal));
    }

    [Fact]
    public void ApiSportsNbaCoverageMatchesReviewedProviderRange()
    {
        var catalog = new BackfillCatalog();
        var nba = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == ApiSportsBasketballDataProvider.Source &&
            league.Country == "USA" &&
            league.LeagueName == "NBA");

        Assert.Equal("USA: NBA", nba.DisplayName);
        Assert.Equal("2008-2009", nba.StartSeason);
        Assert.Equal("2025-2026", nba.EndSeason);
        Assert.Equal(18, catalog.GetSeasonsForLeague(nba).Count);
    }
}
