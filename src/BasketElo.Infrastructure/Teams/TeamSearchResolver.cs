using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Infrastructure.Teams;

/// <summary>
/// Resolves user-entered team names to canonical team IDs. Provider identity
/// aliases are intentionally not included here: those aliases are for
/// ingestion and may be tied to a specific source record.
/// </summary>
public static class TeamSearchResolver
{
    public static async Task<HashSet<Guid>> ResolveTeamIdsAsync(
        BasketEloDbContext dbContext,
        string? search,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return [];
        }

        var rawTerm = search.Trim().ToLowerInvariant();
        var normalizedTerm = InternationalTeamCatalog.NormalizeSearchTerm(search);
        var query = dbContext.Teams.AsNoTracking();
        query = string.IsNullOrWhiteSpace(normalizedTerm)
            ? query.Where(x => x.CanonicalName.ToLower().Contains(rawTerm))
            : query.Where(x =>
                x.CanonicalName.ToLower().Contains(rawTerm) ||
                x.SearchNames.Any(name => name.NormalizedName.Contains(normalizedTerm)));

        return (await query
            .Select(x => x.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();
    }
}
