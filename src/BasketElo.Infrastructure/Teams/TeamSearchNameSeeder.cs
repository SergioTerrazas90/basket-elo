using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Infrastructure.Teams;

public static class TeamSearchNameSeeder
{
    public static async Task SeedInternationalTeamSearchNamesAsync(
        BasketEloDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var definitions = InternationalTeamCatalog.GetSearchNames();
        if (definitions.Count == 0)
        {
            return;
        }

        var canonicalNames = definitions
            .Select(x => x.CanonicalName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var teams = await dbContext.Teams
            .Where(x => canonicalNames.Contains(x.CanonicalName))
            .ToListAsync(cancellationToken);

        var matches = definitions
            .Select(definition => new
            {
                Definition = definition,
                Teams = teams
                    .Where(team =>
                        string.Equals(team.CanonicalName, definition.CanonicalName, StringComparison.OrdinalIgnoreCase) &&
                        CountryCodeCatalog.AreEquivalent(team.CountryCode, definition.CountryCode))
                    .ToList()
            })
            .Where(x => x.Teams.Count > 0)
            .ToList();

        if (matches.Count == 0)
        {
            return;
        }

        var teamIds = matches
            .SelectMany(x => x.Teams)
            .Select(x => x.Id)
            .Distinct()
            .ToList();
        var existingNames = await dbContext.TeamSearchNames
            .Where(x => teamIds.Contains(x.TeamId))
            .ToListAsync(cancellationToken);

        var added = false;
        foreach (var match in matches)
        {
            var normalizedName = InternationalTeamCatalog.NormalizeSearchTerm(match.Definition.Name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                continue;
            }

            foreach (var team in match.Teams)
            {
                if (existingNames.Any(existing =>
                        existing.TeamId == team.Id &&
                        string.Equals(existing.Locale, match.Definition.Locale, StringComparison.OrdinalIgnoreCase) &&
                        existing.NormalizedName == normalizedName))
                {
                    continue;
                }

                var searchName = new TeamSearchName
                {
                    Id = Guid.NewGuid(),
                    TeamId = team.Id,
                    Locale = match.Definition.Locale,
                    Name = match.Definition.Name,
                    NormalizedName = normalizedName,
                    CreatedAtUtc = DateTime.UtcNow
                };
                dbContext.TeamSearchNames.Add(searchName);
                existingNames.Add(searchName);
                added = true;
            }
        }

        if (added)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
