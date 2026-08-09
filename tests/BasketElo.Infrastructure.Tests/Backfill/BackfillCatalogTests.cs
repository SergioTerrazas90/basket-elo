using BasketElo.Domain.Elo;
using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class BackfillCatalogTests
{
    [Fact]
    public void SerbianHistoricalCatalogCoversAgreedHistoricalCutoff()
    {
        var catalog = new BackfillCatalog();
        var league = Assert.Single(catalog.GetLeagues(), item =>
            item.Provider == SerbianHistoricalBasketballDataProvider.Source &&
            item.Country == "Serbia" &&
            item.LeagueName == "First League");

        Assert.Equal("Serbia: Yugoslav / Serbia and Montenegro / Serbia top flight", league.DisplayName);
        Assert.Equal("1973-1974", league.StartSeason);
        Assert.Equal("2007-2008", league.EndSeason);
        Assert.Equal(35, catalog.GetSeasonsForLeague(league).Count);

        var cup = Assert.Single(catalog.GetLeagues(), item =>
            item.Provider == SerbianHistoricalBasketballDataProvider.Source &&
            item.Country == "Serbia" &&
            item.LeagueName == "Yugoslav Cup");
        Assert.Contains("1991-1992", catalog.GetSeasonsForLeague(cup));
        Assert.Contains("1992-1993", catalog.GetSeasonsForLeague(cup));
        Assert.Contains("1973-1974", catalog.GetSeasonsForLeague(cup));
        Assert.Contains("1999-2000", catalog.GetSeasonsForLeague(cup));
        Assert.Equal(27, catalog.GetSeasonsForLeague(cup).Count);
    }

    [Fact]
    public void GreekOfficialCatalogFillsLegacyGapAndNeverAddsCupWithoutLeague()
    {
        var catalog = new BackfillCatalog();
        var league = Assert.Single(catalog.GetLeagues(), item =>
            item.Provider == GreekOfficialBasketballDataProvider.Source && item.Country == "Greece" && item.LeagueName == "A1");
        var cup = Assert.Single(catalog.GetLeagues(), item =>
            item.Provider == GreekOfficialBasketballDataProvider.Source && item.Country == "Greece" && item.LeagueName == "Greek Cup");

        var leagueSeasons = catalog.GetSeasonsForLeague(league).ToList();
        var cupSeasons = catalog.GetSeasonsForLeague(cup).ToList();

        Assert.Equal(23, leagueSeasons.Count);
        Assert.Equal("1986-1987", leagueSeasons[0]);
        Assert.Equal("2015-2016", leagueSeasons[^1]);
        Assert.Contains("1986-1987", leagueSeasons);
        Assert.Contains("1987-1988", leagueSeasons);
        Assert.Contains("1988-1989", leagueSeasons);
        Assert.Contains("1989-1990", leagueSeasons);
        Assert.Contains("1990-1991", leagueSeasons);
        Assert.Contains("1991-1992", leagueSeasons);
        Assert.Contains("1993-1994", leagueSeasons);
        Assert.Contains("1994-1995", leagueSeasons);
        Assert.Contains("1995-1996", leagueSeasons);
        Assert.Contains("1998-1999", leagueSeasons);
        Assert.Equal(17, cupSeasons.Count);
        Assert.Contains("1992-1993", cupSeasons);
        Assert.Contains("1993-1994", cupSeasons);
        Assert.Contains("1994-1995", cupSeasons);
        Assert.Contains("1995-1996", cupSeasons);
        Assert.Contains("2009-2010", cupSeasons);
        Assert.Contains("2015-2016", cupSeasons);
        Assert.DoesNotContain("2003-2004", cupSeasons);
        Assert.Contains("2015-2016", leagueSeasons);

        var allLeagueSeasons = catalog.GetLeagues()
            .Where(item => item.Country == "Greece" && item.LeagueName == "A1")
            .SelectMany(catalog.GetSeasonsForLeague)
            .ToList();
        var allCupSeasons = catalog.GetLeagues()
            .Where(item => item.Country == "Greece" && item.LeagueName == "Greek Cup")
            .SelectMany(catalog.GetSeasonsForLeague)
            .ToList();
        Assert.Equal(allLeagueSeasons.Count, allLeagueSeasons.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("2008-2009", allLeagueSeasons);
        Assert.Contains("2009-2010", allLeagueSeasons);
        Assert.Contains("2015-2016", allCupSeasons);
        Assert.All(allCupSeasons, season => Assert.Contains(season, allLeagueSeasons));
    }

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
    public void GermanOfficialCatalogFillsTheHistoricalBblGapBeforeApiSports()
    {
        var catalog = new BackfillCatalog();
        var historical = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == GermanBasketballDataProvider.Source &&
            league.Country == "Germany" &&
            league.LeagueName == "BBL");

        var seasons = catalog.GetSeasonsForLeague(historical).ToList();

        Assert.Equal("Germany: BBL", historical.DisplayName);
        Assert.Equal("1975-1976", seasons[0]);
        Assert.Equal("2007-2008", seasons[^1]);
        Assert.Equal(33, seasons.Count);
        Assert.DoesNotContain("2008-2009", seasons);
        Assert.DoesNotContain(catalog.GetLeagues(), league =>
            league.Provider == GermanBasketballDataProvider.Source &&
            league.LeagueName == "German Cup");
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
    public void GermanCupCatalogCoversHistoricalFinalsBeforeApiSports()
    {
        var catalog = new BackfillCatalog();
        var historical = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == GermanCupWikipediaBasketballDataProvider.Source &&
            league.Country == "Germany" && league.LeagueName == "German Cup");
        var modern = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == ApiSportsBasketballDataProvider.Source &&
            league.Country == "Germany" && league.LeagueName == "German Cup");

        var historicalSeasons = catalog.GetSeasonsForLeague(historical).ToList();
        Assert.Equal("1975-1976", historicalSeasons[0]);
        Assert.Equal("2007-2008", historicalSeasons[^1]);
        Assert.Equal(33, historicalSeasons.Count);
        Assert.Equal("2008-2009", catalog.GetSeasonsForLeague(modern).First());
        Assert.Equal("domestic_cup", historical.CompetitionType);
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
    public void AfroBasketFibaMappingsIncludeTheValidated2021And2025Editions()
    {
        var catalog = new BackfillCatalog();
        var source = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == GlobalSportsArchiveBasketballDataProvider.Source &&
            league.Country == "Africa" &&
            league.LeagueName == "FIBA AfroBasket");

        Assert.Contains("2003", catalog.GetSeasonsForLeague(source));
        Assert.Contains("2025", catalog.GetSeasonsForLeague(source));
        var fibaSource = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Africa" &&
            league.LeagueName == "FIBA AfroBasket");
        Assert.Equal(["1962", "1964", "1965", "1968", "1970", "1972", "1974", "1975", "1978", "1980", "1981", "1983", "1985", "1987", "1989", "1993", "1995", "1997", "1999", "2001", "2005", "2007", "2009", "2011", "2013", "2015", "2017", "2021", "2025"], catalog.GetSeasonsForLeague(fibaSource));

        var fibaQualifiers = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Africa" &&
            league.LeagueName == "FIBA AfroBasket Qualifiers");
        Assert.Equal(["2005", "2007", "2009", "2011", "2013", "2015", "2017", "2021", "2025"], catalog.GetSeasonsForLeague(fibaQualifiers));

        var fibaPreQualifiers = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Africa" &&
            league.LeagueName == "FIBA AfroBasket Pre-Qualifiers");
        Assert.Equal(["2021", "2025"], catalog.GetSeasonsForLeague(fibaPreQualifiers));
    }

    [Fact]
    public void AsiaCupFibaCatalogSeparatesFinalsQualifiersAndPreQualifiers()
    {
        var catalog = new BackfillCatalog();
        var finals = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Asia" &&
            league.LeagueName == "FIBA Asia Cup");
        var qualifiers = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Asia" &&
            league.LeagueName == "FIBA Asia Cup Qualifiers");
        var preQualifiers = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Asia" &&
            league.LeagueName == "FIBA Asia Cup Pre-Qualifiers");

        Assert.Contains("2025", catalog.GetSeasonsForLeague(finals));
        Assert.Contains("2002", catalog.GetSeasonsForLeague(finals));
        Assert.Equal(["2021", "2025"], catalog.GetSeasonsForLeague(qualifiers));
        Assert.Equal(["2021", "2025"], catalog.GetSeasonsForLeague(preQualifiers));
        Assert.Equal(EloPoolKeys.NationalTeams, finals.EloPoolKey);
        Assert.True(finals.UsesSingleYearSeasonLabel);
    }

    [Fact]
    public void WorldCupFibaCatalogIncludesPreQualifierCycles()
    {
        var catalog = new BackfillCatalog();
        var preQualifiers = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "World" &&
            league.LeagueName == "FIBA Basketball World Cup Pre-Qualifiers");

        Assert.True(preQualifiers.UsesSingleYearSeasonLabel);
        Assert.Equal(["2019", "2023", "2027"], catalog.GetSeasonsForLeague(preQualifiers));
        Assert.Equal(EloPoolKeys.NationalTeams, preQualifiers.EloPoolKey);
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

    [Fact]
    public void AmeriCupCatalogSeparatesFibaAndGsaStages()
    {
        var catalog = new BackfillCatalog();
        var fibaFinals = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Americas" &&
            league.LeagueName == "FIBA AmeriCup");
        var fibaQualifiers = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Americas" &&
            league.LeagueName == "FIBA AmeriCup Qualifiers");
        var fibaPreQualifiers = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Americas" &&
            league.LeagueName == "FIBA AmeriCup Pre-Qualifiers");
        var gsaPreQualifiers = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == GlobalSportsArchiveBasketballDataProvider.Source &&
            league.Country == "Americas" &&
            league.LeagueName == "FIBA AmeriCup Pre-Qualifiers");

        Assert.Contains("2025", catalog.GetSeasonsForLeague(fibaFinals));
        Assert.Equal(["2022", "2025"], catalog.GetSeasonsForLeague(fibaQualifiers));
        Assert.Equal(["2022", "2025", "2029"], catalog.GetSeasonsForLeague(fibaPreQualifiers));
        Assert.Equal(["2022", "2025"], catalog.GetSeasonsForLeague(gsaPreQualifiers));
        Assert.Equal(EloPoolKeys.NationalTeams, fibaFinals.EloPoolKey);
        Assert.True(fibaFinals.UsesSingleYearSeasonLabel);
    }

    [Fact]
    public void OlympicsCatalogSeparatesFinalsQualifiersAndPreQualifiers()
    {
        var catalog = new BackfillCatalog();
        var fibaFinals = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "World" &&
            league.LeagueName == "Summer Olympics");
        var fibaQualifiers = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "World" &&
            league.LeagueName == "Olympics Qualification");
        var fibaPreQualifiers = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "World" &&
            league.LeagueName == "Olympics Pre-Qualification");
        var gsaPreQualifiers = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == GlobalSportsArchiveBasketballDataProvider.Source &&
            league.Country == "World" &&
            league.LeagueName == "Olympics Pre-Qualification");

        Assert.Contains("1948", catalog.GetSeasonsForLeague(fibaFinals));
        Assert.Contains("2024", catalog.GetSeasonsForLeague(fibaFinals));
        Assert.Contains("2020", catalog.GetSeasonsForLeague(fibaQualifiers));
        Assert.Contains("2024", catalog.GetSeasonsForLeague(fibaQualifiers));
        Assert.Contains("2024", catalog.GetSeasonsForLeague(fibaPreQualifiers));
        Assert.Equal(["2024"], catalog.GetSeasonsForLeague(gsaPreQualifiers));
        Assert.Equal(EloPoolKeys.NationalTeams, fibaFinals.EloPoolKey);
        Assert.True(fibaFinals.UsesSingleYearSeasonLabel);
    }

    [Fact]
    public void WorldCupCatalogSeparatesFibaFinalsAndQualifiers()
    {
        var catalog = new BackfillCatalog();
        var fibaFinals = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "World" &&
            league.LeagueName == "FIBA Basketball World Cup");
        var fibaQualifiers = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "World" &&
            league.LeagueName == "FIBA Basketball World Cup Qualifiers");

        Assert.Equal(["1950", "1954", "1959", "1963", "1967", "1970", "1974", "1978", "1982", "1986", "1990", "1994", "1998", "2002", "2006", "2010", "2014", "2019", "2023"], catalog.GetSeasonsForLeague(fibaFinals));
        Assert.Equal(["2019", "2023", "2027"], catalog.GetSeasonsForLeague(fibaQualifiers));
        Assert.Equal(EloPoolKeys.NationalTeams, fibaFinals.EloPoolKey);
        Assert.Equal(EloPoolKeys.NationalTeams, fibaQualifiers.EloPoolKey);
        Assert.True(fibaFinals.UsesSingleYearSeasonLabel);
        Assert.True(fibaQualifiers.UsesSingleYearSeasonLabel);
    }

    [Fact]
    public void OceaniaCatalogCoversOfficialMenSeniorEditionsAsOneFibaFamily()
    {
        var catalog = new BackfillCatalog();
        var oceania = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Oceania" &&
            league.LeagueName == "FIBA Oceania Championship");

        Assert.Equal(
            ["1971", "1975", "1978", "1979", "1981", "1983", "1985", "1987", "1989", "1991", "1993", "1995", "1997", "1999", "2001", "2003", "2005", "2007", "2009", "2011", "2013", "2015"],
            catalog.GetSeasonsForLeague(oceania));
        Assert.Equal(EloPoolKeys.NationalTeams, oceania.EloPoolKey);
        Assert.True(oceania.UsesSingleYearSeasonLabel);
    }

    [Fact]
    public void AmericasRegionalCatalogKeepsEachFibaFamilySeparate()
    {
        var catalog = new BackfillCatalog();
        var centrobasket = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source && league.LeagueName == "Centrobasket Championship");
        var cocaba = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source && league.LeagueName == "COCABA Championship");
        var southAmerican = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source && league.LeagueName == "South American Championship");
        var caribbean = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source && league.LeagueName == "Caribbean Basketball Championship");

        Assert.Equal(["1965", "1967", "1969", "1971", "1973", "1975", "1977", "1981", "1985", "1987", "1989", "1991", "1993", "1995", "1997", "1998", "1999", "2001", "2002", "2003", "2004", "2006", "2008", "2010", "2012", "2014", "2016"], catalog.GetSeasonsForLeague(centrobasket));
        Assert.Equal(["2003", "2004", "2006", "2007", "2009", "2011", "2013", "2015"], catalog.GetSeasonsForLeague(cocaba));
        Assert.Equal(["1932", "1942", "1943", "1987", "1989", "1991", "1993", "1995", "1997", "1999", "2001", "2003", "2004", "2006", "2008", "2010", "2011", "2012", "2014", "2015", "2016"], catalog.GetSeasonsForLeague(southAmerican));
        Assert.Equal(["2004", "2006", "2007", "2009", "2011", "2014", "2015"], catalog.GetSeasonsForLeague(caribbean));
        Assert.All(new[] { centrobasket, cocaba, southAmerican, caribbean }, league =>
        {
            Assert.Equal("Americas", league.Country);
            Assert.Equal(EloPoolKeys.NationalTeams, league.EloPoolKey);
            Assert.True(league.UsesSingleYearSeasonLabel);
        });
    }

    [Fact]
    public void FibaEuropeanChampionsCupUsesTwoYearSeasonsAndEuropeanClubPool()
    {
        var catalog = new BackfillCatalog();
        var source = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Europe" &&
            league.LeagueName == "FIBA European Champions Cup");

        Assert.Equal(EloPoolKeys.EuropeClubs, source.EloPoolKey);
        Assert.Equal("1958-1959", source.StartSeason);
        Assert.Equal("1999-2000", source.EndSeason);
        Assert.Equal(42, catalog.GetSeasonsForLeague(source).Count);
        Assert.DoesNotContain("2000-2001", catalog.GetSeasonsForLeague(source));
    }

    [Fact]
    public void EuroleagueBridgeUsesFlashscoreFrom2000Through2007()
    {
        var catalog = new BackfillCatalog();
        var source = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == EuroleagueRHistoricalDataProvider.Source &&
            league.Country == "Europe" &&
            league.LeagueName == "Euroleague");

        Assert.Equal(EloPoolKeys.EuropeClubs, source.EloPoolKey);
        Assert.Equal("2000-2001", source.StartSeason);
        Assert.Equal("2007-2008", source.EndSeason);
        Assert.Equal(8, catalog.GetSeasonsForLeague(source).Count);
    }

    [Fact]
    public void FibaSuproLeagueIsConfiguredAsTheSingle2000Season()
    {
        var catalog = new BackfillCatalog();
        var source = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Europe" &&
            league.LeagueName == "FIBA SuproLeague");

        Assert.Equal(EloPoolKeys.EuropeClubs, source.EloPoolKey);
        Assert.Equal(["2000-2001"], catalog.GetSeasonsForLeague(source));
    }

    [Fact]
    public void FibaSaportaCupCoversTheIssueRangeAndUsesEuropeanClubPool()
    {
        var catalog = new BackfillCatalog();
        var source = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Europe" &&
            league.LeagueName == "FIBA Saporta Cup");

        var seasons = catalog.GetSeasonsForLeague(source).ToList();

        Assert.Equal(EloPoolKeys.EuropeClubs, source.EloPoolKey);
        Assert.Equal("1967-1968", seasons[0]);
        Assert.Equal("2001-2002", seasons[^1]);
        Assert.Equal(35, seasons.Count);
        Assert.Equal(35, seasons.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void FibaKoracCupCoversTheIssueRangeAndUsesEuropeanClubPool()
    {
        var catalog = new BackfillCatalog();
        var source = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Europe" &&
            league.LeagueName == "FIBA Korac Cup");

        var seasons = catalog.GetSeasonsForLeague(source).ToList();

        Assert.Equal(EloPoolKeys.EuropeClubs, source.EloPoolKey);
        Assert.Equal("1971-1972", seasons[0]);
        Assert.Equal("2001-2002", seasons[^1]);
        Assert.Equal(31, seasons.Count);
        Assert.Equal(31, seasons.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.NotEqual("FIBA Saporta Cup", source.LeagueName);
    }

    [Fact]
    public void FibaEuropeanTierTwoCoversThePostSaportaLineage()
    {
        var catalog = new BackfillCatalog();
        var source = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == FibaBasketballDataProvider.Source &&
            league.Country == "Europe" &&
            league.LeagueName == "FIBA European Tier 2");

        var seasons = catalog.GetSeasonsForLeague(source).ToList();

        Assert.Equal(EloPoolKeys.EuropeClubs, source.EloPoolKey);
        Assert.Equal("2002-2003", seasons[0]);
        Assert.Equal("2007-2008", seasons[^1]);
        Assert.Equal(6, seasons.Count);
        Assert.DoesNotContain(catalog.GetLeagues(), league =>
            league.LeagueName == "FIBA Saporta Cup" &&
            league.Provider == source.Provider &&
            league.DisplayName == source.DisplayName);
    }

    [Fact]
    public void UlebCupIsConfiguredSeparatelyForItsSixHistoricalSeasons()
    {
        var catalog = new BackfillCatalog();
        var source = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == WikipediaUlebCupHistoricalDataProvider.Source &&
            league.Country == "Europe" &&
            league.LeagueName == "ULEB Cup");

        var seasons = catalog.GetSeasonsForLeague(source).ToList();

        Assert.Equal(EloPoolKeys.EuropeClubs, source.EloPoolKey);
        Assert.Equal("2002-2003", seasons[0]);
        Assert.Equal("2007-2008", seasons[^1]);
        Assert.Equal(6, seasons.Count);
        Assert.Equal(6, seasons.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(catalog.GetLeagues(), league => league.LeagueName == "FIBA Saporta Cup" && league.Provider == source.Provider && league.DisplayName == source.DisplayName);
    }
}
