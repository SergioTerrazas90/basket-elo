using System.Globalization;
using System.Text;
using System.Text.Json;
using BasketElo.Domain.Backfill;
using BasketElo.Domain.Elo;
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Status = BackfillJobStatus.Pending;
            job.StartedAtUtc = null;
            job.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backfill job {jobId} failed.", job.Id);
            var httpException = ex as HttpRequestException;
            var retryException = ex as BackfillHttpRequestException;
            var failedAtUtc = DateTime.UtcNow;
            job.Status = BackfillJobStatus.Failed;
            job.ErrorMessage = Truncate(
                $"{job.Provider} backfill failed for {job.Country}: {job.LeagueName} {job.Season}. " +
                $"{ex.GetType().Name}: {ex.Message}",
                4000);
            job.SummaryJson = JsonSerializer.Serialize(new
            {
                failure = new
                {
                    jobId = job.Id,
                    provider = job.Provider,
                    country = job.Country,
                    leagueName = job.LeagueName,
                    season = job.Season,
                    exceptionType = ex.GetType().FullName,
                    message = ex.Message,
                    isTransientHttpFailure = retryException is not null,
                    httpStatusCode = httpException?.StatusCode is null ? null : (int?)httpException.StatusCode,
                    attempts = retryException?.Attempts ?? 1,
                    requestsUsed = job.RequestsUsed,
                    failedAtUtc,
                    retryEndpoint = "/api/backfill/jobs"
                }
            });
            job.FinishedAtUtc = failedAtUtc;
            job.UpdatedAtUtc = failedAtUtc;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private async Task ProcessJobAsync(BackfillJob job, CancellationToken cancellationToken)
    {
        string? changedPoolKey = null;
        var canQueuePoolRebuild = false;
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
        var filteredGameReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var hasMorePages = false;
        var usesSingleYearSeasonLabel = configuredLeague?.UsesSingleYearSeasonLabel == true;
        var canonicalSeason = SeasonLabelNormalizer.ToCanonicalSeasonLabel(job.Season, usesSingleYearSeasonLabel);
        if (!string.Equals(job.Season, canonicalSeason, StringComparison.Ordinal))
        {
            job.Season = canonicalSeason;
            job.UpdatedAtUtc = DateTime.UtcNow;
        }

        foreach (var mapping in providerLeagueMappings)
        {
            BasketballProviderLeague? resolvedLeague;
            try
            {
                resolvedLeague = await provider.ResolveLeagueAsync(
                    mapping.Country,
                    mapping.LeagueName,
                    executionContext,
                    cancellationToken);
            }
            finally
            {
                job.RequestsUsed = executionContext.RequestsUsed;
            }

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

            (IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings) gamesResult;
            try
            {
                gamesResult = await provider.GetGamesAsync(
                    league,
                    canonicalSeason,
                    executionContext,
                    cancellationToken);
            }
            finally
            {
                job.RequestsUsed = executionContext.RequestsUsed;
            }

            if (gamesResult.HasMorePages)
            {
                hasMorePages = true;
                job.WarningCount += 1;
            }

            job.WarningCount += gamesResult.Warnings.Count;
            warnings.AddRange(gamesResult.Warnings.Select(warning => $"{league.Name}: {warning}"));
            foreach (var game in gamesResult.Games)
            {
                allGames.Add(game);
                if (game.ExclusionReason is not null)
                {
                    filteredGameReasons[game.ExclusionReason] =
                        filteredGameReasons.GetValueOrDefault(game.ExclusionReason) + 1;
                }
            }
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
            GamesFetched = allGames.Count,
            GamesFiltered = filteredGameReasons.Values.Sum()
        };
        summary.FilteredGameReasons.AddRange(filteredGameReasons
            .OrderBy(x => x.Key)
            .Select(x => $"{x.Key}:{x.Value}"));

        if (hasMorePages)
        {
            summary.Warnings.Add("The provider returned more pages than the current request budget allowed, so only the first page of games was imported.");
        }

        summary.ProviderLeagues.AddRange(resolvedLeagues.Select(x => $"{x.Name} ({x.SourceLeagueId})"));
        summary.Warnings.AddRange(warnings);
        summary.SourceUrls.AddRange(allGames
            .Select(x => x.Provenance?.SourceUrl)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal));
        summary.SourceSeasonKeys.AddRange(allGames
            .Select(x => x.Provenance?.SourceSeasonKey)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal));
        summary.ParserVersions.AddRange(allGames
            .Select(x => x.Provenance?.ParserVersion)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal));
        summary.SourceFetchedAtUtc = allGames
            .Select(x => x.Provenance?.FetchedAtUtc)
            .Where(x => x.HasValue)
            .Max();

        if (!job.DryRun)
        {
            var competition = await GetOrCreateCompetitionAsync(resolvedLeagues[0], configuredLeague, cancellationToken);

            foreach (var additionalLeague in resolvedLeagues.Skip(1))
            {
                await EnsureCompetitionAliasAsync(competition, additionalLeague, cancellationToken);
            }

            var season = await GetOrCreateSeasonAsync(competition, canonicalSeason, usesSingleYearSeasonLabel, cancellationToken);

            foreach (var providerGame in allGames)
            {
                var homeCountryCode = providerGame.SourceHomeTeamCountryCode ??
                    (string.Equals(providerGame.Source, FibaBasketballDataProvider.Source, StringComparison.OrdinalIgnoreCase)
                        ? FibaBasketballDataProvider.CountryCodeFromTeamId(providerGame.SourceHomeTeamId)
                        : competition.CountryCode);
                var awayCountryCode = providerGame.SourceAwayTeamCountryCode ??
                    (string.Equals(providerGame.Source, FibaBasketballDataProvider.Source, StringComparison.OrdinalIgnoreCase)
                        ? FibaBasketballDataProvider.CountryCodeFromTeamId(providerGame.SourceAwayTeamId)
                        : competition.CountryCode);
                var homeTeam = await GetOrCreateTeamAsync(providerGame.Source, providerGame.SourceHomeTeamId, providerGame.HomeTeamName, homeCountryCode, canonicalSeason, cancellationToken);
                var awayTeam = await GetOrCreateTeamAsync(providerGame.Source, providerGame.SourceAwayTeamId, providerGame.AwayTeamName, awayCountryCode, canonicalSeason, cancellationToken);

                var existingGame = await dbContext.Games
                    .FirstOrDefaultAsync(x =>
                        x.Source == providerGame.Source &&
                        x.SourceGameId == providerGame.SourceGameId,
                        cancellationToken);

                if (existingGame is null)
                {
                    var duplicateAcrossSources = await dbContext.Games
                        .Where(x =>
                            x.SeasonId == season.Id &&
                            x.Source != providerGame.Source &&
                            x.GameDateTimeUtc >= providerGame.GameDateTimeUtc.Date &&
                            x.GameDateTimeUtc < providerGame.GameDateTimeUtc.Date.AddDays(1) &&
                            x.HomeScore == providerGame.HomeScore &&
                            x.AwayScore == providerGame.AwayScore &&
                            (x.HomeTeamId == homeTeam.Id ||
                                (!string.IsNullOrWhiteSpace(homeCountryCode) && x.HomeTeam.CountryCode == homeCountryCode)) &&
                            (x.AwayTeamId == awayTeam.Id ||
                                (!string.IsNullOrWhiteSpace(awayCountryCode) && x.AwayTeam.CountryCode == awayCountryCode)))
                        .AnyAsync(cancellationToken);
                    if (duplicateAcrossSources)
                    {
                        summary.GamesDeduplicated += 1;
                        continue;
                    }

                    dbContext.Games.Add(new Game
                    {
                        Id = Guid.NewGuid(),
                        Source = providerGame.Source,
                        SourceGameId = providerGame.SourceGameId,
                        SourceUrl = providerGame.Provenance?.SourceUrl,
                        SourceSeasonKey = providerGame.Provenance?.SourceSeasonKey,
                        SourceFetchedAtUtc = providerGame.Provenance?.FetchedAtUtc,
                        SourceRevision = providerGame.Provenance?.SourceRevision,
                        ParserVersion = providerGame.Provenance?.ParserVersion,
                        CompetitionId = competition.Id,
                        SeasonId = season.Id,
                        GameDateTimeUtc = providerGame.GameDateTimeUtc,
                        HomeTeamId = homeTeam.Id,
                        AwayTeamId = awayTeam.Id,
                        HomeScore = providerGame.HomeScore,
                        AwayScore = providerGame.AwayScore,
                        Status = providerGame.Status,
                        CompetitionPhase = providerGame.CompetitionPhase,
                        CompetitionRound = providerGame.CompetitionRound,
                        EloEligible = providerGame.ExclusionReason is null,
                        EloExclusionReason = providerGame.ExclusionReason,
                        IngestedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    });

                    summary.GamesInserted += 1;
                }
                else
                {
                    var preserveManualResult = existingGame.HasManualResultOverride;
                    existingGame.CompetitionId = competition.Id;
                    existingGame.SeasonId = season.Id;
                    existingGame.GameDateTimeUtc = providerGame.GameDateTimeUtc;
                    existingGame.HomeTeamId = homeTeam.Id;
                    existingGame.AwayTeamId = awayTeam.Id;
                    if (!preserveManualResult)
                    {
                        existingGame.HomeScore = providerGame.HomeScore;
                        existingGame.AwayScore = providerGame.AwayScore;
                        existingGame.Status = providerGame.Status;
                        existingGame.CompetitionPhase = providerGame.CompetitionPhase;
                        existingGame.CompetitionRound = providerGame.CompetitionRound;
                        existingGame.EloEligible = providerGame.ExclusionReason is null;
                        existingGame.EloExclusionReason = providerGame.ExclusionReason;
                    }
                    existingGame.SourceUrl = providerGame.Provenance?.SourceUrl;
                    existingGame.SourceSeasonKey = providerGame.Provenance?.SourceSeasonKey;
                    existingGame.SourceFetchedAtUtc = providerGame.Provenance?.FetchedAtUtc;
                    existingGame.SourceRevision = providerGame.Provenance?.SourceRevision;
                    existingGame.ParserVersion = providerGame.Provenance?.ParserVersion;
                    existingGame.UpdatedAtUtc = DateTime.UtcNow;
                    summary.GamesUpdated += 1;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            if (summary.GamesInserted > 0 || summary.GamesUpdated > 0)
            {
                changedPoolKey = competition.EloPoolKey;
                var changedScope = new IdentityChangedScope
                {
                    EloPoolKey = competition.EloPoolKey,
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
                        EloPoolKey = changedScope.EloPoolKey,
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
                canQueuePoolRebuild = identityRun.UnresolvedBlockersCount == 0;
            }
        }

        CompleteJob(
            job,
            job.WarningCount > 0 ? BackfillJobStatus.CompletedWithWarnings : BackfillJobStatus.Completed,
            summary);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (canQueuePoolRebuild && !string.IsNullOrWhiteSpace(changedPoolKey))
        {
            await QueuePoolRebuildsIfReadyAsync(changedPoolKey, cancellationToken);
        }
    }

    private async Task QueuePoolRebuildsIfReadyAsync(string poolKey, CancellationToken cancellationToken)
    {
        var unfinishedJobs = await dbContext.BackfillJobs
            .AsNoTracking()
            .Where(x => x.Status == BackfillJobStatus.Pending || x.Status == BackfillJobStatus.Running)
            .Select(x => new { x.Provider, x.Country, x.LeagueName })
            .ToListAsync(cancellationToken);
        var configuredLeagues = backfillCatalog.GetLeagues();
        var poolStillHasJobs = unfinishedJobs.Any(job => configuredLeagues.Any(league =>
            string.Equals(league.Provider, job.Provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(league.Country, job.Country, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(league.LeagueName, job.LeagueName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(league.EloPoolKey, poolKey, StringComparison.Ordinal)));
        if (poolStillHasJobs)
        {
            return;
        }

        var activeRulesets = await dbContext.EloRebuildRuns
            .AsNoTracking()
            .Where(x => x.EloPoolKey == poolKey &&
                (x.Status == EloRebuildRunStatus.Pending || x.Status == EloRebuildRunStatus.Running))
            .Select(x => x.RulesetVersion)
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var runs = EloRulesetVersions.All
            .Where(ruleset => !activeRulesets.Contains(ruleset))
            .Select(ruleset => new EloRebuildRun
            {
                Id = Guid.NewGuid(),
                EloPoolKey = poolKey,
                RulesetVersion = ruleset,
                CompetitionName = string.Empty,
                Status = EloRebuildRunStatus.Pending,
                QueuedAtUtc = now,
                CreatedAtUtc = now,
                Notes = "Automatically queued after the pool's final changed backfill job completed."
            })
            .ToList();
        if (runs.Count == 0)
        {
            return;
        }

        dbContext.EloRebuildRuns.AddRange(runs);
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
            await EnsureCompetitionPoolAsync(alias.Competition, configuredLeague, cancellationToken);
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
                EloPoolKey = configuredLeague?.EloPoolKey,
                CountryCode = countryCode,
                Tier = 1,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.Competitions.Add(competition);
        }

        await EnsureCompetitionPoolAsync(competition, configuredLeague, cancellationToken);

        await EnsureCompetitionAliasAsync(competition, providerLeague, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return competition;
    }

    private async Task EnsureCompetitionPoolAsync(
        Competition competition,
        ConfiguredBackfillLeague? configuredLeague,
        CancellationToken cancellationToken)
    {
        if (configuredLeague is null)
        {
            return;
        }

        var poolKey = EloPoolKeys.Normalize(configuredLeague.EloPoolKey);
        if (string.IsNullOrWhiteSpace(competition.EloPoolKey))
        {
            competition.EloPoolKey = poolKey;
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!string.Equals(competition.EloPoolKey, poolKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Competition '{competition.Name}' is assigned to ELO pool '{competition.EloPoolKey}', not '{poolKey}'.");
        }
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

    private async Task<Season> GetOrCreateSeasonAsync(
        Competition competition,
        string seasonLabel,
        bool usesSingleYearSeasonLabel,
        CancellationToken cancellationToken)
    {
        var canonicalSeasonLabel = SeasonLabelNormalizer.ToCanonicalSeasonLabel(seasonLabel, usesSingleYearSeasonLabel);
        var existing = await dbContext.Seasons
            .FirstOrDefaultAsync(
                x => x.CompetitionId == competition.Id && x.Label == canonicalSeasonLabel,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var legacyLabel = usesSingleYearSeasonLabel
            ? SeasonLabelNormalizer.ToFullSeasonLabel(canonicalSeasonLabel)
            : LegacySingleYearLabel(canonicalSeasonLabel);
        if (legacyLabel is not null)
        {
            existing = await dbContext.Seasons
                .FirstOrDefaultAsync(
                    x => x.CompetitionId == competition.Id && x.Label == legacyLabel,
                    cancellationToken);

            if (existing is not null)
            {
                var (updatedStartDate, updatedEndDate) = ParseSeasonDates(canonicalSeasonLabel, usesSingleYearSeasonLabel);
                existing.Label = canonicalSeasonLabel;
                existing.StartDateUtc = updatedStartDate;
                existing.EndDateUtc = updatedEndDate;
                await dbContext.SaveChangesAsync(cancellationToken);
                return existing;
            }
        }

        var (startDate, endDate) = ParseSeasonDates(canonicalSeasonLabel, usesSingleYearSeasonLabel);
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
        string season,
        CancellationToken cancellationToken)
    {
        sourceTeamId = NormalizeSourceTeamId(sourceTeamId, teamName);
        var usesNbaFranchiseCatalog = string.Equals(
                source,
                BasketballReferenceBasketballDataProvider.Source,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                source,
                FiveThirtyEightBasketballDataProvider.Source,
                StringComparison.OrdinalIgnoreCase);
        var franchiseMatch = usesNbaFranchiseCatalog
            ? NbaFranchiseCatalog.Resolve(sourceTeamId, teamName, SeasonLabelNormalizer.ParseStartYear(season))
            : null;
        var apiSportsCanonicalName = string.Equals(
                source,
                ApiSportsBasketballDataProvider.Source,
                StringComparison.OrdinalIgnoreCase)
            ? NbaApiSportsCatalog.GetCanonicalName(sourceTeamId)
            : null;

        var alias = await dbContext.TeamAliases
            .Include(x => x.Team)
            .FirstOrDefaultAsync(
                x => x.Source == source && x.SourceTeamId == sourceTeamId,
                cancellationToken);

        if (alias is not null)
        {
            var aliasChanged = false;

            if (franchiseMatch is not null)
            {
                if (alias.Team.CanonicalName != franchiseMatch.Franchise.CanonicalName)
                {
                    alias.Team.CanonicalName = franchiseMatch.Franchise.CanonicalName;
                    aliasChanged = true;
                }

                if (alias.Team.IsActive != franchiseMatch.Franchise.IsActive)
                {
                    alias.Team.IsActive = franchiseMatch.Franchise.IsActive;
                    aliasChanged = true;
                }

                var (validFromUtc, validToUtc) = NbaFranchiseCatalog.GetValidity(franchiseMatch.Alias);
                if (alias.ValidFromUtc != validFromUtc || alias.ValidToUtc != validToUtc)
                {
                    alias.ValidFromUtc = validFromUtc;
                    alias.ValidToUtc = validToUtc;
                    aliasChanged = true;
                }
            }

            if (apiSportsCanonicalName is not null && alias.Team.CanonicalName != apiSportsCanonicalName)
            {
                alias.Team.CanonicalName = apiSportsCanonicalName;
                aliasChanged = true;
            }

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
                var (validFromUtc, validToUtc) = franchiseMatch is null
                    ? (null, null)
                    : NbaFranchiseCatalog.GetValidity(franchiseMatch.Alias);
                dbContext.TeamAliases.Add(new TeamAlias
                {
                    Id = Guid.NewGuid(),
                    TeamId = alias.TeamId,
                    Source = source,
                    SourceTeamId = sourceTeamId,
                    AliasName = teamName,
                    ValidFromUtc = validFromUtc,
                    ValidToUtc = validToUtc,
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

        Team? team = null;
        if (franchiseMatch is not null)
        {
            var franchiseSourceIds = NbaFranchiseCatalog.GetSourceTeamIds(franchiseMatch.Franchise.Key);
            team = await dbContext.TeamAliases
                .Where(existingAlias =>
                    existingAlias.Source == source &&
                    franchiseSourceIds.Contains(existingAlias.SourceTeamId))
                .Select(existingAlias => existingAlias.Team)
                .FirstOrDefaultAsync(cancellationToken);
            team ??= await dbContext.Teams.FirstOrDefaultAsync(
                existingTeam =>
                    existingTeam.CanonicalName == franchiseMatch.Franchise.CanonicalName &&
                    existingTeam.CountryCode == countryCode,
                cancellationToken);
        }
        else if (apiSportsCanonicalName is not null)
        {
            team = await dbContext.Teams.FirstOrDefaultAsync(
                existingTeam =>
                    existingTeam.CanonicalName == apiSportsCanonicalName &&
                    existingTeam.CountryCode == countryCode,
                cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(countryCode))
        {
            var normalizedTeamName = NormalizeInternationalTeamName(teamName);
            var candidates = await dbContext.Teams
                .Where(existingTeam => existingTeam.CountryCode == countryCode)
                .ToListAsync(cancellationToken);
            team = candidates.FirstOrDefault(existingTeam =>
                NormalizeInternationalTeamName(existingTeam.CanonicalName) == normalizedTeamName);
        }

        if (team is null)
        {
            team = new Team
            {
                Id = Guid.NewGuid(),
                CanonicalName = franchiseMatch?.Franchise.CanonicalName ?? apiSportsCanonicalName ?? teamName,
                CountryCode = string.IsNullOrWhiteSpace(countryCode) ? "UNK" : countryCode,
                IsActive = franchiseMatch?.Franchise.IsActive ?? true,
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.Teams.Add(team);
        }
        else if (franchiseMatch is not null)
        {
            team.CanonicalName = franchiseMatch.Franchise.CanonicalName;
            team.IsActive = franchiseMatch.Franchise.IsActive;
            if (team.CountryCode == "UNK" && !string.IsNullOrWhiteSpace(countryCode))
            {
                team.CountryCode = countryCode;
            }
        }

        var (newAliasValidFromUtc, newAliasValidToUtc) = franchiseMatch is null
            ? (null, null)
            : NbaFranchiseCatalog.GetValidity(franchiseMatch.Alias);

        dbContext.TeamAliases.Add(new TeamAlias
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            Source = source,
            SourceTeamId = sourceTeamId,
            AliasName = teamName,
            ValidFromUtc = newAliasValidFromUtc,
            ValidToUtc = newAliasValidToUtc,
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

    private static (DateTime StartDateUtc, DateTime EndDateUtc) ParseSeasonDates(string seasonLabel, bool usesSingleYearSeasonLabel)
    {
        if (usesSingleYearSeasonLabel && int.TryParse(seasonLabel, out var singleYear))
        {
            return (
                new DateTime(singleYear, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(singleYear, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        }

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

    private static string NormalizeInternationalTeamName(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        return new string(decomposed
            .Where(character =>
                CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark &&
                char.IsLetterOrDigit(character))
            .ToArray())
            .ToUpperInvariant();
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
            "USA" => "USA",
            "United States" => "USA",
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

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed class BackfillSummary
    {
        public string Source { get; set; } = string.Empty;
        public string LeagueName { get; set; } = string.Empty;
        public string Season { get; set; } = string.Empty;
        public int RequestsUsed { get; set; }
        public bool HasMorePages { get; set; }
        public int GamesFetched { get; set; }
        public int GamesFiltered { get; set; }
        public int GamesDeduplicated { get; set; }
        public int GamesInserted { get; set; }
        public int GamesUpdated { get; set; }
        public List<string> ProviderLeagues { get; } = [];
        public List<string> SourceUrls { get; } = [];
        public List<string> SourceSeasonKeys { get; } = [];
        public List<string> ParserVersions { get; } = [];
        public List<string> FilteredGameReasons { get; } = [];
        public DateTime? SourceFetchedAtUtc { get; set; }
        public List<string> Warnings { get; } = [];
        public string? IdentityHealthStatus { get; set; }
        public int IdentityFindingsCount { get; set; }
        public int IdentityBlockersCount { get; set; }
    }
}
