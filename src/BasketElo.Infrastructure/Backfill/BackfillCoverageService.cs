using BasketElo.Domain.Backfill;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Infrastructure.Backfill;

public class BackfillCoverageService(
    BasketEloDbContext dbContext,
    IBackfillCatalog catalog) : IBackfillCoverageService
{
    public async Task<BackfillCoverageResponse> GetCoverageAsync(
        string? provider,
        string? country,
        string? leagueName,
        CancellationToken cancellationToken)
    {
        var configuredLeagues = catalog.GetLeagues()
            .Where(x => string.IsNullOrWhiteSpace(provider) || string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(country) || string.Equals(x.Country, country, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(leagueName) || string.Equals(x.LeagueName, leagueName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var jobs = await dbContext.BackfillJobs
            .AsNoTracking()
            .Where(x => configuredLeagues.Select(l => l.Provider).Contains(x.Provider))
            .ToListAsync(cancellationToken);

        var competitionKeys = configuredLeagues
            .Select(x => new CompetitionKey(x.Country, x.LeagueName))
            .Distinct()
            .ToList();

        var competitions = await dbContext.Competitions
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var matchedCompetitions = competitions
            .Where(c => competitionKeys.Contains(new CompetitionKey(DisplayCountryFromCode(c.CountryCode), c.Name)))
            .ToList();

        var seasons = await dbContext.Seasons
            .AsNoTracking()
            .Where(x => matchedCompetitions.Select(c => c.Id).Contains(x.CompetitionId))
            .ToListAsync(cancellationToken);

        var games = await dbContext.Games
            .AsNoTracking()
            .Where(x => seasons.Select(s => s.Id).Contains(x.SeasonId))
            .ToListAsync(cancellationToken);

        var latestJobs = jobs
            .GroupBy(x => $"{x.Provider}|{x.Country}|{x.LeagueName}|{x.Season}")
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(j => j.CreatedAtUtc).First());

        var dataPresence = (from competition in matchedCompetitions
                            join season in seasons on competition.Id equals season.CompetitionId
                            join game in games on season.Id equals game.SeasonId into seasonGames
                            select new
                            {
                                Key = $"{competition.Name}|{DisplayCountryFromCode(competition.CountryCode)}|{season.Label}",
                                GameCount = seasonGames.Count()
                            })
            .ToDictionary(x => x.Key, x => x.GameCount);

        var rows = new List<BackfillCoverageRow>();
        foreach (var league in configuredLeagues)
        {
            foreach (var season in catalog.GetSeasonsForLeague(league))
            {
                var jobKey = $"{league.Provider}|{league.Country}|{league.LeagueName}|{season}";
                latestJobs.TryGetValue(jobKey, out var latestJob);

                var dataKey = $"{league.LeagueName}|{league.Country}|{season}";
                var gameCount = dataPresence.GetValueOrDefault(dataKey);
                var dataPresent = gameCount > 0;
                var coverageStatus = ComputeCoverageStatus(latestJob, dataPresent);

                rows.Add(new BackfillCoverageRow(
                    league.Provider,
                    league.Country,
                    league.LeagueName,
                    league.DisplayName,
                    season,
                    coverageStatus,
                    latestJob?.FinishedAtUtc ?? latestJob?.StartedAtUtc,
                    latestJob?.RequestsUsed ?? 0,
                    latestJob?.WarningCount ?? 0,
                    dataPresent,
                    gameCount,
                    latestJob?.Status,
                    latestJob?.Id));
            }
        }

        return new BackfillCoverageResponse(rows
            .OrderBy(x => x.Country)
            .ThenBy(x => x.LeagueName)
            .ThenByDescending(x => x.Season)
            .ToList());
    }

    private static string ComputeCoverageStatus(BackfillJob? job, bool dataPresent)
    {
        if (job is null)
        {
            return dataPresent ? "partial" : "not_started";
        }

        return job.Status switch
        {
            BackfillJobStatus.Pending => "in_progress",
            BackfillJobStatus.Running => "in_progress",
            BackfillJobStatus.CompletedWithWarnings => "completed_with_warnings",
            BackfillJobStatus.Completed when dataPresent => "completed",
            BackfillJobStatus.Completed => "partial",
            BackfillJobStatus.Failed when dataPresent => "partial",
            BackfillJobStatus.Failed => "failed",
            _ => dataPresent ? "partial" : "not_started"
        };
    }

    private static string DisplayCountryFromCode(string? countryCode)
    {
        return countryCode?.ToUpperInvariant() switch
        {
            "ESP" => "Spain",
            "FRA" => "France",
            "LTU" => "Lithuania",
            "GRC" => "Greece",
            "ITA" => "Italy",
            "TUR" => "Turkey",
            "LVA" => "Latvia",
            "BEL" => "Belgium",
            "DEU" => "Germany",
            "ISR" => "Israel",
            "POL" => "Poland",
            "CZE" => "Czech Republic",
            "RUS" => "Russia",
            _ => countryCode ?? string.Empty
        };
    }

    private readonly record struct CompetitionKey(string Country, string LeagueName);
}
