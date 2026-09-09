using BasketElo.Infrastructure.Identity;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Identity;

public sealed class CountryCodeCatalogTests
{
    [Theory]
    [InlineData("IT", "IT")]
    [InlineData("ITA", "IT")]
    [InlineData("ESP", "ES")]
    [InlineData("GER", "DE")]
    [InlineData("GRE", "GR")]
    [InlineData("USA", "US")]
    [InlineData("UK", "GB")]
    public void Normalize_UsesIsoAlpha2ForCurrentCountryAliases(string source, string expected)
    {
        Assert.Equal(expected, CountryCodeCatalog.Normalize(source));
    }

    [Theory]
    [InlineData("YUG")]
    [InlineData("URS")]
    [InlineData("DDR")]
    [InlineData("ENG")]
    [InlineData("FRG")]
    [InlineData("TCH")]
    public void Normalize_PreservesHistoricalAndConstituentNationalIdentities(string historicalCode)
    {
        Assert.Equal(historicalCode, CountryCodeCatalog.Normalize(historicalCode));
    }

    [Fact]
    public void DisplayName_UsesEnglandForSourceSpecificCode()
    {
        Assert.Equal("England", CountryCodeCatalog.DisplayName("ENG"));
    }

    [Theory]
    [InlineData("ESP", "Spain")]
    [InlineData("USA", "United States")]
    [InlineData("YUG", "Yugoslavia")]
    [InlineData("SCO", "Scotland")]
    [InlineData("WAL", "Wales")]
    public void DisplayName_UsesCanonicalNamesInsteadOfCodes(string code, string expected)
    {
        Assert.Equal(expected, CountryCodeCatalog.DisplayName(code));
    }

    [Theory]
    [InlineData("UNK")]
    [InlineData("INT")]
    [InlineData("EUR")]
    [InlineData("XYZ")]
    public void DisplayName_DoesNotExposeUnknownOrRegionalCodes(string code)
    {
        Assert.Equal(string.Empty, CountryCodeCatalog.DisplayName(code));
    }

    [Fact]
    public void AreEquivalent_TreatsAlpha2AndProviderAliasAsOneCountry()
    {
        Assert.True(CountryCodeCatalog.AreEquivalent("IT", "ITA"));
        Assert.True(CountryCodeCatalog.AreEquivalent("US", "USA"));
        Assert.False(CountryCodeCatalog.AreEquivalent("IT", "ES"));
    }
}
