using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BasketElo.Domain.Competitions;
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
    private const int AutomaticTeamMatchThreshold = 95;
    private const int SuggestedTeamMatchThreshold = 80;

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
            throw new ArgumentException("Use action 'assign' with a planned game ID, or action 'ignore'.", nameof(request));
        }

        var game = await dbContext.Games
            .Include(x => x.Competition)
            .SingleOrDefaultAsync(x => x.Id == request.GameId.Value, cancellationToken)
            ?? throw new KeyNotFoundException($"Game {request.GameId.Value} was not found.");
        if (!IsAssignablePlannedGameStatus(game.Status))
        {
            throw new InvalidOperationException("Only planned games can receive a manually assigned current result.");
        }
        if (game.HasManualResultOverride)
        {
            throw new InvalidOperationException("The selected game has a manual result override and cannot be changed by current-results review.");
        }
        if (review.Reason == CurrentResultReviewReasons.TournamentCycleConfirmationRequired && game.TournamentCycleId is null)
        {
            throw new InvalidOperationException("Confirm the tournament cycle before assigning this result to Elo.");
        }

        await EnsureTeamAliasAsync(game.HomeTeamId, review.Source, review.HomeTeamSourceId, review.HomeTeamName, cancellationToken, "planned_fixture", 100);
        await EnsureTeamAliasAsync(game.AwayTeamId, review.Source, review.AwayTeamSourceId, review.AwayTeamName, cancellationToken, "planned_fixture", 100);

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
        review.Reason = string.Empty;
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

    public async Task<IReadOnlyList<CurrentResultReviewTeamCandidateDto>> GetReviewTeamCandidatesAsync(
        Guid reviewId,
        string side,
        string? search,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.CurrentResultReviews
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == reviewId, cancellationToken)
            ?? throw new KeyNotFoundException($"Current-results review {reviewId} was not found.");

        var normalizedSide = NormalizeReviewTeamSide(side);
        var observedName = normalizedSide == "home" ? review.HomeTeamName : review.AwayTeamName;
        var searchTerm = string.IsNullOrWhiteSpace(search) ? observedName : search.Trim();
        var countryCode = CountryCode(review.CountryName);
        var competitionId = await ResolveReviewCompetitionIdAsync(review, cancellationToken);
        var candidates = await GetScoredTeamCandidatesAsync(
            observedName,
            searchTerm,
            countryCode,
            competitionId,
            review.GameDateTimeUtc,
            cancellationToken,
            includeCountryMismatches: true);
        return candidates
            .Take(10)
            .Select(x => new CurrentResultReviewTeamCandidateDto(
                x.Team.Id,
                x.Team.CanonicalName,
                x.Team.CountryCode,
                x.Confidence,
                x.Reason))
            .ToList();
    }

    public async Task<CurrentResultReviewTeamMappingDto> MapReviewTeamAsync(
        Guid reviewId,
        CurrentResultReviewTeamMappingRequest request,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.CurrentResultReviews
            .SingleOrDefaultAsync(x => x.Id == reviewId, cancellationToken)
            ?? throw new KeyNotFoundException($"Current-results review {reviewId} was not found.");
        if (review.Status != CurrentResultReviewStatuses.Open)
        {
            throw new InvalidOperationException("Only open reviews can receive a team mapping.");
        }

        var side = NormalizeReviewTeamSide(request.Side);
        var team = await dbContext.Teams
            .Include(x => x.Aliases)
            .SingleOrDefaultAsync(x => x.Id == request.TeamId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException($"Canonical team {request.TeamId} was not found.");
        var sourceTeamId = side == "home" ? review.HomeTeamSourceId : review.AwayTeamSourceId;
        var observedName = side == "home" ? review.HomeTeamName : review.AwayTeamName;
        var competitionId = await ResolveReviewCompetitionIdAsync(review, cancellationToken);
        var confidence = await CalculateTeamConfidenceAsync(
            observedName,
            team,
            CountryCode(review.CountryName),
            competitionId,
            review.GameDateTimeUtc,
            cancellationToken);
        await EnsureTeamAliasAsync(
            team.Id,
            review.Source,
            sourceTeamId,
            observedName,
            cancellationToken,
            "manual",
            confidence);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        review.ResolutionAction = $"map_{side}_team";
        review.ResolutionNote = $"Mapped {side} Livescore team identity to {team.CanonicalName}.";
        review.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        var outcome = await ReprocessStoredReviewAsync(review, cancellationToken);
        var message = review.Status == CurrentResultReviewStatuses.Resolved
            ? $"Mapped {observedName} to {team.CanonicalName}. The fixture was resolved and removed from open reviews."
            : outcome.ReviewOpened
                ? $"Mapped {observedName} to {team.CanonicalName}. Remaining issue: {review.Reason.Replace('_', ' ')}."
                : $"Mapped {observedName} to {team.CanonicalName}.";
        await identityHealthCheckService.InvalidateChangedScopeAsync(new IdentityChangedScope { Source = review.Source }, cancellationToken);

        return new CurrentResultReviewTeamMappingDto(
            review.Id,
            side,
            team.Id,
            team.CanonicalName,
            review.Status,
            message);
    }

    public async Task<CurrentResultReviewTeamMappingDto> CreateReviewTeamAsync(
        Guid reviewId,
        CurrentResultReviewTeamCreateRequest request,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.CurrentResultReviews
            .SingleOrDefaultAsync(x => x.Id == reviewId, cancellationToken)
            ?? throw new KeyNotFoundException($"Current-results review {reviewId} was not found.");
        if (review.Status != CurrentResultReviewStatuses.Open)
        {
            throw new InvalidOperationException("Only open reviews can receive a team mapping.");
        }

        var side = NormalizeReviewTeamSide(request.Side);
        var canonicalName = request.CanonicalName?.Trim();
        if (string.IsNullOrWhiteSpace(canonicalName))
        {
            throw new ArgumentException("Canonical team name is required.", nameof(request));
        }
        if (canonicalName.Length > 200)
        {
            throw new ArgumentException("Canonical team name cannot exceed 200 characters.", nameof(request));
        }

        var countryCode = CountryCodeCatalog.Normalize(request.CountryCode)
            ?? CountryCodeCatalog.Normalize(review.SuggestedCompetitionCountryCode)
            ?? CountryCode(review.CountryName)
            ?? "UNK";
        var existingTeams = await dbContext.Teams
            .Where(x => x.CountryCode == countryCode || x.CountryCode == "" || x.CountryCode == "UNK")
            .ToListAsync(cancellationToken);
        if (existingTeams.Any(x => NormalizeName(x.CanonicalName) == NormalizeName(canonicalName)))
        {
            throw new InvalidOperationException($"A canonical team named '{canonicalName}' already exists. Map this identity to that team instead.");
        }

        var team = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = canonicalName,
            CountryCode = countryCode,
            IsActive = true,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };
        dbContext.Teams.Add(team);
        var sourceTeamId = side == "home" ? review.HomeTeamSourceId : review.AwayTeamSourceId;
        var observedName = side == "home" ? review.HomeTeamName : review.AwayTeamName;
        await EnsureTeamAliasAsync(
            team.Id,
            review.Source,
            sourceTeamId,
            observedName,
            cancellationToken,
            "created",
            null);

        review.ResolutionAction = $"create_{side}_team";
        review.ResolutionNote = $"Created canonical team {canonicalName} for the {side} Livescore identity.";
        review.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);

        var outcome = await ReprocessStoredReviewAsync(review, cancellationToken);
        var message = review.Status == CurrentResultReviewStatuses.Resolved
            ? $"Created {canonicalName}, mapped {observedName}, and resolved the fixture."
            : outcome.ReviewOpened
                ? $"Created {canonicalName} and mapped {observedName}. Remaining issue: {review.Reason.Replace('_', ' ')}."
                : $"Created {canonicalName} and mapped {observedName}.";
        await identityHealthCheckService.InvalidateChangedScopeAsync(new IdentityChangedScope { Source = review.Source }, cancellationToken);

        return new CurrentResultReviewTeamMappingDto(
            review.Id,
            side,
            team.Id,
            team.CanonicalName,
            review.Status,
            message);
    }

    public async Task<IReadOnlyList<CurrentResultTeamMappingHistoryDto>> GetRecentTeamMappingsAsync(
        int days,
        CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-Math.Clamp(days, 1, 31));
        return await dbContext.TeamAliases
            .AsNoTracking()
            .Where(x => x.Source == provider.Source &&
                        x.CreatedAtUtc >= cutoff &&
                        x.MappingMethod != null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(500)
            .Select(x => new CurrentResultTeamMappingHistoryDto(
                x.CreatedAtUtc,
                x.AliasName,
                x.Team.CanonicalName,
                x.Team.CountryCode,
                x.Source,
                x.MappingMethod!,
                x.MappingConfidence))
            .ToListAsync(cancellationToken);
    }

    private async Task<UpsertOutcome> ReprocessStoredReviewAsync(CurrentResultReview review, CancellationToken cancellationToken)
    {
        if (!string.Equals(review.Source, provider.Source, StringComparison.OrdinalIgnoreCase))
        {
            return new UpsertOutcome(false, true, false, false, null);
        }

        var run = review.RunId.HasValue
            ? await dbContext.CurrentResultsRuns.SingleOrDefaultAsync(x => x.Id == review.RunId.Value, cancellationToken)
            : null;
        if (run is null)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            run = new CurrentResultsRun
            {
                Id = Guid.NewGuid(),
                Provider = provider.Source,
                FromDate = review.SourceDate,
                ToDate = review.SourceDate,
                Status = "manual_review_reprocess",
                StartedAtUtc = now,
                FinishedAtUtc = now,
                CreatedAtUtc = now
            };
            dbContext.CurrentResultsRuns.Add(run);
        }

        var candidate = new CurrentResultCandidate(
            review.SourceGameId,
            review.SourceUrl,
            review.SourceDate,
            review.GameDateTimeUtc,
            review.CountryName,
            review.CompetitionName,
            review.StageName,
            review.HomeTeamName,
            review.AwayTeamName,
            review.HomeTeamSourceId,
            review.AwayTeamSourceId,
            review.HomeScore,
            review.AwayScore,
            review.ResultStatus,
            review.ResultStatus,
            review.SourceRevision ?? "review-reprocess",
            review.ParserVersion ?? "review-reprocess",
            review.SourceCompetitionId);
        var outcome = await UpsertCandidateAsync(candidate, run, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (outcome.EloChanged && outcome.EloPoolKey is not null)
        {
            await identityHealthCheckService.InvalidateChangedScopeAsync(new IdentityChangedScope
            {
                EloPoolKey = outcome.EloPoolKey,
                Source = provider.Source
            }, cancellationToken);
            var health = await identityHealthCheckService.RunAsync(new IdentityHealthCheckRequest
            {
                EloPoolKey = outcome.EloPoolKey,
                Source = provider.Source,
                Force = true
            }, cancellationToken);
            if (health.Status != IdentityHealthCheckStatus.Blockers)
            {
                await QueueEloRunsAsync(outcome.EloPoolKey, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return outcome;
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
        foreach (var review in reviews.Where(x => x.Status == CurrentResultReviewStatuses.Open))
        {
            await ReprocessStoredReviewAsync(review, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return reviews.Count;
    }

    public async Task<int> ReprocessMergedCompetitionReviewsAsync(CancellationToken cancellationToken)
    {
        var reviews = await dbContext.CurrentResultReviews
            .Where(x => x.Status == CurrentResultReviewStatuses.Open && x.ResolutionAction == "merge")
            .ToListAsync(cancellationToken);
        foreach (var review in reviews)
        {
            await ReprocessStoredReviewAsync(review, cancellationToken);
        }

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
                target.Id,
                review.GameDateTimeUtc,
                cancellationToken);
            var away = await ResolveTeamAsync(
                review.AwayTeamName,
                review.AwayTeamSourceId,
                target.CountryCode,
                target.Id,
                review.GameDateTimeUtc,
                cancellationToken);
            if (home.Team is null || away.Team is null)
            {
                continue;
            }

            var plannedFixtureMatch = await FindCrossSourceFixtureAsync(
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
            review.Reason = string.Empty;
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
        var type = CompetitionTypeCatalog.Normalize(create.Type);
        var supportPolicy = RequiredValue(create.SupportPolicy, "New competition support policy", 30).ToLowerInvariant();
        if (!CompetitionSupportPolicies.IsValid(supportPolicy))
        {
            throw new ArgumentException("New competition support policy is invalid.");
        }
        var homeAdvantagePolicy = RequiredValue(create.HomeAdvantagePolicy, "New competition home-advantage policy", 30).ToLowerInvariant();
        if (!HomeAdvantagePolicies.IsValid(homeAdvantagePolicy))
        {
            throw new ArgumentException("New competition home-advantage policy is invalid.");
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

        var eloPoolKey = string.IsNullOrWhiteSpace(create.EloPoolKey)
            ? null
            : EloPoolKeys.Normalize(create.EloPoolKey);
        var target = new Competition
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = type,
            CountryCode = countryCode,
            EloPoolKey = eloPoolKey,
            Tier = Math.Max(0, create.Tier),
            IsActive = true,
            SupportPolicy = supportPolicy,
            HomeAdvantagePolicy = homeAdvantagePolicy,
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
        var editionLabel = TournamentCycleCatalog.ResolveEditionLabelFromFamily(
                family,
                request.TournamentCycleEditionLabel)
            ?? throw new ArgumentException("Tournament cycle edition could not be normalized.");

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

        var expectedFamily = TournamentCycleCatalog.ResolveFamilyFromKey(expectedKey);
        if (expectedFamily is null)
        {
            return;
        }

        if (!string.Equals(expectedFamily, cycle.Family, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Competition '{competitionName}' belongs to the '{expectedFamily}' cycle family, not '{cycle.Family}'.");
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
        var normalizedAlias = NormalizeName(competitionName);
        var sourceAliases = await dbContext.CompetitionAliases
            .Where(x => x.Source == source)
            .ToListAsync(cancellationToken);
        var existingAlias = sourceAliases
            .Where(x => (!string.IsNullOrWhiteSpace(request.SourceCompetitionId) &&
                        x.SourceCompetitionId == request.SourceCompetitionId) ||
                       NormalizeName(x.AliasName) == normalizedAlias)
            .OrderByDescending(x => !string.IsNullOrWhiteSpace(request.SourceCompetitionId) &&
                                    x.SourceCompetitionId == request.SourceCompetitionId)
            .FirstOrDefault();

        // Reuse an existing unsupported/inactive placeholder when the same
        // unmatched competition is ignored again. The action is intentionally
        // idempotent: repeated clicks must not create a second canonical row
        // or collide with the alias that was saved by the first attempt.
        var competition = existingAlias is null
            ? await dbContext.Competitions
                .SingleOrDefaultAsync(x => x.Name == competitionName && x.CountryCode == countryCode, cancellationToken)
            : await dbContext.Competitions
                .SingleOrDefaultAsync(x => x.Id == existingAlias.CompetitionId, cancellationToken);
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
            if (string.IsNullOrWhiteSpace(competition.CountryCode) && !string.IsNullOrWhiteSpace(countryCode))
            {
                competition.CountryCode = countryCode;
            }
        }

        if (existingAlias is null)
        {
            await AddCompetitionAliasAsync(competition, source, request.SourceCompetitionId, competitionName, cancellationToken);
        }
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
        if (IsExplicitlyUnsupportedCompetition(candidate.CountryName, candidate.CompetitionName))
        {
            var existingReview = await dbContext.CurrentResultReviews
                .SingleOrDefaultAsync(
                    x => x.Source == provider.Source && x.SourceGameId == candidate.SourceGameId,
                    cancellationToken);
            if (existingReview is not null)
            {
                dbContext.CurrentResultReviews.Remove(existingReview);
            }

            return new UpsertOutcome(false, false, false, true, null);
        }

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

        var home = await ResolveTeamAsync(candidate.HomeTeamName, candidate.HomeTeamSourceId, mapping.Competition.CountryCode, mapping.Competition.Id, candidate.GameDateTimeUtc, cancellationToken);
        var away = await ResolveTeamAsync(candidate.AwayTeamName, candidate.AwayTeamSourceId, mapping.Competition.CountryCode, mapping.Competition.Id, candidate.GameDateTimeUtc, cancellationToken);
        var reasons = new List<string>();
        if (mapping.Competition is null) reasons.Add(mapping.Reason ?? CurrentResultReviewReasons.UnsupportedCompetition);
        if (home.Team is null) reasons.Add(home.Ambiguous ? CurrentResultReviewReasons.AmbiguousHomeTeam : CurrentResultReviewReasons.UnresolvedHomeTeam);
        if (away.Team is null) reasons.Add(away.Ambiguous ? CurrentResultReviewReasons.AmbiguousAwayTeam : CurrentResultReviewReasons.UnresolvedAwayTeam);
        if (home.Team is not null && away.Team is not null && home.Team.Id == away.Team.Id)
        {
            // A basketball fixture cannot contain the same canonical team on
            // both sides. This usually means a newly inferred provider alias
            // chose the opponent, so discard any aliases inferred during this
            // candidate and send the fixture to review instead of persisting a
            // self-game.
            var candidateSourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                candidate.HomeTeamSourceId,
                candidate.AwayTeamSourceId
            };
            foreach (var entry in dbContext.ChangeTracker.Entries<TeamAlias>()
                         .Where(x => x.State == EntityState.Added &&
                             string.Equals(x.Entity.Source, provider.Source, StringComparison.OrdinalIgnoreCase) &&
                             candidateSourceIds.Contains(x.Entity.SourceTeamId))
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }

            reasons.Add(CurrentResultReviewReasons.ConflictingTeamMapping);
        }
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
            var plannedFixtureMatch = await FindCrossSourceFixtureAsync(
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

        changed |= game.GameDateTimeUtc != candidate.GameDateTimeUtc;
        var eloChanged = !tournamentCyclePendingConfirmation && existing is null && candidate.Status == CurrentResultStatuses.Finished && candidate.HomeScore.HasValue && candidate.AwayScore.HasValue;
        if (!game.HasManualResultOverride)
        {
            var preserveCompletedCrossSourceResult = reconciledAcrossSources && IsCompletedResult(game);
            if (!preserveCompletedCrossSourceResult)
            {
                var resultChanged = game.HomeScore != candidate.HomeScore || game.AwayScore != candidate.AwayScore || game.Status != candidate.Status;
                changed |= resultChanged;
                eloChanged |= !tournamentCyclePendingConfirmation && resultChanged && (IsCompletedResult(game) || candidate.Status == CurrentResultStatuses.Finished);
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
        if (candidate.IsNeutralSite.HasValue && !game.IsNeutralSite.HasValue)
        {
            game.IsNeutralSite = candidate.IsNeutralSite;
        }
        game.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        if (review is not null && review.Status == CurrentResultReviewStatuses.Open && !tournamentCyclePendingConfirmation)
        {
            review.Status = CurrentResultReviewStatuses.Resolved;
            review.Reason = string.Empty;
            review.AssignedGameId = game.Id;
            review.ResolvedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
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

    private async Task<PlannedFixtureMatch> FindCrossSourceFixtureAsync(
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

    private static bool IsCompletedResult(Game game) =>
        game.HomeScore.HasValue &&
        game.AwayScore.HasValue &&
        (string.Equals(game.Status, CurrentResultStatuses.Finished, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(game.Status, "final", StringComparison.OrdinalIgnoreCase));

    private static bool IsAssignablePlannedGameStatus(string? status) =>
        string.Equals(status, CurrentResultStatuses.Scheduled, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "not started", StringComparison.OrdinalIgnoreCase);

    private async Task EnsureTeamAliasAsync(
        Guid teamId,
        string source,
        string sourceTeamId,
        string aliasName,
        CancellationToken cancellationToken,
        string? mappingMethod = null,
        int? mappingConfidence = null)
    {
        if (string.IsNullOrWhiteSpace(sourceTeamId))
        {
            return;
        }

        var aliases = await dbContext.TeamAliases
            .Where(x => x.Source == source && x.SourceTeamId == sourceTeamId)
            .ToListAsync(cancellationToken);
        if (aliases.Any(x => x.TeamId != teamId))
        {
            throw new InvalidOperationException(
                $"The source team ID '{sourceTeamId}' is already mapped to another canonical team.");
        }

        if (!aliases.Any(x => string.Equals(x.AliasName, aliasName, StringComparison.OrdinalIgnoreCase)))
        {
            dbContext.TeamAliases.Add(new TeamAlias
            {
                Id = Guid.NewGuid(),
                TeamId = teamId,
                Source = source,
                SourceTeamId = sourceTeamId,
                AliasName = aliasName,
                MappingMethod = mappingMethod,
                MappingConfidence = mappingConfidence,
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            });
        }
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
        bool SourceAliasMatches(Competition competition) => competition.Aliases.Any(alias =>
            alias.Source == provider.Source &&
            ((!string.IsNullOrWhiteSpace(candidate.SourceCompetitionId) &&
              !string.IsNullOrWhiteSpace(alias.SourceCompetitionId) &&
              string.Equals(alias.SourceCompetitionId, candidate.SourceCompetitionId, StringComparison.OrdinalIgnoreCase)) ||
             NormalizeName(alias.AliasName) == normalizedObservedName));

        var sourceAliasMatches = competitions.Where(SourceAliasMatches).ToList();
        if (countryCode is null)
        {
            // Some current-results feeds use the competition name as the country.
            // With no usable country code, an explicit provider alias is the only
            // reliable identity signal and must not be rejected by country scoping.
            if (sourceAliasMatches.Count == 1)
            {
                return new CompetitionMapping(
                    sourceAliasMatches[0], null, sourceAliasMatches[0].Name, sourceAliasMatches[0].CountryCode);
            }

            if (sourceAliasMatches.Count > 1)
            {
                return new CompetitionMapping(
                    null, CurrentResultReviewReasons.AmbiguousCompetition, null, null);
            }
        }

        bool CanonicalNameMatches(Competition competition) =>
            (desired is not null && string.Equals(competition.Name, desired, StringComparison.OrdinalIgnoreCase)) ||
            NormalizeName(competition.Name) == normalizedObservedName;

        var matches = competitions.Where(x =>
            CountryMatches(x.CountryCode, countryCode) &&
            (CanonicalNameMatches(x) || SourceAliasMatches(x))).ToList();

        if (matches.Count == 0)
        {
            // A saved provider alias is an explicit admin decision and must remain
            // authoritative when a legacy competition has no country code. This
            // fallback is only used when no country-specific match exists.
            matches = competitions.Where(x =>
                string.IsNullOrWhiteSpace(x.CountryCode) && SourceAliasMatches(x)).ToList();
        }

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

    private async Task<Guid?> ResolveReviewCompetitionIdAsync(CurrentResultReview review, CancellationToken cancellationToken)
    {
        var name = review.SuggestedCompetitionName ?? review.CompetitionName;
        return await dbContext.Competitions
            .AsNoTracking()
            .Where(x => x.Name.ToLower() == name.ToLower())
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<TeamMatchCandidate>> GetScoredTeamCandidatesAsync(
        string observedName,
        string searchTerm,
        string? countryCode,
        Guid? competitionId,
        DateTime gameDateTimeUtc,
        CancellationToken cancellationToken,
        bool includeCountryMismatches = false)
    {
        var query = dbContext.Teams
            .AsNoTracking()
            .Include(x => x.Aliases)
            .Where(x => x.IsActive);
        if (!includeCountryMismatches && !string.IsNullOrWhiteSpace(countryCode))
        {
            query = query.Where(x => x.CountryCode == countryCode || x.CountryCode == "" || x.CountryCode == "UNK");
        }

        var nameRanked = (await query.ToListAsync(cancellationToken))
            .Select(team =>
            {
                var observedScore = ScoreTeamName(observedName, team, out var reason);
                var searchScore = ScoreTeamName(searchTerm, team, out _);
                return new { Team = team, ObservedScore = observedScore, SearchScore = searchScore, NameReason = reason };
            })
            .OrderByDescending(x => x.SearchScore)
            .ThenByDescending(x => x.ObservedScore)
            .ThenBy(x => x.Team.CanonicalName)
            .Take(50)
            .ToList();
        var teamIds = nameRanked.Select(x => x.Team.Id).ToHashSet();
        var activity = await LoadTeamActivityAsync(teamIds, competitionId, gameDateTimeUtc, cancellationToken);

        return nameRanked
            .Select(x => BuildTeamMatchCandidate(
                observedName,
                x.Team,
                x.ObservedScore,
                x.SearchScore,
                x.NameReason,
                countryCode,
                activity.RecentTeamIds.Contains(x.Team.Id),
                activity.SameCompetitionTeamIds.Contains(x.Team.Id)))
            .OrderByDescending(x => x.SearchRank)
            .ThenByDescending(x => x.Confidence)
            .ThenBy(x => x.Team.CanonicalName)
            .ToList();
    }

    private async Task<int> CalculateTeamConfidenceAsync(
        string observedName,
        Team team,
        string? countryCode,
        Guid? competitionId,
        DateTime gameDateTimeUtc,
        CancellationToken cancellationToken)
    {
        var activity = await LoadTeamActivityAsync(new HashSet<Guid> { team.Id }, competitionId, gameDateTimeUtc, cancellationToken);
        var nameScore = ScoreTeamName(observedName, team, out var reason);
        return BuildTeamMatchCandidate(
            observedName,
            team,
            nameScore,
            nameScore,
            reason,
            countryCode,
            activity.RecentTeamIds.Contains(team.Id),
            activity.SameCompetitionTeamIds.Contains(team.Id)).Confidence;
    }

    private async Task<TeamActivity> LoadTeamActivityAsync(
        IReadOnlySet<Guid> teamIds,
        Guid? competitionId,
        DateTime gameDateTimeUtc,
        CancellationToken cancellationToken)
    {
        if (teamIds.Count == 0) return new TeamActivity([], []);
        var from = gameDateTimeUtc.AddMonths(-18);
        var games = await dbContext.Games
            .AsNoTracking()
            .Where(x => x.GameDateTimeUtc >= from &&
                        x.GameDateTimeUtc < gameDateTimeUtc &&
                        (teamIds.Contains(x.HomeTeamId) || teamIds.Contains(x.AwayTeamId)))
            .Select(x => new { x.HomeTeamId, x.AwayTeamId, x.CompetitionId })
            .ToListAsync(cancellationToken);
        var recent = new HashSet<Guid>();
        var sameCompetition = new HashSet<Guid>();
        foreach (var game in games)
        {
            if (teamIds.Contains(game.HomeTeamId))
            {
                recent.Add(game.HomeTeamId);
                if (competitionId == game.CompetitionId) sameCompetition.Add(game.HomeTeamId);
            }
            if (teamIds.Contains(game.AwayTeamId))
            {
                recent.Add(game.AwayTeamId);
                if (competitionId == game.CompetitionId) sameCompetition.Add(game.AwayTeamId);
            }
        }

        return new TeamActivity(recent, sameCompetition);
    }

    private TeamMatchCandidate BuildTeamMatchCandidate(
        string observedName,
        Team team,
        int nameScore,
        int searchRank,
        string nameReason,
        string? countryCode,
        bool hasRecentGames,
        bool hasSameCompetitionGames)
    {
        var countryMatches = !string.IsNullOrWhiteSpace(countryCode) && CountryMatches(team.CountryCode, countryCode);
        var normalizedObserved = InternationalTeamCatalog.NormalizeSearchTerm(observedName);
        var hasCrossSourceAlias = team.Aliases.Any(x =>
            !string.Equals(x.Source, provider.Source, StringComparison.OrdinalIgnoreCase) &&
            InternationalTeamCatalog.NormalizeSearchTerm(x.AliasName) == normalizedObserved);
        var confidence = (int)Math.Round(nameScore * 0.70) +
                         (countryMatches ? 10 : 0) +
                         (hasRecentGames ? 10 : 0) +
                         (hasSameCompetitionGames ? 8 : 0) +
                         (hasCrossSourceAlias ? 2 : 0);
        if (HasIdentityQualifierMismatch(observedName, team.CanonicalName)) confidence = Math.Min(confidence, 79);
        confidence = Math.Clamp(confidence, 0, 100);
        var evidence = new List<string> { nameReason };
        if (hasSameCompetitionGames) evidence.Add("same competition last season");
        else if (hasRecentGames) evidence.Add("recent games");
        if (hasCrossSourceAlias) evidence.Add("matching provider alias");
        evidence.Add(confidence >= AutomaticTeamMatchThreshold
            ? "automatic"
            : confidence >= SuggestedTeamMatchThreshold ? "review suggested" : "manual review");
        return new TeamMatchCandidate(team, confidence, string.Join(" · ", evidence), searchRank);
    }

    private async Task<TeamResolution> ResolveTeamAsync(
        string name,
        string sourceId,
        string? countryCode,
        Guid competitionId,
        DateTime gameDateTimeUtc,
        CancellationToken cancellationToken)
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
                    MappingMethod = "automatic_exact",
                    MappingConfidence = 100,
                    CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
                });
            }

            return new TeamResolution(exactMatches[0], false);
        }

        if (exactMatches.Count == 0)
        {
            var fuzzyCandidates = await GetScoredTeamCandidatesAsync(
                name,
                name,
                countryCode,
                competitionId,
                gameDateTimeUtc,
                cancellationToken);
            var top = fuzzyCandidates.FirstOrDefault();
            var second = fuzzyCandidates.Skip(1).FirstOrDefault();
            if (top is not null &&
                top.Confidence >= AutomaticTeamMatchThreshold &&
                (second is null || second.Confidence < AutomaticTeamMatchThreshold))
            {
                await EnsureTeamAliasAsync(
                    top.Team.Id,
                    provider.Source,
                    sourceId,
                    name,
                    cancellationToken,
                    "automatic_fuzzy",
                    top.Confidence);
                logger.LogInformation(
                    "Automatically mapped {Provider}:{SourceTeamId} '{ObservedName}' to {CanonicalTeam} at {Confidence}% confidence.",
                    provider.Source,
                    sourceId,
                    name,
                    top.Team.CanonicalName,
                    top.Confidence);
                return new TeamResolution(top.Team, false);
            }
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
        if (value.Contains("lega basket") || Regex.IsMatch(value, @"\bserie a\b", RegexOptions.CultureInvariant)) return "Lega A";
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
        "denmark" => "DK", "great britain" or "united kingdom" => "GB", "norway" => "NO",
        "russia" => "RU", "serbia" => "RS", "croatia" => "HR", "slovenia" => "SI", "latvia" => "LV", "estonia" => "EE",
        "usa" or "united states" => "US", "mexico" => "MX", _ => null
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
        return configured == source || ContainsWholeName(source, configured) || ContainsWholeName(configured, source);
    }

    private static bool IsExplicitlyUnsupportedCompetition(string country, string competition) =>
        NormalizeName(country) == "italy" &&
        Regex.IsMatch(NormalizeName(competition), @"\bserie a\s*2\b", RegexOptions.CultureInvariant);

    private static bool ContainsWholeName(string value, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        var start = 0;
        while ((start = value.IndexOf(candidate, start, StringComparison.Ordinal)) >= 0)
        {
            var beforeIsBoundary = start == 0 || !char.IsLetterOrDigit(value[start - 1]);
            var end = start + candidate.Length;
            var afterIsBoundary = end == value.Length || !char.IsLetterOrDigit(value[end]);
            if (beforeIsBoundary && afterIsBoundary) return true;
            start++;
        }

        return false;
    }

    private static string NormalizeReviewTeamSide(string side)
    {
        var normalized = side.Trim().ToLowerInvariant();
        return normalized switch
        {
            "home" => "home",
            "away" => "away",
            _ => throw new ArgumentException("Team side must be 'home' or 'away'.", nameof(side))
        };
    }

    private static int ScoreTeamName(string observedName, Team team, out string reason)
    {
        var observed = InternationalTeamCatalog.NormalizeSearchTerm(observedName);
        var names = new[] { team.CanonicalName }.Concat(team.Aliases.Select(x => x.AliasName));
        var best = 0;
        reason = "Fuzzy name match";
        foreach (var name in names)
        {
            var candidate = InternationalTeamCatalog.NormalizeSearchTerm(name);
            if (string.IsNullOrWhiteSpace(observed) || string.IsNullOrWhiteSpace(candidate)) continue;
            var score = observed == candidate
                ? 100
                : observed.Contains(candidate, StringComparison.OrdinalIgnoreCase) || candidate.Contains(observed, StringComparison.OrdinalIgnoreCase)
                    ? 96
                    : SimilarityScore(observed, candidate);
            if (score <= best) continue;
            best = score;
            reason = score >= 100 ? "Exact normalized name" : score >= 96 ? "Name contains" : "Fuzzy name match";
        }

        return best;
    }

    private static bool HasIdentityQualifierMismatch(string observedName, string canonicalName)
    {
        var observed = IdentityQualifiers(observedName);
        var canonical = IdentityQualifiers(canonicalName);
        return !observed.SetEquals(canonical);
    }

    private static HashSet<string> IdentityQualifiers(string value)
    {
        var normalized = Regex.Replace(value.ToLowerInvariant().Normalize(), @"[^a-z0-9]+", " ", RegexOptions.CultureInvariant).Trim();
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var qualifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (token is "women" or "woman" or "w" or "ladies" or "femenino" or "feminino") qualifiers.Add("women");
            if (token is "academy" or "reserve" or "reserves" or "youth" or "junior" or "juniors") qualifiers.Add("development");
            if (Regex.IsMatch(token, @"^u\d{2}$", RegexOptions.CultureInvariant)) qualifiers.Add(token);
        }
        if (tokens.Length > 1 && tokens[^1] is "b" or "ii" or "2") qualifiers.Add("reserve");
        return qualifiers;
    }

    private static int SimilarityScore(string left, string right)
    {
        var distance = LevenshteinDistance(left, right);
        var length = Math.Max(left.Length, right.Length);
        return length == 0 ? 0 : (int)Math.Round(100d * (1d - distance / (double)length));
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++) previous[j] = j;
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static string NormalizeName(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant().Normalize(), @"[^a-z0-9]+", " ", RegexOptions.CultureInvariant).Trim();

    private sealed record CompetitionMapping(Competition? Competition, string? Reason, string? SuggestedName, string? SuggestedCountryCode);
    private sealed record TeamResolution(Team? Team, bool Ambiguous);
    private sealed record TeamMatchCandidate(Team Team, int Confidence, string Reason, int SearchRank);
    private sealed record TeamActivity(HashSet<Guid> RecentTeamIds, HashSet<Guid> SameCompetitionTeamIds);
    private sealed record PlannedFixtureMatch(Game? Game, bool Ambiguous);
    private sealed record UpsertOutcome(bool GameChanged, bool ReviewOpened, bool EloChanged, bool UnsupportedSkipped, string? EloPoolKey);
}
