using System.Text.Json;
using BasketElo.Domain.Backfill;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BasketElo.Infrastructure.Backfill;

public class BackfillJobProcessor(
    BasketEloDbContext dbContext,
    IEnumerable<IBasketballDataProvider> providers,
    IIdentityHealthCheckService identityHealthCheckService,
    IBackfillCatalog backfillCatalog,
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

        var executionContext = new BackfillExecutionContext(job.MaxRequests, job.RequestsUsed);
        var configuredLeague = backfillCatalog.GetLeagues().FirstOrDefault(x =>
            string.Equals(x.Provider, job.Provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Country, job.Country, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.LeagueName, job.LeagueName, StringComparison.OrdinalIgnoreCase));

        var providerLeagueMappings = GetProviderLeagueMappings(configuredLeague, job);
        var resolvedLeagues = new List<BasketballProviderLeague>();
        var allGames = new List<BasketballProviderGame>();
        var warnings = new List<string>();
        var hasMorePages = false;
        var canonicalSeason = SeasonLabelNormalizer.ToFullSeasonLabel(job.Season);
        if (!string.Equals(job.Season, canonicalSeason, StringComparison.Ordinal))
        {
            job.Season = canonicalSeason;
            job.UpdatedAtUtc = DateTime.UtcNow;
        }

        foreach (var mapping in providerLeagueMappings)
        {
            var resolvedLeague = await provider.ResolveLeagueAsync(
                mapping.Country,
                mapping.LeagueName,
                executionContext,
                cancellationToken);
            job.RequestsUsed = executionContext.RequestsUsed;

            if (resolvedLeague is null)
            {
                job.WarningCount += 1;
                warnings.Add($"League not found for provider mapping '{mapping.Country}: {mapping.LeagueName}'.");
                continue;
            }

            var league = resolvedLeague with
            {
                SeasonParameterFormat = mapping.SeasonParameterFormat
            };
            resolvedLeagues.Add(league);

            var gamesResult = await provider.GetGamesAsync(
                league,
                canonicalSeason,
                executionContext,
                cancellationToken);
            job.RequestsUsed = executionContext.RequestsUsed;

            if (gamesResult.HasMorePages)
            {
                hasMorePages = true;
                job.WarningCount += 1;
            }

            job.WarningCount += gamesResult.Warnings.Count;
            warnings.AddRange(gamesResult.Warnings.Select(warning => $"{league.Name}: {warning}"));
            allGames.AddRange(gamesResult.Games);
        }

        if (resolvedLeagues.Count == 0)
        {
            CompleteJob(job, BackfillJobStatus.CompletedWithWarnings, new
            {
                message = "No provider league mapping could be resolved.",
                requestsUsed = job.RequestsUsed,
                warnings
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var summary = new BackfillSummary
        {
            LeagueName = configuredLeague?.LeagueName ?? resolvedLeagues[0].Name,
            Season = canonicalSeason,
            Source = provider.SourceKey,
            RequestsUsed = job.RequestsUsed,
            HasMorePages = hasMorePages,
            GamesFetched = allGames.Count
        };

        if (hasMorePages)
        {
            summary.Warnings.Add("The provider returned more pages than the current request budget allowed, so only the first page of games was imported.");
        }

        summary.ProviderLeagues.AddRange(resolvedLeagues.Select(x => $"{x.Name} ({x.SourceLeagueId})"));
        summary.Warnings.AddRange(warnings);

        if (!job.DryRun)
        {
            var competition = await GetOrCreateCompetitionAsync(resolvedLeagues[0], configuredLeague, cancellationToken);

            foreach (var additionalLeague in resolvedLeagues.Skip(1))
            {
                await EnsureCompetitionAliasAsync(competition, additionalLeague, cancellationToken);
            }

            var season = await GetOrCreateSeasonAsync(competition, canonicalSeason, cancellationToken);

            foreach (var providerGame in allGames)
            {
                var homeTeam = await GetOrCreateTeamAsync(providerGame.Source, providerGame.SourceHomeTeamId, providerGame.HomeTeamName, competition.CountryCode, cancellationToken);
                var awayTeam = await GetOrCreateTeamAsync(providerGame.Source, providerGame.SourceAwayTeamId, providerGame.AwayTeamName, competition.CountryCode, cancellationToken);

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
                    existingGame.HomeTeamId = homeTeam.Id;
                    existingGame.AwayTeamId = awayTeam.Id;
                    existingGame.HomeScore = providerGame.HomeScore;
                    existingGame.AwayScore = providerGame.AwayScore;
                    existingGame.Status = providerGame.Status;
                    existingGame.UpdatedAtUtc = DateTime.UtcNow;
                    summary.GamesUpdated += 1;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            if (summary.GamesInserted > 0 || summary.GamesUpdated > 0)
            {
                var changedScope = new IdentityChangedScope
                {
                    Source = provider.SourceKey,
                    Season = season.Label,
                    CountryCode = competition.CountryCode,
                    CompetitionId = competition.Id
                };

                await identityHealthCheckService.InvalidateChangedScopeAsync(
                    changedScope,
                    cancellationToken);

                var identityRun = await identityHealthCheckService.RunAsync(
                    new IdentityHealthCheckRequest
                    {
                        Source = changedScope.Source,
                        Season = changedScope.Season,
                        CountryCode = changedScope.CountryCode,
                        CompetitionId = changedScope.CompetitionId,
                        Force = true
                    },
                    cancellationToken);

                summary.IdentityHealthStatus = identityRun.Status;
                summary.IdentityFindingsCount = identityRun.FindingsCount;
                summary.IdentityBlockersCount = identityRun.UnresolvedBlockersCount;
            }
        }

        CompleteJob(
            job,
            job.WarningCount > 0 ? BackfillJobStatus.CompletedWithWarnings : BackfillJobStatus.Completed,
            summary);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyCollection<ConfiguredProviderLeague> GetProviderLeagueMappings(
        ConfiguredBackfillLeague? configuredLeague,
        BackfillJob job)
    {
        if (configuredLeague?.ProviderLeagues is { Count: > 0 })
        {
            return configuredLeague.ProviderLeagues;
        }

        var seasonParameterFormat = configuredLeague is not null &&
            SeasonLabelNormalizer.IsSingleYearSeason(configuredLeague.StartSeason)
            ? "start_year"
            : "default";

        return [new ConfiguredProviderLeague(job.Country, job.LeagueName, seasonParameterFormat)];
    }

    private async Task<Competition> GetOrCreateCompetitionAsync(
        BasketballProviderLeague providerLeague,
        ConfiguredBackfillLeague? configuredLeague,
        CancellationToken cancellationToken)
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

        var competitionName = configuredLeague?.LeagueName ?? providerLeague.Name;
        var countryCode = NormalizeCountryCode(
            configuredLeague is null
                ? providerLeague.CountryCode
                : CountryCodeFromDisplay(configuredLeague.Country));
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(
                x => x.Name == competitionName && x.CountryCode == countryCode,
                cancellationToken);

        if (competition is null)
        {
            competition = new Competition
            {
                Id = Guid.NewGuid(),
                Name = competitionName,
                Type = configuredLeague?.CompetitionType ??
                    (string.IsNullOrWhiteSpace(countryCode) ? "international" : "domestic_first_division"),
                CountryCode = countryCode,
                Tier = 1,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.Competitions.Add(competition);
        }

        await EnsureCompetitionAliasAsync(competition, providerLeague, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return competition;
    }

    private async Task EnsureCompetitionAliasAsync(
        Competition competition,
        BasketballProviderLeague providerLeague,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.CompetitionAliases.AnyAsync(
            x => x.Source == providerLeague.Source && x.SourceCompetitionId == providerLeague.SourceLeagueId,
            cancellationToken);

        if (exists)
        {
            return;
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
    }

    private async Task<Season> GetOrCreateSeasonAsync(Competition competition, string seasonLabel, CancellationToken cancellationToken)
    {
        var canonicalSeasonLabel = SeasonLabelNormalizer.ToFullSeasonLabel(seasonLabel);
        var existing = await dbContext.Seasons
            .FirstOrDefaultAsync(
                x => x.CompetitionId == competition.Id && x.Label == canonicalSeasonLabel,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var legacyLabel = LegacySingleYearLabel(canonicalSeasonLabel);
        if (legacyLabel is not null)
        {
            existing = await dbContext.Seasons
                .FirstOrDefaultAsync(
                    x => x.CompetitionId == competition.Id && x.Label == legacyLabel,
                    cancellationToken);

            if (existing is not null)
            {
                var (updatedStartDate, updatedEndDate) = ParseSeasonDates(canonicalSeasonLabel);
                existing.Label = canonicalSeasonLabel;
                existing.StartDateUtc = updatedStartDate;
                existing.EndDateUtc = updatedEndDate;
                await dbContext.SaveChangesAsync(cancellationToken);
                return existing;
            }
        }

        var (startDate, endDate) = ParseSeasonDates(canonicalSeasonLabel);
        var season = new Season
        {
            Id = Guid.NewGuid(),
            CompetitionId = competition.Id,
            Label = canonicalSeasonLabel,
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
        string? countryCode,
        CancellationToken cancellationToken)
    {
        sourceTeamId = NormalizeSourceTeamId(sourceTeamId, teamName);

        var alias = await dbContext.TeamAliases
            .Include(x => x.Team)
            .FirstOrDefaultAsync(
                x => x.Source == source && x.SourceTeamId == sourceTeamId,
                cancellationToken);

        if (alias is not null)
        {
            var aliasChanged = false;

            if (alias.Team.CountryCode == "UNK" && !string.IsNullOrWhiteSpace(countryCode))
            {
                alias.Team.CountryCode = countryCode;
                aliasChanged = true;
            }

            var hasObservedName = await dbContext.TeamAliases.AnyAsync(
                x =>
                    x.Source == source &&
                    x.SourceTeamId == sourceTeamId &&
                    x.TeamId == alias.TeamId &&
                    x.AliasName == teamName,
                cancellationToken);

            if (!hasObservedName)
            {
                dbContext.TeamAliases.Add(new TeamAlias
                {
                    Id = Guid.NewGuid(),
                    TeamId = alias.TeamId,
                    Source = source,
                    SourceTeamId = sourceTeamId,
                    AliasName = teamName,
                    CreatedAtUtc = DateTime.UtcNow
                });

                aliasChanged = true;
            }

            if (aliasChanged)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return alias.Team;
        }

        var team = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = teamName,
            CountryCode = string.IsNullOrWhiteSpace(countryCode) ? "UNK" : countryCode,
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

    private static string NormalizeSourceTeamId(string sourceTeamId, string teamName)
    {
        if (!string.IsNullOrWhiteSpace(sourceTeamId) &&
            sourceTeamId != "0" &&
            !sourceTeamId.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return sourceTeamId.Trim();
        }

        var normalizedName = new string(teamName
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        normalizedName = string.Join('-', normalizedName.Split('-', StringSplitOptions.RemoveEmptyEntries));

        return $"name:{normalizedName}";
    }

    private static (DateTime StartDateUtc, DateTime EndDateUtc) ParseSeasonDates(string seasonLabel)
    {
        var pieces = SeasonLabelNormalizer.ToFullSeasonLabel(seasonLabel)
            .Split('-', StringSplitOptions.TrimEntries);
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

    private static string? LegacySingleYearLabel(string canonicalSeasonLabel)
    {
        var pieces = canonicalSeasonLabel.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return pieces.Length == 2 && int.TryParse(pieces[0], out var _)
            ? pieces[0]
            : null;
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

    private static string? CountryCodeFromDisplay(string country)
    {
        return country switch
        {
            "Spain" => "ES",
            "France" => "FR",
            "Lithuania" => "LT",
            "Greece" => "GR",
            "Italy" => "IT",
            "Turkey" => "TR",
            "Belgium" => "BE",
            "Germany" => "DE",
            "Israel" => "IL",
            "Poland" => "PL",
            "Czech Republic" => "CZ",
            "Russia" => "RU",
            "Serbia" => "RS",
            "Croatia" => "HR",
            "Slovenia" => "SI",
            "Latvia" => "LV",
            "Estonia" => "EE",
            "Europe" => null,
            _ => null
        };
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
        public List<string> ProviderLeagues { get; } = [];
        public List<string> Warnings { get; } = [];
        public string? IdentityHealthStatus { get; set; }
        public int IdentityFindingsCount { get; set; }
        public int IdentityBlockersCount { get; set; }
    }
}
