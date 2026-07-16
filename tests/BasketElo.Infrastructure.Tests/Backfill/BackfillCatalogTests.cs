using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class BackfillCatalogTests
{
    [Fact]
    public void NbaIsOneCanonicalCompetitionFromInauguralSeasonThroughCurrentCatalogEnd()
    {
        var catalog = new BackfillCatalog();
        var nbaSegments = catalog.GetLeagues()
            .Where(league => league.DisplayName == "United States: NBA")
            .ToList();

        Assert.Equal(2, nbaSegments.Count);
        Assert.DoesNotContain(nbaSegments, league =>
            league.Provider == BasketballReferenceBasketballDataProvider.Source);

        var seasons = nbaSegments
            .SelectMany(catalog.GetSeasonsForLeague)
            .OrderBy(SeasonLabelNormalizer.ParseStartYear)
            .ToList();
        Assert.Equal("1946-1947", seasons[0]);
        Assert.Equal("2025-2026", seasons[^1]);
        Assert.Equal(80, seasons.Count);
        Assert.Equal(80, seasons.Distinct(StringComparer.OrdinalIgnoreCase).Count());
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

        Assert.Equal("United States: NBA", nba.DisplayName);
        Assert.Equal("2008-2009", nba.StartSeason);
        Assert.Equal("2025-2026", nba.EndSeason);
        Assert.Equal(18, catalog.GetSeasonsForLeague(nba).Count);
    }

    [Fact]
    public void FiveThirtyEightNbaCoverageFillsPreApiSportsRange()
    {
        var catalog = new BackfillCatalog();
        var nba = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FiveThirtyEightBasketballDataProvider.Source &&
            league.Country == "United States" &&
            league.LeagueName == "NBA");

        var seasons = catalog.GetSeasonsForLeague(nba).ToList();
        Assert.Equal("United States: NBA", nba.DisplayName);
        Assert.Equal("1946-1947", seasons[0]);
        Assert.Equal("2007-2008", seasons[^1]);
        Assert.Equal(62, seasons.Count);
    }
}
