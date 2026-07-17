using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class NbaFranchiseCatalogTests
{
    [Fact]
    public void RelocationsResolveToOneCanonicalFranchise()
    {
        var minneapolis = NbaFranchiseCatalog.Resolve("MNL", "Minneapolis Lakers", 1959);
        var losAngeles = NbaFranchiseCatalog.Resolve("LAL", "Los Angeles Lakers", 1960);
        var fortWayne = NbaFranchiseCatalog.Resolve("FTW", "Fort Wayne Pistons", 1956);
        var detroit = NbaFranchiseCatalog.Resolve("DET", "Detroit Pistons", 1957);

        Assert.Equal("lakers", minneapolis?.Franchise.Key);
        Assert.Equal(minneapolis?.Franchise.Key, losAngeles?.Franchise.Key);
        Assert.Equal("pistons", fortWayne?.Franchise.Key);
        Assert.Equal(fortWayne?.Franchise.Key, detroit?.Franchise.Key);
    }

    [Fact]
    public void CatalogExposesCuratedRelocationsAndRenamesAsTypedIdentityEvents()
    {
        var lakers = NbaFranchiseCatalog.FindByCanonicalName("Los Angeles Lakers");
        var pelicans = NbaFranchiseCatalog.FindByCanonicalName("New Orleans Pelicans");
        var wizards = NbaFranchiseCatalog.FindByCanonicalName("Washington Wizards");

        var lakersMove = Assert.Single(lakers?.IdentityEvents ?? []);
        Assert.Equal(1960, lakersMove.Year);
        Assert.Equal("Minneapolis Lakers", lakersMove.FromName);
        Assert.Equal("Los Angeles Lakers", lakersMove.ToName);
        Assert.Equal(NbaFranchiseIdentityEventType.Relocation, lakersMove.Type);

        Assert.Equal(2, pelicans?.IdentityEvents.Count(x => x.Type == NbaFranchiseIdentityEventType.TemporaryRelocation));
        var bulletsRename = Assert.Single(wizards?.IdentityEvents ?? [], identityEvent => identityEvent.Year == 1997);
        Assert.Equal("Washington Bullets", bulletsRename.FromName);
        Assert.Equal("Washington Wizards", bulletsRename.ToName);
        Assert.Equal(NbaFranchiseIdentityEventType.Rename, bulletsRename.Type);
    }

    [Fact]
    public void AmbiguousAndDefunctFranchisesRemainPredictable()
    {
        var originalCharlotte = NbaFranchiseCatalog.Resolve("CHH", "Charlotte Hornets", 2001);
        var currentCharlotte = NbaFranchiseCatalog.Resolve("CHO", "Charlotte Hornets", 2025);
        var newOrleans = NbaFranchiseCatalog.Resolve("NOH", "New Orleans Hornets", 2002);
        var historicalDenver = NbaFranchiseCatalog.Resolve("DNN", "Denver Nuggets", 1949);
        var modernDenver = NbaFranchiseCatalog.Resolve("DEN", "Denver Nuggets", 2025);

        Assert.Equal(originalCharlotte?.Franchise.Key, currentCharlotte?.Franchise.Key);
        Assert.NotEqual(originalCharlotte?.Franchise.Key, newOrleans?.Franchise.Key);
        Assert.NotEqual(historicalDenver?.Franchise.Key, modernDenver?.Franchise.Key);
        Assert.False(historicalDenver?.Franchise.IsActive);
        Assert.True(modernDenver?.Franchise.IsActive);
    }

    [Fact]
    public void CatalogHasThirtyActiveFranchisesAndNoCrossFranchiseSourceIdCollisions()
    {
        Assert.Equal(30, NbaFranchiseCatalog.All.Count(franchise => franchise.IsActive));
        var collisions = NbaFranchiseCatalog.All
            .SelectMany(franchise => franchise.Aliases.Select(alias => new
            {
                franchise.Key,
                alias.SourceTeamId
            }))
            .GroupBy(item => item.SourceTeamId)
            .Where(group => group.Select(item => item.Key).Distinct().Count() > 1)
            .ToList();

        Assert.Empty(collisions);
    }

    [Theory]
    [InlineData("BAL", "Baltimore Bullets", 1963, "wizards")]
    [InlineData("NYN", "New York Nets", 1976, "nets")]
    public void ResolvesFiveThirtyEightSpecificTeamIds(
        string sourceTeamId,
        string observedName,
        int seasonStartYear,
        string expectedFranchise)
    {
        Assert.Equal(
            expectedFranchise,
            NbaFranchiseCatalog.Resolve(sourceTeamId, observedName, seasonStartYear)?.Franchise.Key);
    }
}
