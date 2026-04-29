using System.Text.Json;
using BasketElo.Domain.Backfill;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BasketElo.Infrastructure.Backfill;

public class BackfillJobProcessor(
    BasketEloDbContext dbContext,
    IEnumerable<IBasketballDataProvider> providers,
    ILogger<BackfillJobProcessor> logger) : IBackfillJobProcessor
{
    public async Task<bool> TryProcessNextPendingJobAsync(CancellationToken cancellationToken)
    {
        var job = await dbContext.BackfillJobs
            .Where(x => x.Status == BackfillJobStatus.Pending)
            .OrderBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return false;
        }

        job.Status = BackfillJobStatus.Running;
        job.StartedAtUtc = DateTime.UtcNow;
        job.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await ProcessJobAsync(job, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backfill job {jobId} failed.", job.Id);
            job.Status = BackfillJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.FinishedAtUtc = DateTime.UtcNow;
            job.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private async Task ProcessJobAsync(BackfillJob job, CancellationToken cancellationToken)
    {
        var provider = providers.FirstOrDefault(x =>
            string.Equals(x.SourceKey, job.Provider, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            throw new InvalidOperationException($"Provider '{job.Provider}' is not registered.");
        }

        ConsumeRequestBudget(job);
        var league = await provider.ResolveLeagueAsync(
            job.Country,
            job.LeagueName,
            new BackfillExecutionContext(job.MaxRequests, job.RequestsUsed - 1),
            cancellationToken);

        if (league is null)
        {
            job.WarningCount += 1;
            CompleteJob(job, BackfillJobStatus.CompletedWithWarnings, new
            {
                message = "League not found for provided country/name.",
                requestsUsed = job.RequestsUsed
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        ConsumeRequestBudget(job);
        var gamesResult = await provider.GetGamesAsync(
            league,
            job.Season,
            new BackfillExecutionContext(job.MaxRequests, job.RequestsUsed - 1),
            cancellationToken);

        if (gamesResult.HasMorePages)
        {
            job.WarningCount += 1;
        }

        job.WarningCount += gamesResult.Warnings.Count;

        var summary = new BackfillSummary
        {
            LeagueName = league.Name,
            Season = job.Season,
            Source = provider.SourceKey,
            RequestsUsed = job.RequestsUsed,
            HasMorePages = gamesResult.HasMorePages,
            GamesFetched = gamesResult.Games.Count
        };

        if (gamesResult.HasMorePages)
        {
            summary.Warnings.Add("The provider returned more pages than the current request budget allowed, so only the first page of games was imported.");
        }

        summary.Warnings.AddRange(gamesResult.Warnings);

        if (!job.DryRun)
        {
            var competition = await GetOrCreateCompetitionAsync(league, cancellationToken);
            var season = await GetOrCreateSeasonAsync(competition, job.Season, cancellationToken);

            foreach (var providerGame in gamesResult.Games)
            {
                var homeTeam = await GetOrCreateTeamAsync(providerGame.Source, providerGame.SourceHomeTeamId, providerGame.HomeTeamName, cancellationToken);
                var awayTeam = await GetOrCreateTeamAsync(providerGame.Source, providerGame.SourceAwayTeamId, providerGame.AwayTeamName, cancellationToken);

                var existingGame = await dbContext.Games
                    .FirstOrDefaultAsync(x =>
                        x.Source == providerGame.Source &&
                        x.SourceGameId == providerGame.SourceGameId,
                        cancellationToken);

                if (existingGame is null)
                {
                    dbContext.Games.Add(new Game
                    {
                        Id = Guid.NewGuid(),
                        Source = providerGame.Source,
                        SourceGameId = providerGame.SourceGameId,
                        CompetitionId = competition.Id,
                        SeasonId = season.Id,
                        GameDateTimeUtc = providerGame.GameDateTimeUtc,
                        HomeTeamId = homeTeam.Id,
                        AwayTeamId = awayTeam.Id,
                        HomeScore = providerGame.HomeScore,
                        AwayScore = providerGame.AwayScore,
                        Status = providerGame.Status,
                        IngestedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    });

                    summary.GamesInserted += 1;
                }
                else
                {
                    existingGame.GameDateTimeUtc = providerGame.GameDateTimeUtc;
                    existingGame.HomeScore = providerGame.HomeScore;
                    existingGame.AwayScore = providerGame.AwayScore;
                    existingGame.Status = providerGame.Status;
                    existingGame.UpdatedAtUtc = DateTime.UtcNow;
                    summary.GamesUpdated += 1;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        CompleteJob(
            job,
            job.WarningCount > 0 ? BackfillJobStatus.CompletedWithWarnings : BackfillJobStatus.Completed,
            summary);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Competition> GetOrCreateCompetitionAsync(BasketballProviderLeague providerLeague, CancellationToken cancellationToken)
    {
        var alias = await dbContext.CompetitionAliases
            .Include(x => x.Competition)
            .FirstOrDefaultAsync(
                x => x.Source == providerLeague.Source && x.SourceCompetitionId == providerLeague.SourceLeagueId,
                cancellationToken);

        if (alias is not null)
        {
            return alias.Competition;
        }

        var countryCode = NormalizeCountryCode(providerLeague.CountryCode);
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(
                x => x.Name == providerLeague.Name && x.CountryCode == countryCode,
                cancellationToken);

        if (competition is null)
        {
            competition = new Competition
            {
                Id = Guid.NewGuid(),
                Name = providerLeague.Name,
                Type = string.IsNullOrWhiteSpace(providerLeague.CountryCode) ? "international" : "domestic_first_division",
                CountryCode = countryCode,
                Tier = 1,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.Competitions.Add(competition);
        }

        dbContext.CompetitionAliases.Add(new CompetitionAlias
        {
            Id = Guid.NewGuid(),
            CompetitionId = competition.Id,
            Source = providerLeague.Source,
            SourceCompetitionId = providerLeague.SourceLeagueId,
            AliasName = providerLeague.Name,
            CreatedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return competition;
    }

    private async Task<Season> GetOrCreateSeasonAsync(Competition competition, string seasonLabel, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Seasons
            .FirstOrDefaultAsync(
                x => x.CompetitionId == competition.Id && x.Label == seasonLabel,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var (startDate, endDate) = ParseSeasonDates(seasonLabel);
        var season = new Season
        {
            Id = Guid.NewGuid(),
            CompetitionId = competition.Id,
            Label = seasonLabel,
            StartDateUtc = startDate,
            EndDateUtc = endDate,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.Seasons.Add(season);
        await dbContext.SaveChangesAsync(cancellationToken);
        return season;
    }

    private async Task<Team> GetOrCreateTeamAsync(
        string source,
        string sourceTeamId,
        string teamName,
        CancellationToken cancellationToken)
    {
        var alias = await dbContext.TeamAliases
            .Include(x => x.Team)
            .FirstOrDefaultAsync(
                x => x.Source == source && x.SourceTeamId == sourceTeamId,
                cancellationToken);

        if (alias is not null)
        {
            return alias.Team;
        }

        var team = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = teamName,
            CountryCode = "UNK",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        dbContext.Teams.Add(team);

        dbContext.TeamAliases.Add(new TeamAlias
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Source = source,
            SourceTeamId = sourceTeamId,
            AliasName = teamName,
            CreatedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return team;
    }

    private static (DateTime StartDateUtc, DateTime EndDateUtc) ParseSeasonDates(string seasonLabel)
    {
        var pieces = seasonLabel.Split('-', StringSplitOptions.TrimEntries);
        if (pieces.Length == 2 &&
            int.TryParse(pieces[0], out var startYear) &&
            int.TryParse(pieces[1], out var endYear))
        {
            var start = new DateTime(startYear, 7, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(endYear, 6, 30, 23, 59, 59, DateTimeKind.Utc);
            return (start, end);
        }

        var fallbackStart = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var fallbackEnd = fallbackStart.AddYears(1).AddSeconds(-1);
        return (fallbackStart, fallbackEnd);
    }

    private static string? NormalizeCountryCode(string? providerCountryCode)
    {
        if (string.IsNullOrWhiteSpace(providerCountryCode))
        {
            return null;
        }

        var normalized = providerCountryCode.Trim().ToUpperInvariant();
        return normalized.Length <= 3 ? normalized : normalized[..3];
    }

    private static void ConsumeRequestBudget(BackfillJob job)
    {
        if (job.RequestsUsed >= job.MaxRequests)
        {
            throw new InvalidOperationException($"Backfill request budget reached (maxRequests={job.MaxRequests}).");
        }

        job.RequestsUsed += 1;
    }

    private static void CompleteJob(BackfillJob job, string status, object summary)
    {
        job.Status = status;
        job.FinishedAtUtc = DateTime.UtcNow;
        job.UpdatedAtUtc = DateTime.UtcNow;
        job.SummaryJson = JsonSerializer.Serialize(summary);
    }

    private sealed class BackfillSummary
    {
        public string Source { get; set; } = string.Empty;
        public string LeagueName { get; set; } = string.Empty;
        public string Season { get; set; } = string.Empty;
        public int RequestsUsed { get; set; }
        public bool HasMorePages { get; set; }
        public int GamesFetched { get; set; }
        public int GamesInserted { get; set; }
        public int GamesUpdated { get; set; }
        public List<string> Warnings { get; } = [];
    }
}
