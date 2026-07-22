using BasketElo.Domain.Elo;
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

    [Fact]
    public void GlobalSportsArchiveAfroBasketUsesOfficialSingleEditionYears()
    {
        var catalog = new BackfillCatalog();
        var afroBasket = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == GlobalSportsArchiveBasketballDataProvider.Source &&
            league.Country == "Africa" &&
            league.LeagueName == "FIBA AfroBasket");

        Assert.True(afroBasket.UsesSingleYearSeasonLabel);
        Assert.Contains("1975", catalog.GetSeasonsForLeague(afroBasket));
        Assert.Contains("1981", catalog.GetSeasonsForLeague(afroBasket));
        Assert.Contains("1983", catalog.GetSeasonsForLeague(afroBasket));
        Assert.Contains("1993", catalog.GetSeasonsForLeague(afroBasket));
        Assert.DoesNotContain("1976", catalog.GetSeasonsForLeague(afroBasket));
        Assert.DoesNotContain("1982", catalog.GetSeasonsForLeague(afroBasket));
        Assert.DoesNotContain("1984", catalog.GetSeasonsForLeague(afroBasket));
        Assert.Contains("1992", catalog.GetSeasonsForLeague(afroBasket));
        Assert.DoesNotContain("2023", catalog.GetSeasonsForLeague(afroBasket));
        Assert.Contains("1999", catalog.GetSeasonsForLeague(afroBasket));
        Assert.DoesNotContain("1999-2000", catalog.GetSeasonsForLeague(afroBasket));
        Assert.Equal("1999", SeasonLabelNormalizer.ToCanonicalSeasonLabel("1999-2000", afroBasket.UsesSingleYearSeasonLabel));
    }

    [Fact]
    public void GlobalSportsArchiveAfroBasketIsThePrimaryInternationalSource()
    {
        var catalog = new BackfillCatalog();
        var source = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == GlobalSportsArchiveBasketballDataProvider.Source &&
            league.Country == "Africa" &&
            league.LeagueName == "FIBA AfroBasket");

        Assert.Contains("2003", catalog.GetSeasonsForLeague(source));
        Assert.Contains("2025", catalog.GetSeasonsForLeague(source));
        Assert.DoesNotContain(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Africa" &&
            league.LeagueName == "FIBA AfroBasket");
    }

    [Fact]
    public void GlobalSportsArchiveAfroBasketPreQualifiersAreASeparateTournament()
    {
        var catalog = new BackfillCatalog();
        var source = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == GlobalSportsArchiveBasketballDataProvider.Source &&
            league.Country == "Africa" &&
            league.LeagueName == "FIBA AfroBasket Pre-Qualifiers");

        Assert.True(source.UsesSingleYearSeasonLabel);
        Assert.Equal(["2021", "2025"], catalog.GetSeasonsForLeague(source));
        Assert.Equal(EloPoolKeys.NationalTeams, source.EloPoolKey);
    }
}
