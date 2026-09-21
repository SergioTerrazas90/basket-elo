using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Infrastructure.Elo;

public static class CurrentEuropeanTeamResolver
{
    private const int ActiveCompetitionWindowMonths = 18;

    public static async Task<HashSet<Guid>> ResolveAsync(
        BasketEloDbContext dbContext,
        string? competitionName,
        CancellationToken cancellationToken)
    {
        var scopedGames = dbContext.Games
            .AsNoTracking()
            .Where(game =>
                game.Competition.EloPoolKey == EloPoolKeys.EuropeClubs &&
                game.Competition.IsActive &&
                game.Competition.SupportPolicy == CompetitionSupportPolicies.Supported &&
                (string.IsNullOrWhiteSpace(competitionName) || game.Competition.Name == competitionName));

        var latestGameUtc = await scopedGames
            .Select(game => (DateTime?)game.GameDateTimeUtc)
            .MaxAsync(cancellationToken);
        if (!latestGameUtc.HasValue)
        {
            return [];
        }

        var activeCompetitionCutoffUtc = latestGameUtc.Value.AddMonths(-ActiveCompetitionWindowMonths);
        var recentSeasonRows = await scopedGames
            .Where(game => game.GameDateTimeUtc >= activeCompetitionCutoffUtc)
            .Select(game => new
            {
                game.CompetitionId,
                game.SeasonId,
                game.GameDateTimeUtc,
                game.Id
            })
            .ToListAsync(cancellationToken);

        var latestSeasonIds = recentSeasonRows
            .GroupBy(game => game.CompetitionId)
            .Select(group => group
                .OrderByDescending(game => game.GameDateTimeUtc)
                .ThenByDescending(game => game.Id)
                .First()
                .SeasonId)
            .Distinct()
            .ToArray();
        if (latestSeasonIds.Length == 0)
        {
            return [];
        }

        var latestSeasonGames = dbContext.Games
            .AsNoTracking()
            .Where(game => latestSeasonIds.Contains(game.SeasonId));
        var homeTeamIds = await latestSeasonGames
            .Select(game => game.HomeTeamId)
            .ToListAsync(cancellationToken);
        var awayTeamIds = await latestSeasonGames
            .Select(game => game.AwayTeamId)
            .ToListAsync(cancellationToken);

        return homeTeamIds
            .Concat(awayTeamIds)
            .ToHashSet();
    }
}
