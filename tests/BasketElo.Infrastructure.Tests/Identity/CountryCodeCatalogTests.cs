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
    [InlineData("FRG")]
    [InlineData("TCH")]
    public void Normalize_PreservesHistoricalNationalIdentities(string historicalCode)
    {
        Assert.Equal(historicalCode, CountryCodeCatalog.Normalize(historicalCode));
    }

    [Fact]
    public void AreEquivalent_TreatsAlpha2AndProviderAliasAsOneCountry()
    {
        Assert.True(CountryCodeCatalog.AreEquivalent("IT", "ITA"));
        Assert.True(CountryCodeCatalog.AreEquivalent("US", "USA"));
        Assert.False(CountryCodeCatalog.AreEquivalent("IT", "ES"));
    }
}
