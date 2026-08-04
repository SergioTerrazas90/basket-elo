using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class InternationalTeamCatalogTests
{
    [Theory]
    [InlineData("ESP", "Spain", "ES")]
    [InlineData("Spain", "Spain", "ES")]
    [InlineData("GSA-123", "Spain", "ES")]
    [InlineData("TUR", "Turkey", "TR")]
    [InlineData("CON", "Republic of the Congo", "CG")]
    [InlineData("SMN", "Serbia and Montenegro", "SCG")]
    [InlineData("FR Yugoslavia", "Serbia and Montenegro", "SCG")]
    [InlineData("NOR", "Norway", "NO")]
    [InlineData("Bolivia", "Bolivia", "BO")]
    public void ResolvesCodesAndFullNamesToOneCanonicalIdentity(
        string sourceTeamId,
        string expectedName,
        string expectedCode)
    {
        var resolved = InternationalTeamCatalog.TryResolve(
            sourceTeamId,
            sourceTeamId,
            sourceTeamId == "GSA-123" ? "ESP" : null,
            out var canonicalName,
            out var countryCode);

        Assert.True(resolved);
        Assert.Equal(expectedName, canonicalName);
        Assert.Equal(expectedCode, countryCode);
    }

    [Fact]
    public void DoesNotReturnAThreeLetterCodeAsTheCanonicalName()
    {
        Assert.True(InternationalTeamCatalog.TryGetCanonicalName("ESP", out var name));
        Assert.Equal("Spain", name);
    }

    [Fact]
    public void KeepsHistoricalYugoslaviaSeparateFromPost1992Federation()
    {
        Assert.True(InternationalTeamCatalog.TryResolve("YUG", "Yugoslavia", null, out var historicalName, out var historicalCode));
        Assert.True(InternationalTeamCatalog.TryResolve("SMN", "FR Yugoslavia", "SMN", out var successorName, out var successorCode));

        Assert.Equal("Yugoslavia", historicalName);
        Assert.Equal("YUG", historicalCode);
        Assert.Equal("Serbia and Montenegro", successorName);
        Assert.Equal("SCG", successorCode);
    }

    [Theory]
    [InlineData("Yugoslavia", "YUG", true)]
    [InlineData("USSR", "URS", true)]
    [InlineData("Serbia and Montenegro", "SCG", true)]
    [InlineData("United States", "USA", false)]
    public void ClassifiesHistoricalInternationalIdentities(
        string name,
        string code,
        bool expectedHistorical)
    {
        Assert.Equal(expectedHistorical, InternationalTeamCatalog.IsHistoricalIdentity(name, code));
    }
}
