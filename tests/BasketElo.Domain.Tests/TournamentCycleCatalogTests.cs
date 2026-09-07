using BasketElo.Domain.Tournaments;
using Xunit;

namespace BasketElo.Domain.Tests;

public class TournamentCycleCatalogTests
{
    [Theory]
    [InlineData("EuroBasket", "2021")]
    [InlineData("EuroBasket Qualifiers", "2021")]
    [InlineData("FIBA EuroBasket Pre-Qualifiers", "2021")]
    public void StagesShareTheSameEditionKey(string competition, string season)
    {
        Assert.Equal("eurobasket-2022", TournamentCycleCatalog.ResolveKey("Europe", competition, season));
    }

    [Fact]
    public void DivisionBUsesAnIndependentCycleFamily()
    {
        Assert.Equal(
            "eurobasket-division-b-2007",
            TournamentCycleCatalog.ResolveKey("Europe", "FIBA EuroBasket Division B", "2007"));
        Assert.Contains("EuroBasket Division B", TournamentCycleCatalog.SupportedFamilies);
        Assert.Equal(
            "EuroBasket Division B",
            TournamentCycleCatalog.ResolveFamilyFromKey("eurobasket-division-b-2007"));
    }

    [Fact]
    public void AfroBasketStagesShareTheSameEditionKey()
    {
        Assert.Equal("afrobasket-2021", TournamentCycleCatalog.ResolveKey("Africa", "FIBA AfroBasket", "2021"));
        Assert.Equal("afrobasket-2021", TournamentCycleCatalog.ResolveKey("Africa", "FIBA AfroBasket Qualifiers", "2021"));
        Assert.Equal("afrobasket-2021", TournamentCycleCatalog.ResolveKey("Africa", "FIBA AfroBasket Pre-Qualifiers", "2021"));
    }

    [Fact]
    public void AsiaCupStagesShareTheSameEditionKey()
    {
        Assert.Equal("asiacup-2025", TournamentCycleCatalog.ResolveKey("Asia", "FIBA Asia Cup", "2025"));
        Assert.Equal("asiacup-2025", TournamentCycleCatalog.ResolveKey("Asia", "FIBA Asia Cup Qualifiers", "2025"));
        Assert.Equal("asiacup-2025", TournamentCycleCatalog.ResolveKey("Asia", "FIBA Asia Cup Pre-Qualifiers", "2025"));
        Assert.Equal("asiacup-2022", TournamentCycleCatalog.ResolveKey("Asia", "FIBA Asia Cup Qualification", "2021"));
        Assert.Equal("asiacup-2022", TournamentCycleCatalog.ResolveKey("Asia", "FIBA Asia Cup Qualifiers", "2021"));
        Assert.Equal("asiacup-1986", TournamentCycleCatalog.ResolveKey("Asia", "FIBA Asia Cup", "1985"));
    }

    [Fact]
    public void AmeriCupStagesShareTheSameEditionKey()
    {
        Assert.Equal("americup-2025", TournamentCycleCatalog.ResolveKey("Americas", "FIBA AmeriCup", "2025"));
        Assert.Equal("americup-2025", TournamentCycleCatalog.ResolveKey("Americas", "FIBA AmeriCup Qualifiers", "2025"));
        Assert.Equal("americup-2025", TournamentCycleCatalog.ResolveKey("Americas", "FIBA AmeriCup Pre-Qualifiers", "2025"));
        Assert.Equal("americup-2025", TournamentCycleCatalog.ResolveKey("Americas", "FIBA AmeriCup Qualification", "2025"));
    }

    [Fact]
    public void OceaniaChampionshipStagesUseTheirOwnEditionKey()
    {
        Assert.Equal("oceania-2015", TournamentCycleCatalog.ResolveKey("Oceania", "FIBA Oceania Championship", "2015"));
        Assert.Equal("oceania-1971", TournamentCycleCatalog.ResolveKey("Oceania", "Oceania Championship", "1971"));
        Assert.Null(TournamentCycleCatalog.ResolveKey("Asia", "FIBA Oceania Championship", "2015"));
    }

    [Fact]
    public void AmericasRegionalChampionshipsUseSeparateEditionKeys()
    {
        Assert.Equal("centrobasket-2016", TournamentCycleCatalog.ResolveKey("Americas", "Centrobasket Championship", "2016"));
        Assert.Equal("cocaba-2015", TournamentCycleCatalog.ResolveKey("Americas", "COCABA Championship", "2015"));
        Assert.Equal("south-american-2016", TournamentCycleCatalog.ResolveKey("Americas", "South American Championship", "2016"));
        Assert.Equal("caribbean-2015", TournamentCycleCatalog.ResolveKey("Americas", "Caribbean Basketball Championship", "2015"));
        Assert.NotEqual(
            TournamentCycleCatalog.ResolveKey("Americas", "Centrobasket Championship", "2016"),
            TournamentCycleCatalog.ResolveKey("Americas", "FIBA AmeriCup", "2016"));
    }

    [Fact]
    public void OlympicStagesShareTheSameEditionKey()
    {
        Assert.Equal("olympics-2024", TournamentCycleCatalog.ResolveKey("World", "Summer Olympics", "2024"));
        Assert.Equal("olympics-2024", TournamentCycleCatalog.ResolveKey("World", "Olympics Qualification", "2024"));
        Assert.Equal("olympics-2024", TournamentCycleCatalog.ResolveKey("World", "Olympics Pre-Qualification", "2024"));
        Assert.Equal("olympics-2024", TournamentCycleCatalog.ResolveKey("World", "FIBA Men's Olympic Basketball Tournament", "2024"));
    }

    [Fact]
    public void WorldCupStagesShareTheSameEditionKey()
    {
        Assert.Equal("worldcup-2027", TournamentCycleCatalog.ResolveKey("World", "FIBA Basketball World Cup", "2027"));
        Assert.Equal("worldcup-2027", TournamentCycleCatalog.ResolveKey("World", "FIBA Basketball World Cup Qualifiers", "2027"));
        Assert.Equal("worldcup-2027", TournamentCycleCatalog.ResolveKey("World", "FIBA Basketball World Cup Pre-Qualifiers", "2027"));
        Assert.Equal("worldcup-2027", TournamentCycleCatalog.ResolveKey("World", "FIBA WC Qualification", "2027"));
        Assert.Equal("worldcup-2027", TournamentCycleCatalog.ResolveKey("Americas", "FIBA World Cup Qualifiers", "2026-2027"));
    }

    [Fact]
    public void ManualFamilyKeysUseTheCanonicalPlayedEdition()
    {
        Assert.Equal("eurobasket-2022", TournamentCycleCatalog.ResolveKeyFromFamily("EuroBasket", "2021"));
        Assert.Equal("asiacup-1986", TournamentCycleCatalog.ResolveKeyFromFamily("FIBA Asia Cup", "1985"));
        Assert.Equal("worldcup-2027", TournamentCycleCatalog.ResolveKeyFromFamily("FIBA Basketball World Cup", "2026-2027"));
    }
}
