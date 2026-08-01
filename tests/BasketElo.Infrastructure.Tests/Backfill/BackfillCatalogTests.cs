using BasketElo.Domain.Elo;
using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class BackfillCatalogTests
{
    [Fact]
    public void FrenchHistoricalCatalogStartsWithFirstContinuousCompleteSeasonAndPreservesCupOverlap()
    {
        var catalog = new BackfillCatalog();
        var league = catalog.GetLeagues().Single(item =>
            item.Provider == FrenchHistoricalBasketballDataProvider.Source && item.Country == "France" && item.LeagueName == "LNB");
        var cup = catalog.GetLeagues().Single(item =>
            item.Provider == FrenchHistoricalBasketballDataProvider.Source && item.Country == "France" && item.LeagueName == "French Cup");

        var leagueSeasons = catalog.GetSeasonsForLeague(league);
        var cupSeasons = catalog.GetSeasonsForLeague(cup);

        Assert.DoesNotContain("1981-1982", leagueSeasons);
        Assert.DoesNotContain("1986-1987", leagueSeasons);
        Assert.Contains("1987-1988", leagueSeasons);
        Assert.Contains("1998-1999", leagueSeasons);
        Assert.Contains("2007-2008", leagueSeasons);
        Assert.Equal(21, leagueSeasons.Count);
        Assert.Equal(4, cupSeasons.Count);
        Assert.All(cupSeasons, season => Assert.Contains(season, leagueSeasons));
    }

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
    public void DbasketAcbFillsTheHistoricalGapBeforeApiSports()
    {
        var catalog = new BackfillCatalog();
        var archive = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == DbasketAcbBasketballDataProvider.Source &&
            league.Country == "Spain" && league.LeagueName == "ACB");

        var seasons = catalog.GetSeasonsForLeague(archive).ToList();
        Assert.Equal("1983-1984", seasons[0]);
        Assert.Equal("2007-2008", seasons[^1]);
        Assert.Equal(25, seasons.Count);
    }

    [Fact]
    public void OfficialLbaFillsThirtyFourSeasonsBeforeApiSports()
    {
        var catalog = new BackfillCatalog();
        var archive = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == LbaOfficialSerieABasketballDataProvider.Source &&
            league.Country == "Italy" && league.LeagueName == "Serie A");

        var seasons = catalog.GetSeasonsForLeague(archive).ToList();
        Assert.Equal("Italy: Lega Basket Serie A", archive.DisplayName);
        Assert.Equal("1974-1975", seasons[0]);
        Assert.Equal("2007-2008", seasons[^1]);
        Assert.Equal(34, seasons.Count);
    }

    [Fact]
    public void ItalianCupCatalogStartsAtLeagueCoverageAndUsesOfficialLbaForModernEditions()
    {
        var catalog = new BackfillCatalog();
        var wikipedia = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == ItalianCupWikipediaBasketballDataProvider.Source &&
            league.Country == "Italy" && league.LeagueName == "Italian Cup");
        var official = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == LbaOfficialSerieABasketballDataProvider.Source &&
            league.Country == "Italy" && league.LeagueName == "Italian Cup");
        Assert.DoesNotContain(catalog.GetLeagues(), league =>
            league.Provider == ApiSportsBasketballDataProvider.Source &&
            league.Country == "Italy" && league.LeagueName == "Italian Cup");

        var historicalSeasons = catalog.GetSeasonsForLeague(wikipedia).ToList();
        Assert.Equal(25, historicalSeasons.Count);
        Assert.Equal("1983-1984", historicalSeasons[0]);
        Assert.Equal("2007-2008", historicalSeasons[^1]);
        Assert.DoesNotContain("1982-1983", historicalSeasons);
        var officialSeasons = catalog.GetSeasonsForLeague(official).ToList();
        Assert.Equal(18, officialSeasons.Count);
        Assert.Equal("2008-2009", officialSeasons[0]);
        Assert.Equal("2025-2026", officialSeasons[^1]);
        Assert.Equal("domestic_cup", wikipedia.CompetitionType);
        Assert.Equal(wikipedia.DisplayName, official.DisplayName);

        var regularLeagueSeasons = catalog.GetLeagues()
            .Where(league => league.Country == "Italy" && league.LeagueName is "Serie A" or "Lega A")
            .SelectMany(catalog.GetSeasonsForLeague)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cupSeasons = catalog.GetLeagues()
            .Where(league => league.Country == "Italy" && league.LeagueName == "Italian Cup")
            .SelectMany(catalog.GetSeasonsForLeague);
        Assert.All(cupSeasons, season => Assert.Contains(season, regularLeagueSeasons));
    }

    [Fact]
    public void PolishCupUsesTheProviderEndYearSeasonKey()
    {
        var catalog = new BackfillCatalog();
        var polishCup = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == ApiSportsBasketballDataProvider.Source &&
            league.Country == "Poland" &&
            league.LeagueName == "Polish Cup");

        var mapping = Assert.Single(polishCup.ProviderLeagues!);
        Assert.Equal("end_year", mapping.SeasonParameterFormat);
        Assert.Contains("2020-2021", catalog.GetSeasonsForLeague(polishCup));
    }

    [Fact]
    public void CovidFallbackSeasonsAreNotExposedAsRedundantApiSportsBackfills()
    {
        var catalog = new BackfillCatalog();
        var fallbackLeagues = new[]
        {
            (Country: "Spain", LeagueName: "ACB"),
            (Country: "Europe", LeagueName: "ABA League"),
            (Country: "Europe", LeagueName: "BIBL"),
            (Country: "Europe", LeagueName: "Champions League"),
            (Country: "Europe", LeagueName: "Eurocup"),
            (Country: "Europe", LeagueName: "Euroleague")
        };

        foreach (var (country, leagueName) in fallbackLeagues)
        {
            var league = Assert.Single(catalog.GetLeagues(), candidate =>
                candidate.Provider == ApiSportsBasketballDataProvider.Source &&
                candidate.Country == country &&
                candidate.LeagueName == leagueName);

            Assert.DoesNotContain("2019-2020", catalog.GetSeasonsForLeague(league));
        }
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
