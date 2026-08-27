using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Persistence;
using BasketElo.Infrastructure.Teams;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Teams;

public sealed class TeamSearchResolverTests
{
    [Fact]
    public async Task ResolvesSpanishItalianFrenchGermanAndAccentlessNamesToTheSameTeam()
    {
        await using var dbContext = CreateDbContext();
        var spain = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = "Spain",
            CountryCode = "ES"
        };
        dbContext.Teams.Add(spain);
        await dbContext.SaveChangesAsync();

        await TeamSearchNameSeeder.SeedInternationalTeamSearchNamesAsync(dbContext);

        foreach (var search in new[] { "Spain", "España", "espana", "Spagna", "Espagne", "Spanien" })
        {
            var matchingIds = await TeamSearchResolver.ResolveTeamIdsAsync(dbContext, search);
            Assert.Contains(spain.Id, matchingIds);
        }
    }

    [Fact]
    public async Task ALocalizedSearchDoesNotMatchAnUnrelatedTeam()
    {
        await using var dbContext = CreateDbContext();
        var spain = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = "Spain",
            CountryCode = "ES"
        };
        var france = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = "France",
            CountryCode = "FR"
        };
        dbContext.Teams.AddRange(spain, france);
        await dbContext.SaveChangesAsync();

        await TeamSearchNameSeeder.SeedInternationalTeamSearchNamesAsync(dbContext);

        var matchingIds = await TeamSearchResolver.ResolveTeamIdsAsync(dbContext, "España");

        Assert.Equal([spain.Id], matchingIds);
    }

    [Fact]
    public void NormalizationRemovesAccentsAndPunctuation()
    {
        Assert.Equal("ESPANA", InternationalTeamCatalog.NormalizeSearchTerm(" España "));
        Assert.Equal("COTEDIVOIRE", InternationalTeamCatalog.NormalizeSearchTerm("Côte d'Ivoire"));
    }

    private static BasketEloDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
