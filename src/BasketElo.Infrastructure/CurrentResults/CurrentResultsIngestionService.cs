using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BasketElo.Domain.CurrentResults;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Domain.Tournaments;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BasketElo.Infrastructure.CurrentResults;

public sealed class CurrentResultsIngestionService(
    BasketEloDbContext dbContext,
    ICurrentResultsProvider provider,
    IBackfillCatalog backfillCatalog,
    IIdentityHealthCheckService identityHealthCheckService,
    TimeProvider timeProvider,
    ILogger<CurrentResultsIngestionService> logger) : ICurrentResultsIngestionService
{
    private static readonly TimeSpan CrossSourceReconciliationWindow = TimeSpan.FromHours(36);

    public async Task<CurrentResultsRunSummary> RunAsync(
        DateOnly fromDate,
        DateOnly toDate,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (toDate < fromDate)
        {
            throw new ArgumentException("The current-results end date must not be before the start date.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var run = new CurrentResultsRun
        {
            Id = Guid.NewGuid(),
            Provider = provider.Source,
            FromDate = fromDate,
            ToDate = toDate,
            Status = "running",
            StartedAtUtc = now,
            CreatedAtUtc = now
        };
        dbContext.CurrentResultsRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);

        var changedPools = new HashSet<string>(StringComparer.Ordinal);
        var deferredPools = new List<string>();
        try
        {
            for (var date = fromDate; date <= toDate; date = date.AddDays(1))
            {
                var fetched = await provider.FetchAsync(date, cancellationToken);
                run.PagesRead++;
                run.CandidatesRead += fetched.Candidates.Count;

                if (dryRun)
                {
                    continue;
                }

                foreach (var candidate in fetched.Candidates)
                {
                    var outcome = await UpsertCandidateAsync(candidate, run, cancellationToken);
                    if (outcome.EloChanged && outcome.EloPoolKey is not null)
                    {
                        changedPools.Add(outcome.EloPoolKey);
                    }

                    run.GamesUpserted += outcome.GameChanged ? 1 : 0;
                    run.ReviewsOpened += outcome.ReviewOpened ? 1 : 0;
                    run.UnsupportedSkipped += outcome.UnsupportedSkipped ? 1 : 0;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
            }

            if (!dryRun)
            {
                foreach (var poolKey in changedPools)
                {
                    await identityHealthCheckService.InvalidateChangedScopeAsync(new IdentityChangedScope
                    {
                        EloPoolKey = poolKey,
                        Source = provider.Source
                    }, cancellationToken);

                    var health = await identityHealthCheckService.RunAsync(new IdentityHealthCheckRequest
                    {
                        EloPoolKey = poolKey,
                        Source = provider.Source,
                        Force = true
                    }, cancellationToken);
                    if (health.Status == IdentityHealthCheckStatus.Blockers)
                    {
                        deferredPools.Add(poolKey);
                        continue;
                    }

                    run.EloPoolsQueued += await QueueEloRunsAsync(poolKey, cancellationToken);
                }
            }

            run.Status = dryRun ? "dry_run" : "completed";
            run.DeferredEloPoolsJson = deferredPools.Count == 0 ? null : JsonSerializer.Serialize(deferredPools);
            run.FinishedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await dbContext.SaveChangesAsync(cancellationToken);
            return ToSummary(run, deferredPools);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Current-results ingestion failed for {fromDate} through {toDate}.", fromDate, toDate);
            run.Status = "failed";
            run.ErrorMessage = exception.Message;
            run.FinishedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<CurrentResultReviewResolutionDto> ResolveReviewAsync(
        Guid reviewId,
        CurrentResultReviewResolutionRequest request,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.CurrentResultReviews
            .SingleOrDefaultAsync(x => x.Id == reviewId, cancellationToken)
            ?? throw new KeyNotFoundException($"Current-results review {reviewId} was not found.");
        var action = request.Action.Trim().ToLowerInvariant();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (action == "ignore")
        {
            review.Status = CurrentResultReviewStatuses.Ignored;
            review.ResolutionAction = action;
            review.ResolutionNote = request.Note;
            review.ResolvedAtUtc = now;
            review.UpdatedAtUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new CurrentResultReviewResolutionDto(review.Id, review.Status, review.AssignedGameId, 0, "Review ignored.");
        }

        if (action != "assign" || !request.GameId.HasValue)
        {
            throw new ArgumentException("Use action 'assign' with a scheduled game ID, or action 'ignore'.", nameof(request));
        }

        var game = await dbContext.Games
            .Include(x => x.Competition)
            .SingleOrDefaultAsync(x => x.Id == request.GameId.Value, cancellationToken)
            ?? throw new KeyNotFoundException($"Game {request.GameId.Value} was not found.");
        if (game.Status != CurrentResultStatuses.Scheduled)
        {
            throw new InvalidOperationException("Only scheduled games can receive a manually assigned current result.");
        }
        if (game.HasManualResultOverride)
        {
            throw new InvalidOperationException("The selected game has a manual result override and cannot be changed by current-results review.");
        }
        if (review.Reason == CurrentResultReviewReasons.TournamentCycleConfirmationRequired && game.TournamentCycleId is null)
        {
            throw new InvalidOperationException("Confirm the tournament cycle before assigning this result to Elo.");
        }

        var resultChanged = game.HomeScore != review.HomeScore || game.AwayScore != review.AwayScore || game.Status != review.ResultStatus;
        game.HomeScore = review.HomeScore;
        game.AwayScore = review.AwayScore;
        game.Status = review.ResultStatus;
        var cyclePendingConfirmation = review.Reason == CurrentResultReviewReasons.TournamentCycleConfirmationRequired && game.TournamentCycleId is null;
        game.EloEligible = !cyclePendingConfirmation && review.ResultStatus == CurrentResultStatuses.Finished && review.HomeScore.HasValue && review.AwayScore.HasValue;
        game.EloExclusionReason = game.EloEligible
            ? null
            : cyclePendingConfirmation
                ? CurrentResultReviewReasons.TournamentCycleConfirmationRequired
                : review.ResultStatus == CurrentResultStatuses.Scheduled ? null : "current_result_not_final";
        game.UpdatedAtUtc = now;

        review.Status = CurrentResultReviewStatuses.Resolved;
        review.AssignedGameId = game.Id;
        review.ResolutionAction = action;
        review.ResolutionNote = request.Note;
        review.ResolvedAtUtc = now;

        var eloRunsQueued = 0;
        if (resultChanged && game.EloEligible && !string.IsNullOrWhiteSpace(game.Competition.EloPoolKey))
        {
            eloRunsQueued = await QueueEloRunsAsync(game.Competition.EloPoolKey!, cancellationToken);
        }

        review.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CurrentResultReviewResolutionDto(review.Id, review.Status, review.AssignedGameId, eloRunsQueued, "Result assigned to the planned game.");
    }

    public async Task<IReadOnlyList<CurrentResultsUnmatchedCompetitionDto>> GetUnmatchedCompetitionsAsync(
        CancellationToken cancellationToken)
    {
        var reviews = await dbContext.CurrentResultReviews
            .AsNoTracking()
            .Where(x => x.Status == CurrentResultReviewStatuses.Open &&
                        (x.Reason == CurrentResultReviewReasons.UnknownCompetition ||
                         x.Reason == CurrentResultReviewReasons.AmbiguousCompetition) &&
                        x.ResolutionAction != "merge")
            .ToListAsync(cancellationToken);

        return reviews
            .GroupBy(x => new
            {
                Source = x.Source.ToLowerInvariant(),
                SourceCompetitionId = (x.SourceCompetitionId ?? string.Empty).ToLowerInvariant(),
                CountryName = x.CountryName.Trim().ToLowerInvariant(),
                CompetitionName = x.CompetitionName.Trim().ToLowerInvariant()
            })
            .Select(group => new CurrentResultsUnmatchedCompetitionDto(
                group.First().Source,
                string.IsNullOrWhiteSpace(group.First().SourceCompetitionId) ? null : group.First().SourceCompetitionId,
                group.First().CountryName,
                group.First().CompetitionName,
                group.Count(),
                group.Min(x => x.CreatedAtUtc),
                group.Max(x => x.UpdatedAtUtc)))
            .OrderByDescending(x => x.LastSeenUtc)
            .ThenBy(x => x.CompetitionName)
            .ToList();
    }

    public async Task<int> MergeUnmatchedCompetitionAsync(
        MergeUnmatchedCompetitionRequest request,
        CancellationToken cancellationToken)
    {
        var source = RequiredValue(request.Source, "Source", 50);
        var competitionName = RequiredValue(request.CompetitionName, "Competition name", 200);
        var countryName = RequiredValue(request.CountryName, "Country", 100);
        var target = await ResolveMergeCompetitionAsync(request, countryName, cancellationToken);
        if (target.SupportPolicy != CompetitionSupportPolicies.Supported)
        {
            throw new InvalidOperationException("Only a supported competition can receive a current-results alias.");
        }

        await AddCompetitionAliasAsync(target, source, request.SourceCompetitionId, competitionName, cancellationToken);
        var tournamentCycle = await ResolveMergeTournamentCycleAsync(request, target.Name, countryName, cancellationToken);
        var reviews = await FindUnmatchedReviewsAsync(source, request.SourceCompetitionId, countryName, competitionName, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var review in reviews)
        {
            review.SuggestedCompetitionName = target.Name;
            review.SuggestedCompetitionCountryCode = target.CountryCode;
            if (tournamentCycle is not null)
            {
                review.TournamentCycleId = tournamentCycle.Id;
            }
            review.ResolutionAction = "merge";
            review.ResolutionNote = tournamentCycle is null
                ? $"Merged into {target.Name}; a unique planned fixture will be assigned automatically, otherwise choose a planned match or rerun current-results."
                : $"Merged into {target.Name} and assigned to {tournamentCycle.DisplayName}; a unique planned fixture will be assigned automatically, otherwise choose a planned match or rerun current-results.";
            review.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await AutoAssignMergedReviewsAsync(target, reviews, tournamentCycle, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return reviews.Count;
    }

    private async Task<int> AutoAssignMergedReviewsAsync(
        Competition target,
        IReadOnlyCollection<CurrentResultReview> reviews,
        TournamentCycle? tournamentCycle,
        CancellationToken cancellationToken)
    {
        var changedPools = new HashSet<string>(StringComparer.Ordinal);
        var assigned = 0;

        foreach (var review in reviews)
        {
            var home = await ResolveTeamAsync(
                review.HomeTeamName,
                review.HomeTeamSourceId,
                target.CountryCode,
                cancellationToken);
            var away = await ResolveTeamAsync(
                review.AwayTeamName,
                review.AwayTeamSourceId,
                target.CountryCode,
                cancellationToken);
            if (home.Team is null || away.Team is null)
            {
                continue;
            }

            var plannedFixtureMatch = await FindScheduledFixtureAsync(
                target.Id,
                home.Team.Id,
                away.Team.Id,
                review.GameDateTimeUtc,
                review.SourceGameId,
                cancellationToken);
            if (plannedFixtureMatch.Ambiguous || plannedFixtureMatch.Game is null)
            {
                continue;
            }

            var game = plannedFixtureMatch.Game;
            if (game.HasManualResultOverride)
            {
                continue;
            }

            var resultChanged = game.HomeScore != review.HomeScore ||
                                game.AwayScore != review.AwayScore ||
                                game.Status != review.ResultStatus;
            game.HomeScore = review.HomeScore;
            game.AwayScore = review.AwayScore;
            game.Status = review.ResultStatus;
            game.EloEligible = review.ResultStatus == CurrentResultStatuses.Finished &&
                               review.HomeScore.HasValue &&
                               review.AwayScore.HasValue;
            game.EloExclusionReason = game.EloEligible
                ? null
                : review.ResultStatus == CurrentResultStatuses.Scheduled ? null : "current_result_not_final";
            if (tournamentCycle is not null)
            {
                game.TournamentCycleId = tournamentCycle.Id;
            }

            game.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            review.Status = CurrentResultReviewStatuses.Resolved;
            review.AssignedGameId = game.Id;
            review.ResolutionAction = "merge_auto_assign";
            review.ResolutionNote = tournamentCycle is null
                ? $"Merged into {target.Name} and auto-assigned to planned fixture {game.Source}:{game.SourceGameId}."
                : $"Merged into {target.Name}, assigned to {tournamentCycle.DisplayName}, and auto-assigned to planned fixture {game.Source}:{game.SourceGameId}.";
            review.ResolvedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            review.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            assigned++;

            if (resultChanged && game.EloEligible && !string.IsNullOrWhiteSpace(target.EloPoolKey))
            {
                changedPools.Add(target.EloPoolKey!);
            }
        }

        foreach (var poolKey in changedPools)
        {
            await QueueEloRunsAsync(poolKey, cancellationToken);
        }

        return assigned;
    }

    private async Task<Competition> ResolveMergeCompetitionAsync(
        MergeUnmatchedCompetitionRequest request,
        string countryName,
        CancellationToken cancellationToken)
    {
        if (request.TargetCompetitionId is Guid targetCompetitionId)
        {
            if (request.NewCompetition is not null)
            {
                throw new ArgumentException("Choose an existing competition or create a new one, not both.");
            }

            return await dbContext.Competitions
                .SingleOrDefaultAsync(x => x.Id == targetCompetitionId && x.IsActive, cancellationToken)
                ?? throw new KeyNotFoundException("Target competition was not found.");
        }

        var create = request.NewCompetition
            ?? throw new ArgumentException("Choose an existing competition or provide a new competition definition.");
        var name = RequiredValue(create.Name, "New competition name", 200);
        var type = RequiredValue(create.Type, "New competition type", 50);
        var supportPolicy = RequiredValue(create.SupportPolicy, "New competition support policy", 30).ToLowerInvariant();
        if (!CompetitionSupportPolicies.IsValid(supportPolicy))
        {
            throw new ArgumentException("New competition support policy is invalid.");
        }

        var countryCode = CountryCodeCatalog.Normalize(create.CountryCode);
        if (string.IsNullOrWhiteSpace(countryCode) && !NormalizeName(countryName).Equals("world", StringComparison.Ordinal))
        {
            countryCode = CountryCode(countryName);
        }

        if (await dbContext.Competitions.AnyAsync(x => x.Name == name && x.CountryCode == countryCode, cancellationToken))
        {
            throw new InvalidOperationException("A competition with this name and country already exists; choose it from the existing competitions.");
        }

        var target = new Competition
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = type,
            CountryCode = countryCode,
            EloPoolKey = string.IsNullOrWhiteSpace(create.EloPoolKey) ? null : create.EloPoolKey.Trim(),
            Tier = Math.Max(0, create.Tier),
            IsActive = true,
            SupportPolicy = supportPolicy,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };
        dbContext.Competitions.Add(target);
        return target;
    }

    private async Task<TournamentCycle?> ResolveMergeTournamentCycleAsync(
        MergeUnmatchedCompetitionRequest request,
        string competitionName,
        string countryName,
        CancellationToken cancellationToken)
    {
        if (request.TournamentCycleId is Guid tournamentCycleId)
        {
            if (!string.IsNullOrWhiteSpace(request.TournamentCycleFamily) ||
                !string.IsNullOrWhiteSpace(request.TournamentCycleEditionLabel))
            {
                throw new ArgumentException("Choose an existing tournament cycle or create a new one, not both.");
            }

            var existingCycle = await dbContext.TournamentCycles
                .SingleOrDefaultAsync(x => x.Id == tournamentCycleId, cancellationToken)
                ?? throw new KeyNotFoundException("Tournament cycle was not found.");
            ValidateCycleFamily(competitionName, countryName, existingCycle);
            return existingCycle;
        }

        var hasFamily = !string.IsNullOrWhiteSpace(request.TournamentCycleFamily);
        var hasEdition = !string.IsNullOrWhiteSpace(request.TournamentCycleEditionLabel);
        if (!hasFamily && !hasEdition)
        {
            return null;
        }

        if (!hasFamily || !hasEdition)
        {
            throw new ArgumentException("Both tournament cycle family and edition are required when creating a cycle.");
        }

        var family = TournamentCycleCatalog.SupportedFamilies.FirstOrDefault(
            value => string.Equals(value, request.TournamentCycleFamily!.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Tournament cycle family is not supported.");
        var editionLabel = request.TournamentCycleEditionLabel!.Trim();

        var key = TournamentCycleCatalog.ResolveKeyFromFamily(family, editionLabel)
            ?? throw new ArgumentException("Tournament cycle family and edition could not be converted to a cycle key.");
        var existing = await dbContext.TournamentCycles
            .SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (existing is not null)
        {
            ValidateCycleFamily(competitionName, countryName, existing);
            return existing;
        }

        var cycle = new TournamentCycle
        {
            Id = Guid.NewGuid(),
            Key = key,
            Family = family,
            EditionLabel = editionLabel,
            DisplayName = TournamentCycleCatalog.DisplayName(family, editionLabel),
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };
        ValidateCycleFamily(competitionName, countryName, cycle);
        dbContext.TournamentCycles.Add(cycle);
        return cycle;
    }

    private static void ValidateCycleFamily(string competitionName, string countryName, TournamentCycle cycle)
    {
        var expectedKey = TournamentCycleCatalog.ResolveKey(countryName, competitionName, cycle.EditionLabel);
        if (expectedKey is null)
        {
            return;
        }

        var expectedSeparator = expectedKey.IndexOf('-');
        var actualSeparator = cycle.Key.IndexOf('-');
        if (expectedSeparator <= 0 || actualSeparator <= 0)
        {
            return;
        }

        var expectedFamilyPrefix = expectedKey[..expectedSeparator];
        var actualFamilyPrefix = cycle.Key[..actualSeparator];
        if (!string.Equals(expectedFamilyPrefix, actualFamilyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Competition '{competitionName}' belongs to the '{expectedFamilyPrefix}' cycle family, not '{actualFamilyPrefix}'.");
        }
    }

    public async Task<int> IgnoreUnmatchedCompetitionAsync(
        IgnoreUnmatchedCompetitionRequest request,
        CancellationToken cancellationToken)
    {
        var source = RequiredValue(request.Source, "Source", 50);
        var competitionName = RequiredValue(request.CompetitionName, "Competition name", 200);
        var countryName = RequiredValue(request.CountryName, "Country", 100);
        var countryCode = CountryCode(countryName);
        var competition = await dbContext.Competitions
            .SingleOrDefaultAsync(x => x.IsActive && x.Name == competitionName && x.CountryCode == countryCode, cancellationToken);
        if (competition is null)
        {
            competition = new Competition
            {
                Id = Guid.NewGuid(),
                Name = competitionName,
                Type = "current-results",
                CountryCode = countryCode,
                SupportPolicy = CompetitionSupportPolicies.Unsupported,
                IsActive = false,
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            };
            dbContext.Competitions.Add(competition);
        }
        else
        {
            competition.SupportPolicy = CompetitionSupportPolicies.Unsupported;
            competition.IsActive = false;
        }

        await AddCompetitionAliasAsync(competition, source, request.SourceCompetitionId, competitionName, cancellationToken);
        var reviews = await FindUnmatchedReviewsAsync(source, request.SourceCompetitionId, countryName, competitionName, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var review in reviews)
        {
            review.Status = CurrentResultReviewStatuses.Ignored;
            review.ResolutionAction = "ignore";
            review.ResolutionNote = $"Competition marked unsupported: {competitionName}.";
            review.ResolvedAtUtc = now;
            review.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return reviews.Count;
    }

    private async Task<List<CurrentResultReview>> FindUnmatchedReviewsAsync(
        string source,
        string? sourceCompetitionId,
        string countryName,
        string competitionName,
        CancellationToken cancellationToken) =>
        await dbContext.CurrentResultReviews
            .Where(x => x.Status == CurrentResultReviewStatuses.Open &&
                        (x.Reason == CurrentResultReviewReasons.UnknownCompetition ||
                         x.Reason == CurrentResultReviewReasons.AmbiguousCompetition) &&
                        x.Source == source &&
                        (string.IsNullOrWhiteSpace(sourceCompetitionId)
                            ? string.IsNullOrWhiteSpace(x.SourceCompetitionId)
                            : x.SourceCompetitionId == sourceCompetitionId) &&
                        x.CountryName == countryName &&
                        x.CompetitionName == competitionName)
            .ToListAsync(cancellationToken);

    private async Task AddCompetitionAliasAsync(
        Competition target,
        string source,
        string? sourceCompetitionId,
        string aliasName,
        CancellationToken cancellationToken)
    {
        var normalizedAlias = NormalizeName(aliasName);
        var aliases = await dbContext.CompetitionAliases
            .Where(x => x.Source == source)
            .ToListAsync(cancellationToken);
        aliases = aliases.Where(x =>
            x.CompetitionId == target.Id ||
            (!string.IsNullOrWhiteSpace(sourceCompetitionId) && x.SourceCompetitionId == sourceCompetitionId) ||
            NormalizeName(x.AliasName) == normalizedAlias).ToList();
        var conflicting = aliases.FirstOrDefault(x => x.CompetitionId != target.Id);
        if (conflicting is not null)
        {
            throw new InvalidOperationException("This source competition alias is already mapped to another canonical competition.");
        }

        if (!aliases.Any(x => x.CompetitionId == target.Id &&
                              string.Equals(x.SourceCompetitionId ?? string.Empty, sourceCompetitionId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                              NormalizeName(x.AliasName) == normalizedAlias))
        {
            dbContext.CompetitionAliases.Add(new CompetitionAlias
            {
                Id = Guid.NewGuid(),
                CompetitionId = target.Id,
                Source = source,
                SourceCompetitionId = sourceCompetitionId?.Trim() ?? string.Empty,
                AliasName = aliasName,
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            });
        }
    }

    private static string RequiredValue(string? value, string label, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) throw new ArgumentException($"{label} is required.");
        if (trimmed.Length > maxLength) throw new ArgumentException($"{label} cannot exceed {maxLength} characters.");
        return trimmed;
    }

    private async Task<UpsertOutcome> UpsertCandidateAsync(
        CurrentResultCandidate candidate,
        CurrentResultsRun run,
        CancellationToken cancellationToken)
    {
        var mapping = await ResolveCompetitionAsync(candidate, cancellationToken);
        if (mapping.Competition is null)
        {
            await UpsertReviewAsync(candidate, run, mapping.Reason ?? CurrentResultReviewReasons.UnknownCompetition, mapping, cancellationToken, reopenIgnored: true);
            return new UpsertOutcome(false, true, false, false, null);
        }

        if (!mapping.Competition.IsActive || mapping.Competition.SupportPolicy == CompetitionSupportPolicies.Unsupported)
        {
            return new UpsertOutcome(false, false, false, true, null);
        }

        var home = await ResolveTeamAsync(candidate.HomeTeamName, candidate.HomeTeamSourceId, mapping.Competition.CountryCode, cancellationToken);
        var away = await ResolveTeamAsync(candidate.AwayTeamName, candidate.AwayTeamSourceId, mapping.Competition.CountryCode, cancellationToken);
        var reasons = new List<string>();
        if (mapping.Competition is null) reasons.Add(mapping.Reason ?? CurrentResultReviewReasons.UnsupportedCompetition);
        if (home.Team is null) reasons.Add(home.Ambiguous ? CurrentResultReviewReasons.AmbiguousHomeTeam : CurrentResultReviewReasons.UnresolvedHomeTeam);
        if (away.Team is null) reasons.Add(away.Ambiguous ? CurrentResultReviewReasons.AmbiguousAwayTeam : CurrentResultReviewReasons.UnresolvedAwayTeam);
        if (candidate.Status == CurrentResultStatuses.Finished && (!candidate.HomeScore.HasValue || !candidate.AwayScore.HasValue)) reasons.Add(CurrentResultReviewReasons.InvalidResult);

        if (reasons.Count > 0 || mapping.Competition is null || home.Team is null || away.Team is null)
        {
            await UpsertReviewAsync(candidate, run, string.Join(',', reasons.Distinct(StringComparer.Ordinal)), mapping, cancellationToken);
            return new UpsertOutcome(false, true, false, false, mapping.Competition?.EloPoolKey);
        }

        var season = await GetOrCreateSeasonAsync(mapping.Competition, candidate.GameDateTimeUtc, cancellationToken);
        var review = await dbContext.CurrentResultReviews
            .SingleOrDefaultAsync(x => x.Source == provider.Source && x.SourceGameId == candidate.SourceGameId, cancellationToken);
        if (review?.Status == CurrentResultReviewStatuses.Ignored)
        {
            return new UpsertOutcome(false, false, false, false, null);
        }

        var existing = await dbContext.Games
            .SingleOrDefaultAsync(x => x.Source == provider.Source && x.SourceGameId == candidate.SourceGameId, cancellationToken);
        var reconciledAcrossSources = false;
        if (existing is null)
        {
            if (review?.AssignedGameId is Guid assignedGameId)
            {
                existing = await dbContext.Games
                    .SingleOrDefaultAsync(x => x.Id == assignedGameId, cancellationToken);
                reconciledAcrossSources = existing is not null;
            }
        }

        if (existing is null)
        {
            var plannedFixtureMatch = await FindScheduledFixtureAsync(
                mapping.Competition.Id,
                home.Team.Id,
                away.Team.Id,
                candidate.GameDateTimeUtc,
                candidate.SourceGameId,
                cancellationToken);
            if (plannedFixtureMatch.Ambiguous)
            {
                await UpsertReviewAsync(candidate, run, CurrentResultReviewReasons.AmbiguousPlannedFixture, mapping, cancellationToken);
                return new UpsertOutcome(false, true, false, false, mapping.Competition.EloPoolKey);
            }

            existing = plannedFixtureMatch.Game;
            reconciledAcrossSources = existing is not null;
        }

        var tournamentCycle = await ResolveConfirmedTournamentCycleAsync(
            candidate.CountryName,
            mapping.Competition.Name,
            season.Label,
            candidate.GameDateTimeUtc,
            existing,
            review,
            cancellationToken);
        var tournamentCyclePendingConfirmation = tournamentCycle is null &&
            TournamentCycleCatalog.ResolveKey(candidate.CountryName, mapping.Competition.Name, season.Label) is not null;
        if (tournamentCyclePendingConfirmation)
        {
            await UpsertReviewAsync(candidate, run, CurrentResultReviewReasons.TournamentCycleConfirmationRequired, mapping, cancellationToken);
        }

        var changed = existing is null;
        var game = existing ?? new Game
        {
            Id = Guid.NewGuid(),
            Source = provider.Source,
            SourceGameId = candidate.SourceGameId,
            IngestedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };

        if (existing is null)
        {
            dbContext.Games.Add(game);
        }

        var eloChanged = !tournamentCyclePendingConfirmation && existing is null && candidate.Status == CurrentResultStatuses.Finished && candidate.HomeScore.HasValue && candidate.AwayScore.HasValue;
        if (!game.HasManualResultOverride)
        {
            var resultChanged = game.HomeScore != candidate.HomeScore || game.AwayScore != candidate.AwayScore || game.Status != candidate.Status;
            changed |= resultChanged || game.GameDateTimeUtc != candidate.GameDateTimeUtc;
            eloChanged |= !tournamentCyclePendingConfirmation && resultChanged && (game.Status == CurrentResultStatuses.Finished || candidate.Status == CurrentResultStatuses.Finished);
            game.HomeScore = candidate.HomeScore;
            game.AwayScore = candidate.AwayScore;
            game.Status = candidate.Status;
            game.EloEligible = !tournamentCyclePendingConfirmation && candidate.Status == CurrentResultStatuses.Finished && candidate.HomeScore.HasValue && candidate.AwayScore.HasValue;
            game.EloExclusionReason = game.EloEligible
                ? null
                : tournamentCyclePendingConfirmation
                    ? CurrentResultReviewReasons.TournamentCycleConfirmationRequired
                    : candidate.Status == CurrentResultStatuses.Scheduled ? null : "current_result_not_final";
        }

        if (!reconciledAcrossSources)
        {
            game.SourceUrl = candidate.SourceUrl;
            game.SourceFetchedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            game.SourceRevision = candidate.SourceRevision;
            game.ParserVersion = candidate.ParserVersion;
        }

        if (!reconciledAcrossSources)
        {
            game.SourceSeasonKey = season.Label;
        }

        game.CompetitionId = mapping.Competition.Id;
        game.SeasonId = season.Id;
        if (tournamentCycle is not null)
        {
            game.TournamentCycleId = tournamentCycle.Id;
        }
        game.GameDateTimeUtc = candidate.GameDateTimeUtc;
        game.HomeTeamId = home.Team.Id;
        game.AwayTeamId = away.Team.Id;
        game.CompetitionPhase = candidate.StageName;
        game.CompetitionRound = candidate.StageName;
        game.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        if (review is not null && review.Status == CurrentResultReviewStatuses.Open && !tournamentCyclePendingConfirmation)
        {
            review.Status = CurrentResultReviewStatuses.Resolved;
            review.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        }

        return new UpsertOutcome(changed, tournamentCyclePendingConfirmation, eloChanged, false, tournamentCyclePendingConfirmation ? null : mapping.Competition.EloPoolKey);
    }

    private async Task<TournamentCycle?> ResolveConfirmedTournamentCycleAsync(
        string country,
        string competitionName,
        string seasonLabel,
        DateTime gameDateTimeUtc,
        Game? existing,
        CurrentResultReview? review,
        CancellationToken cancellationToken)
    {
        if (review?.TournamentCycleId is Guid assignedTournamentCycleId)
        {
            return await dbContext.TournamentCycles
                .SingleOrDefaultAsync(x => x.Id == assignedTournamentCycleId, cancellationToken);
        }

        if (existing?.TournamentCycleId is Guid existingTournamentCycleId)
        {
            return await dbContext.TournamentCycles
                .SingleOrDefaultAsync(x => x.Id == existingTournamentCycleId, cancellationToken);
        }

        var key = TournamentCycleCatalog.ResolveKey(country, competitionName, seasonLabel);
        if (key is null)
        {
            return null;
        }

        var exact = await dbContext.TournamentCycles
            .SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (exact is not null)
        {
            return exact;
        }

        var separator = key.IndexOf('-');
        if (separator <= 0)
        {
            return null;
        }

        var familyPrefix = key[..separator];
        var editionLabel = gameDateTimeUtc.Year.ToString(CultureInfo.InvariantCulture);
        return await dbContext.TournamentCycles
            .Where(x => x.Key.StartsWith(familyPrefix + "-") && x.EditionLabel == editionLabel)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<PlannedFixtureMatch> FindScheduledFixtureAsync(
        Guid competitionId,
        Guid homeTeamId,
        Guid awayTeamId,
        DateTime gameDateTimeUtc,
        string sourceGameId,
        CancellationToken cancellationToken)
    {
        var minimumDateTimeUtc = gameDateTimeUtc - CrossSourceReconciliationWindow;
        var maximumDateTimeUtc = gameDateTimeUtc + CrossSourceReconciliationWindow;
        var candidates = await dbContext.Games
            .Where(x =>
                x.Source != provider.Source &&
                x.CompetitionId == competitionId &&
                x.HomeTeamId == homeTeamId &&
                x.AwayTeamId == awayTeamId &&
                x.Status == CurrentResultStatuses.Scheduled &&
                x.GameDateTimeUtc >= minimumDateTimeUtc &&
                x.GameDateTimeUtc <= maximumDateTimeUtc)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return new PlannedFixtureMatch(null, false);
        }

        var ordered = candidates
            .OrderBy(x => Math.Abs((x.GameDateTimeUtc - gameDateTimeUtc).TotalSeconds))
            .ToList();
        if (ordered.Count > 1)
        {
            var closestDistance = Math.Abs((ordered[0].GameDateTimeUtc - gameDateTimeUtc).TotalSeconds);
            var secondClosestDistance = Math.Abs((ordered[1].GameDateTimeUtc - gameDateTimeUtc).TotalSeconds);
            if (closestDistance == secondClosestDistance)
            {
                return new PlannedFixtureMatch(null, true);
            }
        }

        logger.LogDebug(
            "Reconciled current result {Provider}:{SourceGameId} with planned {PlannedSource}:{PlannedSourceGameId}.",
            provider.Source,
            sourceGameId,
            ordered[0].Source,
            ordered[0].SourceGameId);
        return new PlannedFixtureMatch(ordered[0], false);
    }

    private async Task UpsertReviewAsync(
        CurrentResultCandidate candidate,
        CurrentResultsRun run,
        string reason,
        CompetitionMapping mapping,
        CancellationToken cancellationToken,
        bool reopenIgnored = false)
    {
        var review = await dbContext.CurrentResultReviews
            .SingleOrDefaultAsync(x => x.Source == provider.Source && x.SourceGameId == candidate.SourceGameId, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (review is null)
        {
            review = new CurrentResultReview
            {
                Id = Guid.NewGuid(),
                Source = provider.Source,
                SourceGameId = candidate.SourceGameId,
                CreatedAtUtc = now
            };
            dbContext.CurrentResultReviews.Add(review);
        }

        review.RunId = run.Id;
        review.SourceUrl = candidate.SourceUrl;
        review.SourceCompetitionId = candidate.SourceCompetitionId;
        review.SourceDate = candidate.SourceDate;
        review.GameDateTimeUtc = candidate.GameDateTimeUtc;
        review.CountryName = candidate.CountryName;
        review.CompetitionName = candidate.CompetitionName;
        review.StageName = candidate.StageName;
        review.HomeTeamName = candidate.HomeTeamName;
        review.AwayTeamName = candidate.AwayTeamName;
        review.HomeTeamSourceId = candidate.HomeTeamSourceId;
        review.AwayTeamSourceId = candidate.AwayTeamSourceId;
        review.HomeScore = candidate.HomeScore;
        review.AwayScore = candidate.AwayScore;
        review.ResultStatus = candidate.Status;
        review.Reason = reason;
        if (reopenIgnored)
        {
            review.ResolutionAction = null;
            review.ResolutionNote = null;
            review.ResolvedAtUtc = null;
        }
        if (review.ResolutionAction == "merge")
        {
            review.ResolutionAction = null;
            review.ResolutionNote = null;
        }
        review.Status = !reopenIgnored && (review.Status is CurrentResultReviewStatuses.Resolved or CurrentResultReviewStatuses.Ignored)
            ? review.Status
            : CurrentResultReviewStatuses.Open;
        review.SuggestedCompetitionName = mapping.SuggestedName;
        review.SuggestedCompetitionCountryCode = mapping.SuggestedCountryCode;
        review.ParserVersion = candidate.ParserVersion;
        review.SourceRevision = candidate.SourceRevision;
        review.UpdatedAtUtc = now;
    }

    private async Task<CompetitionMapping> ResolveCompetitionAsync(CurrentResultCandidate candidate, CancellationToken cancellationToken)
    {
        var countryCode = CountryCode(candidate.CountryName);
        var desired = backfillCatalog.GetLeagues()
            .Where(x => CatalogCountryMatches(x.Country, candidate.CountryName))
            .Select(x => x.LeagueName)
            .Where(x => CompetitionNamesMatch(x, candidate.CompetitionName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SingleOrDefault();
        desired ??= SupportedCompetitionName(candidate.CountryName, candidate.CompetitionName);
        var competitions = await dbContext.Competitions
            .Include(x => x.Aliases)
            .ToListAsync(cancellationToken);

        var normalizedObservedName = NormalizeName(candidate.CompetitionName);
        var matches = competitions.Where(x =>
            (desired is not null && string.Equals(x.Name, desired, StringComparison.OrdinalIgnoreCase) && CountryMatches(x.CountryCode, countryCode)) ||
            (NormalizeName(x.Name) == normalizedObservedName && CountryMatches(x.CountryCode, countryCode)) ||
            x.Aliases.Any(alias =>
                alias.Source == provider.Source &&
                ((!string.IsNullOrWhiteSpace(candidate.SourceCompetitionId) &&
                  !string.IsNullOrWhiteSpace(alias.SourceCompetitionId) &&
                  string.Equals(alias.SourceCompetitionId, candidate.SourceCompetitionId, StringComparison.OrdinalIgnoreCase)) ||
                 NormalizeName(alias.AliasName) == normalizedObservedName))).ToList();

        if (matches.Count == 1)
        {
            return new CompetitionMapping(matches[0], null, matches[0].Name, matches[0].CountryCode);
        }

        return new CompetitionMapping(
            null,
            matches.Count > 1 ? CurrentResultReviewReasons.AmbiguousCompetition : CurrentResultReviewReasons.UnknownCompetition,
            desired,
            countryCode);
    }

    private async Task<TeamResolution> ResolveTeamAsync(string name, string sourceId, string? countryCode, CancellationToken cancellationToken)
    {
        var aliasMatches = await dbContext.TeamAliases
            .Include(x => x.Team)
            .Where(x => x.Source == provider.Source && x.SourceTeamId == sourceId)
            .Select(x => x.Team)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (aliasMatches.Count == 1) return new TeamResolution(aliasMatches[0], false);
        if (aliasMatches.Count > 1) return new TeamResolution(null, true);

        var normalized = NormalizeName(name);
        var teamsQuery = dbContext.Teams.Include(x => x.Aliases).AsQueryable();
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            teamsQuery = teamsQuery.Where(x => x.CountryCode == countryCode || x.CountryCode == "UNK");
        }

        var exactMatches = (await teamsQuery.ToListAsync(cancellationToken))
            .Where(x => NormalizeName(x.CanonicalName) == normalized || x.Aliases.Any(alias => NormalizeName(alias.AliasName) == normalized))
            .Where(x => string.IsNullOrWhiteSpace(countryCode) || CountryMatches(x.CountryCode, countryCode) || x.CountryCode == "UNK")
            .ToList();

        if (exactMatches.Count == 1)
        {
            if (!await dbContext.TeamAliases.AnyAsync(x => x.Source == provider.Source && x.SourceTeamId == sourceId, cancellationToken))
            {
                dbContext.TeamAliases.Add(new TeamAlias
                {
                    Id = Guid.NewGuid(),
                    TeamId = exactMatches[0].Id,
                    Source = provider.Source,
                    SourceTeamId = sourceId,
                    AliasName = name,
                    CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
                });
            }

            return new TeamResolution(exactMatches[0], false);
        }

        return new TeamResolution(null, exactMatches.Count > 1);
    }

    private async Task<Season> GetOrCreateSeasonAsync(Competition competition, DateTime gameDateTimeUtc, CancellationToken cancellationToken)
    {
        var existing = dbContext.Seasons.Local
            .FirstOrDefault(x => x.CompetitionId == competition.Id && x.StartDateUtc <= gameDateTimeUtc && x.EndDateUtc >= gameDateTimeUtc);
        existing ??= await dbContext.Seasons
            .FirstOrDefaultAsync(x => x.CompetitionId == competition.Id && x.StartDateUtc <= gameDateTimeUtc && x.EndDateUtc >= gameDateTimeUtc, cancellationToken);
        if (existing is not null) return existing;

        var startYear = gameDateTimeUtc.Month >= 7 ? gameDateTimeUtc.Year : gameDateTimeUtc.Year - 1;
        var label = $"{startYear}-{startYear + 1}";
        existing = dbContext.Seasons.Local.FirstOrDefault(x => x.CompetitionId == competition.Id && x.Label == label) ??
            await dbContext.Seasons.FirstOrDefaultAsync(x => x.CompetitionId == competition.Id && x.Label == label, cancellationToken);
        if (existing is not null) return existing;

        var season = new Season
        {
            Id = Guid.NewGuid(),
            CompetitionId = competition.Id,
            Label = label,
            StartDateUtc = new DateTime(startYear, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(startYear + 1, 6, 30, 23, 59, 59, DateTimeKind.Utc),
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };
        dbContext.Seasons.Add(season);
        return season;
    }

    private async Task<int> QueueEloRunsAsync(string poolKey, CancellationToken cancellationToken)
    {
        var active = await dbContext.EloRebuildRuns
            .Where(x => x.EloPoolKey == poolKey && (x.Status == EloRebuildRunStatus.Pending || x.Status == EloRebuildRunStatus.Running))
            .Select(x => x.RulesetVersion)
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var runs = EloRulesetVersions.All
            .Where(x => !active.Contains(x, StringComparer.Ordinal))
            .Select(x => new EloRebuildRun
            {
                Id = Guid.NewGuid(),
                EloPoolKey = poolKey,
                RulesetVersion = x,
                CompetitionName = string.Empty,
                Status = EloRebuildRunStatus.Pending,
                QueuedAtUtc = now,
                CreatedAtUtc = now,
                Notes = "Queued once after the complete current-results batch."
            }).ToList();
        dbContext.EloRebuildRuns.AddRange(runs);
        return runs.Count;
    }

    private static CurrentResultsRunSummary ToSummary(CurrentResultsRun run, IReadOnlyCollection<string> deferredPools) =>
        new(run.Id, run.FromDate, run.ToDate, run.PagesRead, run.CandidatesRead, run.GamesUpserted, run.ReviewsOpened, run.UnsupportedSkipped, run.EloPoolsQueued, deferredPools, run.Status, run.ErrorMessage);

    private static string? SupportedCompetitionName(string country, string competition)
    {
        var value = Regex.Replace(NormalizeName(competition), @"\b(play off|playoffs?|regular season|group stage|qualification)\b", " ", RegexOptions.CultureInvariant).Trim();
        if (value == "nba") return "NBA";
        if (value.Contains("euroleague") || value == "euro league") return "Euroleague";
        if (value.Contains("eurocup") || value.Contains("euro cup")) return "Eurocup";
        if (value.Contains("adriatic") || value.Contains("aba league")) return "ABA League";
        if (value.Contains("champions league")) return "Champions League";
        if (value.Contains("fiba europe cup")) return "FIBA Europe Cup";
        if (value.Contains("bnxt")) return "BNXT League";
        if (value.Contains("enbl")) return "ENBL";
        if (value.Contains("acb") || value.Contains("liga endesa")) return "ACB";
        if (value.Contains("copa del rey") || value == "spanish cup") return "Spanish Cup";
        if (value.Contains("supercopa") && NormalizeName(country).Contains("spain")) return "Supercopa ACB";
        if (value.Contains("elite 1") || value.Contains("lnb pro a") || value.Contains("betclic elite")) return "LNB";
        if (value.Contains("coupe de france") || value.Contains("french cup")) return "French Cup";
        if (value == "lkl") return "LKL";
        if (value.Contains("greek basket") || value == "a1") return "A1";
        if (value.Contains("greek cup")) return "Greek Cup";
        if (value.Contains("lega basket") || value.Contains("serie a")) return "Lega A";
        if (value.Contains("italian cup")) return "Italian Cup";
        if (value == "bsl" || value.Contains("super ligi")) return "Super Ligi";
        if (value.Contains("turkish cup")) return "Turkish Cup";
        if (value.Contains("liga acb")) return "ACB";
        return null;
    }

    private static string? CountryCode(string country) => NormalizeName(country) switch
    {
        "spain" => "ES", "france" => "FR", "lithuania" => "LT", "greece" => "GR", "italy" => "IT", "turkey" => "TR",
        "belgium" => "BE", "germany" => "DE", "israel" => "IL", "poland" => "PL", "czech republic" => "CZ", "czechia" => "CZ",
        "russia" => "RU", "serbia" => "RS", "croatia" => "HR", "slovenia" => "SI", "latvia" => "LV", "estonia" => "EE",
        "usa" or "united states" => "US", _ => null
    };

    private static bool CountryMatches(string? actual, string? expected) =>
        CountryCodeCatalog.AreEquivalent(actual, expected) || (string.IsNullOrWhiteSpace(actual) && string.IsNullOrWhiteSpace(expected));

    private static bool CatalogCountryMatches(string configuredCountry, string sourceCountry) =>
        NormalizeName(configuredCountry) == NormalizeName(sourceCountry) ||
        (NormalizeName(configuredCountry) is "usa" or "united states" && NormalizeName(sourceCountry) is "usa" or "united states") ||
        (NormalizeName(configuredCountry) == "europe" && NormalizeName(sourceCountry) is "europe" or "international");

    private static bool CompetitionNamesMatch(string configuredName, string sourceName)
    {
        var configured = NormalizeName(configuredName);
        var source = Regex.Replace(NormalizeName(sourceName), @"\b(play off|playoffs?|regular season|group stage|qualification)\b", " ", RegexOptions.CultureInvariant).Trim();
        return configured == source || source.Contains(configured, StringComparison.Ordinal) || configured.Contains(source, StringComparison.Ordinal);
    }

    private static string NormalizeName(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant().Normalize(), @"[^a-z0-9]+", " ", RegexOptions.CultureInvariant).Trim();

    private sealed record CompetitionMapping(Competition? Competition, string? Reason, string? SuggestedName, string? SuggestedCountryCode);
    private sealed record TeamResolution(Team? Team, bool Ambiguous);
    private sealed record PlannedFixtureMatch(Game? Game, bool Ambiguous);
    private sealed record UpsertOutcome(bool GameChanged, bool ReviewOpened, bool EloChanged, bool UnsupportedSkipped, string? EloPoolKey);
}
