using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class SerbianClubIdentityCatalogTests
{
    [Theory]
    [InlineData("2008-2009")]
    [InlineData("2009-2010")]
    [InlineData("2010-2011")]
    public void ApiSports1066ResolvesToOriginalFmpThrough2011(string season)
    {
        var identity = SerbianClubIdentityCatalog.Resolve("api-sports", "1066", season);

        Assert.NotNull(identity);
        Assert.Equal("FMP Železnik", identity.CanonicalName);
        Assert.Equal("RS", identity.CountryCode);
        Assert.False(identity.IsActive);
    }

    [Theory]
    [InlineData("2013-2014")]
    [InlineData("2025-2026")]
    public void ApiSports1066ResolvesToModernFmpFrom2013(string season)
    {
        var identity = SerbianClubIdentityCatalog.Resolve("api-sports", "1066", season);

        Assert.NotNull(identity);
        Assert.Equal("FMP", identity.CanonicalName);
        Assert.Equal("RS", identity.CountryCode);
        Assert.True(identity.IsActive);
    }

    [Theory]
    [InlineData("api-sports", "1066", "2011-2012")]
    [InlineData("api-sports", "1066", "2012-2013")]
    [InlineData("api-sports", "9999", "2010-2011")]
    [InlineData("fiba", "1066", "2010-2011")]
    public void DoesNotGuessOutsideTheCuratedSourceSeasons(
        string source,
        string sourceTeamId,
        string season)
    {
        Assert.Null(SerbianClubIdentityCatalog.Resolve(source, sourceTeamId, season));
    }
}
