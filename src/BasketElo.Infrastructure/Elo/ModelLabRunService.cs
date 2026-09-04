using System.Text.Json;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Jobs;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BasketElo.Infrastructure.Elo;

public sealed class ModelLabRunService(
    BasketEloDbContext dbContext,
    IModelLabBacktestService backtestService,
    IModelLabJobDispatcher jobDispatcher) : IModelLabRunService
{
    private const int MaxRunsReturned = 100;
    private const int MaxRatingsReturned = 100;
    private const int MaxMissesReturned = 12;
    private const int MaxPredictionPageSize = 500;

    public async Task<ModelLabRunCreateResponse?> CreateAsync(
        Guid ownerUserId,
        ModelLabEntitlement entitlement,
        CreateModelLabRunRequest request,
        CancellationToken cancellationToken)
        => await CreateAsyncCore(ownerUserId, entitlement, request, enforceActiveLimit: true, comparisonGroupId: null, cancellationToken: cancellationToken);

    public async Task<ModelLabComparisonCreateResponse> CreateComparisonAsync(
        Guid ownerUserId,
        ModelLabEntitlement entitlement,
        CreateModelLabComparisonRequest request,
        CancellationToken cancellationToken)
    {
        var modelIds = request.ModelIds?.Distinct().Take(3).ToList() ?? [];
        if (modelIds.Count < 2)
        {
            throw new ArgumentException("Select at least two models to compare.");
        }

        var selectedModels = await dbContext.ModelLabModels
            .AsNoTracking()
            .Where(x => modelIds.Contains(x.Id) && x.OwnerUserId == ownerUserId && !x.IsArchived)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (selectedModels.Count != modelIds.Count)
        {
            throw new ArgumentException("One or more selected models could not be found or are archived.");
        }

        await EnforceStoredRunLimitAsync(ownerUserId, entitlement, cancellationToken, modelIds.Count);
        await EnforceMonthlyRunLimitAsync(ownerUserId, entitlement, modelIds.Count, cancellationToken);

        if (await HasActiveRunAsync(ownerUserId, null, cancellationToken))
        {
            throw ActiveRunLimitException(entitlement);
        }

        var comparisonGroupId = Guid.NewGuid();
        await using IDbContextTransaction? transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var results = new List<ModelLabRunCreateResponse>(modelIds.Count);
        foreach (var modelId in modelIds)
        {
            var run = await CreateAsyncCore(
                ownerUserId,
                entitlement,
                new CreateModelLabRunRequest(
                    modelId,
                    null,
                    request.InitializationFromUtc,
                    request.InitializationToUtc,
                    request.ScoredFromUtc,
                    request.ScoredToUtc,
                    request.ScopeType,
                    request.CompetitionIds,
                    request.EloPoolKey,
                    request.LeagueName),
                enforceActiveLimit: false,
                comparisonGroupId: comparisonGroupId,
                cancellationToken: cancellationToken);

            if (run is null)
            {
                throw new ArgumentException("One or more selected models could not be found.");
            }

            results.Add(run);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new ModelLabComparisonCreateResponse(results);
    }

    public async Task<ModelLabSavedComparisonResponse?> GetLatestCompatibleComparisonAsync(
        Guid ownerUserId,
        IReadOnlyCollection<Guid> modelIds,
        CancellationToken cancellationToken)
    {
        var selectedModelIds = modelIds.Distinct().Take(3).ToHashSet();
        if (selectedModelIds.Count < 2)
        {
            return null;
        }

        var comparisons = await ListCompatibleComparisonsAsync(ownerUserId, MaxRunsReturned, cancellationToken);
        return comparisons.FirstOrDefault(comparison =>
            comparison.Runs.Select(run => run.ModelId).ToHashSet().SetEquals(selectedModelIds));
    }

    public async Task<IReadOnlyCollection<ModelLabSavedComparisonResponse>> ListCompatibleComparisonsAsync(
        Guid ownerUserId,
        int take,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(take, 1, 50);

        var versions = await dbContext.ModelLabModelVersions
            .AsNoTracking()
            .Where(version => version.Model.OwnerUserId == ownerUserId && !version.Model.IsArchived)
            .Select(version => new { version.ModelId, version.Id, version.VersionNumber })
            .ToListAsync(cancellationToken);
        var currentVersionByModel = versions
            .GroupBy(version => version.ModelId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(version => version.VersionNumber).First().Id);
        if (currentVersionByModel.Count < 2)
        {
            return [];
        }

        var recentComparisonRuns = await dbContext.ModelLabRuns
            .AsNoTracking()
            .Where(run => run.OwnerUserId == ownerUserId &&
                          run.ComparisonGroupId.HasValue &&
                          run.Status == ModelLabRunStatuses.Completed)
            .OrderByDescending(run => run.CompletedAtUtc)
            .Take(MaxRunsReturned * 3)
            .ToListAsync(cancellationToken);

        return recentComparisonRuns
            .GroupBy(run => run.ComparisonGroupId!.Value)
            .Where(group => group.Count() is >= 2 and <= 3 &&
                            group.Select(run => run.ModelId).Distinct().Count() == group.Count() &&
                            group.All(run => currentVersionByModel.TryGetValue(run.ModelId, out var currentVersionId) && currentVersionId == run.ModelVersionId))
            .OrderByDescending(group => group.Max(run => run.CompletedAtUtc ?? run.CreatedAtUtc))
            .Take(pageSize)
            .Select(group =>
            {
                var orderedRuns = group.OrderBy(run => run.ModelName).ToList();
                return new ModelLabSavedComparisonResponse(
                    group.Key,
                    orderedRuns.Max(run => run.CompletedAtUtc ?? run.CreatedAtUtc),
                    orderedRuns.Select(run => ToSummaryResponse(run, null)).ToList());
            })
            .ToList();
    }

    private async Task<ModelLabRunCreateResponse?> CreateAsyncCore(
        Guid ownerUserId,
        ModelLabEntitlement entitlement,
        CreateModelLabRunRequest request,
        bool enforceActiveLimit,
        Guid? comparisonGroupId,
        CancellationToken cancellationToken)
    {
        var model = await dbContext.ModelLabModels
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.Id == request.ModelId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (model is null)
        {
            return null;
        }

        if (model.IsArchived)
        {
            throw new ArgumentException("Archived models cannot be run. Restore the model before creating a run.");
        }

        var version = request.ModelVersionId.HasValue
            ? model.Versions.FirstOrDefault(x => x.Id == request.ModelVersionId.Value)
            : model.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();

        if (version is null)
        {
            throw new ArgumentException("The selected model version does not exist.");
        }

        var runLeagueName = string.IsNullOrWhiteSpace(request.LeagueName)
            ? model.LeagueName
            : request.LeagueName.Trim();
        if (string.IsNullOrWhiteSpace(runLeagueName))
        {
            throw new ArgumentException("Choose a competition in the run configuration before running the model.");
        }

        var scopeType = NormalizeScopeType(request.ScopeType);
        EnforceScopeLimit(entitlement, runLeagueName, scopeType);
        await EnforceStoredRunLimitAsync(ownerUserId, entitlement, cancellationToken);
        if (enforceActiveLimit)
        {
            await EnforceSingleActiveRunAsync(ownerUserId, entitlement, cancellationToken);
        }

        var poolKey = await ResolvePoolKeyAsync(runLeagueName, request, cancellationToken);
        var now = DateTime.UtcNow;
        var run = new ModelLabRun
        {
            OwnerUserId = ownerUserId,
            ModelId = model.Id,
            ModelVersionId = version.Id,
            ModelName = model.Name,
            LeagueName = runLeagueName,
            EloPoolKey = poolKey,
            ScopeType = scopeType,
            Status = ModelLabRunStatuses.Queued,
            ComparisonGroupId = comparisonGroupId,
            RequestCompetitionIdsJson = JsonSerializer.Serialize(request.CompetitionIds ?? []),
            ProgressPercent = 0,
            ProgressStage = "Waiting for a worker",
            IsRetained = true,
            InitializationFromUtc = request.InitializationFromUtc,
            InitializationToUtc = request.InitializationToUtc,
            ScoredFromUtc = request.ScoredFromUtc,
            ScoredToUtc = request.ScoredToUtc,
            CreatedAtUtc = now,
            CompletedAtUtc = null
        };

        var monthlyUsage = await ReserveMonthlyRunSlotAsync(
            ownerUserId,
            entitlement,
            run.Id,
            ModelLabRunUsageTypes.Run,
            now,
            cancellationToken);
        dbContext.ModelLabRuns.Add(run);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(run).State = EntityState.Detached;
            if (monthlyUsage is not null)
            {
                dbContext.Entry(monthlyUsage).State = EntityState.Detached;
            }

            if (await HasActiveRunAsync(ownerUserId, run.Id, cancellationToken))
            {
                throw ActiveRunLimitException(entitlement);
            }

            throw;
        }

        var queuePosition = await GetQueuePositionAsync(run, cancellationToken);
        return new ModelLabRunCreateResponse(
            run.Id,
            run.ModelId,
            run.ModelVersionId,
            run.Status,
            run.CreatedAtUtc,
            run.CompletedAtUtc,
            null,
            queuePosition,
            run.ProgressPercent,
            run.ProgressStage);
    }

    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.ModelLabRuns
            .Include(x => x.ModelVersion)
            .SingleAsync(x => x.Id == runId, cancellationToken);

        if (run.Status != ModelLabRunStatuses.Running)
        {
            throw new InvalidOperationException($"Model Lab run '{runId}' is not running.");
        }

        var competitionIds = string.IsNullOrWhiteSpace(run.RequestCompetitionIdsJson)
            ? Array.Empty<Guid>()
            : JsonSerializer.Deserialize<Guid[]>(run.RequestCompetitionIdsJson) ?? [];
        var backtestRequest = new ModelLabBacktestRequest(
            run.ModelName,
            ToParameterSet(run.ModelVersion),
            run.LeagueName,
            run.InitializationFromUtc,
            run.InitializationToUtc,
            run.ScoredFromUtc,
            run.ScoredToUtc,
            run.ScopeType,
            competitionIds,
            run.EloPoolKey);

        var execution = await backtestService.RunDetailedAsync(backtestRequest, cancellationToken);
        await dbContext.Entry(run).ReloadAsync(cancellationToken);
        if (run.Status == ModelLabRunStatuses.Canceled)
        {
            return;
        }
        var result = execution.Response;
        run.ProgressPercent = 85;
        run.ProgressStage = "Saving results";
        await dbContext.SaveChangesAsync(cancellationToken);

        run.LeagueName = result.LeagueName;
        run.EloPoolKey = result.EloPoolKey ?? throw new InvalidOperationException("The Model Lab run did not resolve an ELO pool.");
        run.InitializationGames = result.InitializationWindow.Games;
        run.ScoredGames = result.Summary.ScoredGames;
        run.CorrectWinners = result.Summary.CorrectWinners;
        run.WinnerAccuracy = result.Summary.WinnerAccuracy;
        run.BrierScore = result.Summary.BrierScore;
        run.LogLoss = result.Summary.LogLoss;
        run.AverageMarginError = result.Summary.AverageMarginError;
        run.AveragePredictedHomeWinProbability = result.Summary.AveragePredictedHomeWinProbability;
        run.BaselineScoredGames = result.BaselineSummary.ScoredGames;
        run.BaselineCorrectWinners = result.BaselineSummary.CorrectWinners;
        run.BaselineWinnerAccuracy = result.BaselineSummary.WinnerAccuracy;
        run.BaselineBrierScore = result.BaselineSummary.BrierScore;
        run.BaselineLogLoss = result.BaselineSummary.LogLoss;
        run.BaselineAverageMarginError = result.BaselineSummary.AverageMarginError;
        run.BaselineAveragePredictedHomeWinProbability = result.BaselineSummary.AveragePredictedHomeWinProbability;

        foreach (var scope in execution.ScopeCompetitions)
        {
            run.Scopes.Add(new ModelLabRunScope
            {
                CompetitionId = scope.Id,
                CompetitionName = scope.DisplayName,
                CountryCode = scope.CountryCode
            });
        }

        foreach (var prediction in execution.Predictions)
        {
            run.Predictions.Add(new ModelLabRunPrediction
            {
                OwnerUserId = run.OwnerUserId,
                GameId = prediction.GameId,
                CompetitionId = prediction.CompetitionId,
                CompetitionName = prediction.CompetitionName,
                GameDateTimeUtc = prediction.GameDateTimeUtc,
                Season = prediction.Season,
                HomeTeamId = prediction.HomeTeamId,
                AwayTeamId = prediction.AwayTeamId,
                HomeTeamName = prediction.HomeTeam,
                AwayTeamName = prediction.AwayTeam,
                HomeScore = prediction.HomeScore,
                AwayScore = prediction.AwayScore,
                PredictedHomeWinProbability = prediction.PredictedHomeWinProbability,
                PredictedHomeMargin = prediction.PredictedHomeMargin,
                ActualHomeMargin = prediction.ActualHomeMargin,
                MarginError = prediction.MarginError,
                PickedWinner = prediction.PickedWinner
            });
        }

        var baselineRatingsByTeam = execution.BaselineRatings.ToDictionary(x => x.TeamId);
        foreach (var rating in execution.Ratings)
        {
            baselineRatingsByTeam.TryGetValue(rating.TeamId, out var baselineRating);
            run.Ratings.Add(new ModelLabRunRating
            {
                OwnerUserId = run.OwnerUserId,
                Rank = rating.Rank,
                TeamId = rating.TeamId,
                TeamName = rating.TeamName,
                Elo = rating.Elo,
                GamesPlayed = rating.GamesPlayed,
                RecentMovement = rating.RecentMovement,
                BaselineRank = baselineRating?.Rank,
                BaselineElo = baselineRating?.Elo
            });
        }

        foreach (var teamEvolution in execution.Evolution.GroupBy(x => x.TeamId))
        {
            var sampledPoints = EloEvolutionLimits.EvenlySample(
                teamEvolution.OrderBy(x => x.GameDateTimeUtc).ThenBy(x => x.GameId).ToList());
            foreach (var point in sampledPoints)
            {
                run.EvolutionPoints.Add(new ModelLabRunEvolutionPoint
                {
                    OwnerUserId = run.OwnerUserId,
                    TeamId = point.TeamId,
                    TeamName = point.TeamName,
                    GameId = point.GameId,
                    GameDateTimeUtc = point.GameDateTimeUtc,
                    CompetitionName = point.CompetitionName,
                    Season = point.Season,
                    Elo = point.Elo,
                    EloDelta = point.EloDelta,
                    Rank = point.Rank
                });
            }
        }

        foreach (var period in result.Periods)
        {
            run.PeriodMetrics.Add(new ModelLabRunPeriodMetric
            {
                OwnerUserId = run.OwnerUserId,
                PeriodKey = period.Label,
                Games = period.Games,
                WinnerAccuracy = period.WinnerAccuracy,
                AverageMarginError = period.AverageMarginError
            });
        }

        foreach (var breakdown in BuildMetricBreakdowns(
            run.OwnerUserId,
            result.Summary,
            result.BaselineSummary,
            execution.Predictions,
            execution.BaselinePredictions))
        {
            run.MetricBreakdowns.Add(breakdown);
        }

        run.Status = ModelLabRunStatuses.Completed;
        run.ProgressPercent = 100;
        run.ProgressStage = "Completed";
        run.CompletedAtUtc = DateTime.UtcNow;
        run.ErrorMessage = null;

        dbContext.ModelLabRunScopes.AddRange(run.Scopes);
        dbContext.ModelLabRunPredictions.AddRange(run.Predictions);
        dbContext.ModelLabRunRatings.AddRange(run.Ratings);
        dbContext.ModelLabRunEvolutionPoints.AddRange(run.EvolutionPoints);
        dbContext.ModelLabRunPeriodMetrics.AddRange(run.PeriodMetrics);
        dbContext.ModelLabRunMetricBreakdowns.AddRange(run.MetricBreakdowns);
        dbContext.ChangeTracker.DetectChanges();
        var autoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }

    public async Task<IReadOnlyCollection<ModelLabRunSummaryResponse>> ListAsync(
        Guid ownerUserId,
        int take,
        Guid? modelId,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(take, 1, MaxRunsReturned);

        var query = dbContext.ModelLabRuns
            .AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId);
        if (modelId.HasValue)
        {
            query = query.Where(x => x.ModelId == modelId.Value);
        }

        var runs = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var queuePositions = await GetQueuePositionsAsync(cancellationToken);
        return runs
            .Select(run => ToSummaryResponse(
                run,
                queuePositions.TryGetValue(run.Id, out var position) ? position : null))
            .ToList();
    }

    public async Task<ModelLabRunQuotaResponse> GetQuotaAsync(
        Guid ownerUserId,
        ModelLabEntitlement entitlement,
        CancellationToken cancellationToken)
    {
        var runCount = await CountRunsAsync(ownerUserId, cancellationToken);
        var monthlyRunCount = await CountMonthlyRunsAsync(ownerUserId, DateTime.UtcNow, cancellationToken);
        var monthlyLimit = entitlement.MonthlyRunLimit;
        var monthStart = GetMonthStartUtc(DateTime.UtcNow);
        return new ModelLabRunQuotaResponse(
            runCount,
            entitlement.StoredRunLimit,
            entitlement.StoredRunLimit.HasValue && runCount >= entitlement.StoredRunLimit.Value,
            monthlyRunCount,
            monthlyLimit,
            monthlyLimit.HasValue && monthlyRunCount >= monthlyLimit.Value,
            monthlyLimit.HasValue ? monthStart.AddMonths(1) : null);
    }

    public async Task<ModelLabRunDetailResponse?> GetAsync(
        Guid ownerUserId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.ModelLabRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (run is null)
        {
            return null;
        }

        var scopes = await dbContext.ModelLabRunScopes
            .AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderBy(x => x.CompetitionName)
            .Select(x => new ModelLabCompetitionOption(
                x.CompetitionId,
                x.CompetitionName,
                x.CompetitionName,
                x.CountryCode,
                run.EloPoolKey))
            .ToListAsync(cancellationToken);

        var ratings = await dbContext.ModelLabRunRatings
            .AsNoTracking()
            .Where(x => x.RunId == runId && x.OwnerUserId == ownerUserId)
            .OrderBy(x => x.Rank)
            .Take(MaxRatingsReturned)
            .Select(x => new ModelLabRatingRow(
                x.Rank,
                x.TeamId,
                x.TeamName,
                x.Elo,
                x.GamesPlayed,
                x.RecentMovement,
                x.BaselineRank,
                x.BaselineElo))
            .ToListAsync(cancellationToken);

        var biggestMisses = await dbContext.ModelLabRunPredictions
            .AsNoTracking()
            .Where(x => x.RunId == runId && x.OwnerUserId == ownerUserId)
            .OrderByDescending(x => x.MarginError)
            .Take(MaxMissesReturned)
            .Select(x => ToPredictionRow(x))
            .ToListAsync(cancellationToken);

        var periods = await dbContext.ModelLabRunPeriodMetrics
            .AsNoTracking()
            .Where(x => x.RunId == runId && x.OwnerUserId == ownerUserId)
            .OrderBy(x => x.PeriodKey)
            .Select(x => new ModelLabPeriodMetric(
                x.PeriodKey,
                x.Games,
                x.WinnerAccuracy,
                x.AverageMarginError))
            .ToListAsync(cancellationToken);

        var metricBreakdowns = await dbContext.ModelLabRunMetricBreakdowns
            .AsNoTracking()
            .Where(x => x.RunId == runId && x.OwnerUserId == ownerUserId)
            .ToListAsync(cancellationToken);

        var queuePosition = await GetQueuePositionAsync(run, cancellationToken);

        return new ModelLabRunDetailResponse(
            ToSummaryResponse(run, queuePosition),
            scopes,
            ratings,
            biggestMisses,
            periods,
            metricBreakdowns
                .OrderBy(x => SegmentSort(x.SegmentType))
                .ThenBy(x => x.Label)
                .Select(ToMetricBreakdownResponse)
                .ToList());
    }

    public async Task<ModelLabRunPredictionPageResponse?> GetPredictionsAsync(
        Guid ownerUserId,
        Guid runId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.ModelLabRuns
            .AsNoTracking()
            .AnyAsync(x => x.Id == runId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (!exists)
        {
            return null;
        }

        var safeSkip = Math.Max(0, skip);
        var pageSize = Math.Clamp(take, 1, MaxPredictionPageSize);
        var query = dbContext.ModelLabRunPredictions
            .AsNoTracking()
            .Where(x => x.RunId == runId && x.OwnerUserId == ownerUserId);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(x => x.GameDateTimeUtc)
            .ThenBy(x => x.Id)
            .Skip(safeSkip)
            .Take(pageSize)
            .Select(x => ToPredictionRow(x))
            .ToListAsync(cancellationToken);

        return new ModelLabRunPredictionPageResponse(runId, total, safeSkip, pageSize, rows);
    }

    public async Task<ModelLabRunEvolutionResponse?> GetEvolutionAsync(
        Guid ownerUserId,
        Guid runId,
        int teamCount,
        int pointsPerTeam,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.ModelLabRuns
            .AsNoTracking()
            .AnyAsync(x => x.Id == runId && x.OwnerUserId == ownerUserId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        var safeTeamCount = Math.Clamp(teamCount <= 0 ? 10 : teamCount, 1, 20);
        var safePointCount = EloEvolutionLimits.NormalizePointsPerTeam(pointsPerTeam);
        var teamIds = await dbContext.ModelLabRunRatings
            .AsNoTracking()
            .Where(x => x.RunId == runId && x.OwnerUserId == ownerUserId)
            .OrderBy(x => x.Rank)
            .Take(safeTeamCount)
            .Select(x => x.TeamId)
            .ToListAsync(cancellationToken);

        var points = await dbContext.ModelLabRunEvolutionPoints
            .AsNoTracking()
            .Where(x => x.RunId == runId &&
                x.OwnerUserId == ownerUserId &&
                teamIds.Contains(x.TeamId))
            .OrderBy(x => x.GameDateTimeUtc)
            .ThenBy(x => x.GameId)
            .ToListAsync(cancellationToken);

        var series = points
            .GroupBy(x => new { x.TeamId, x.TeamName })
            .OrderBy(group => teamIds.IndexOf(group.Key.TeamId))
            .Select(group =>
            {
                var allPoints = group
                    .Select(x => new EloTeamEvolutionPoint(
                        x.GameDateTimeUtc,
                        x.Elo,
                        x.EloDelta,
                        x.Rank,
                        x.GameId))
                    .ToList();
                return new EloTeamEvolutionSeries(
                    group.Key.TeamId,
                    group.Key.TeamName,
                    EloEvolutionLimits.EvenlySample(allPoints, safePointCount),
                    allPoints.Count);
            })
            .ToList();

        return new ModelLabRunEvolutionResponse(runId, series);
    }

    public async Task<bool> DeleteAsync(
        Guid ownerUserId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.ModelLabRuns
            .FirstOrDefaultAsync(x => x.Id == runId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (run is null)
        {
            return false;
        }

        if (run.Status is ModelLabRunStatuses.Queued or ModelLabRunStatuses.Running)
        {
            throw new ArgumentException("Cancel the active run before deleting it.");
        }

        dbContext.ModelLabRuns.Remove(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ModelLabRunSummaryResponse?> CancelAsync(
        Guid ownerUserId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.ModelLabRuns
            .FirstOrDefaultAsync(x => x.Id == runId && x.OwnerUserId == ownerUserId, cancellationToken);
        if (run is null)
        {
            return null;
        }
        if (run.Status is not (ModelLabRunStatuses.Queued or ModelLabRunStatuses.Running))
        {
            throw new ArgumentException("Only a queued or running Model Lab run can be cancelled.");
        }

        run.Status = ModelLabRunStatuses.Canceled;
        run.ProgressStage = "Canceled";
        run.CompletedAtUtc = DateTime.UtcNow;
        run.ErrorMessage = "Canceled by the user.";
        await dbContext.SaveChangesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(run.HangfireJobId))
        {
            jobDispatcher.Delete(run.HangfireJobId);
        }

        return ToSummaryResponse(run, null);
    }

    public async Task<ModelLabRunSummaryResponse?> RetryAsync(
        Guid ownerUserId,
        Guid runId,
        ModelLabEntitlement entitlement,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.ModelLabRuns
            .FirstOrDefaultAsync(x => x.Id == runId && x.OwnerUserId == ownerUserId, cancellationToken);
        if (run is null)
        {
            return null;
        }
        if (run.Status is not (ModelLabRunStatuses.Failed or ModelLabRunStatuses.Canceled))
        {
            throw new ArgumentException("Only a failed or cancelled Model Lab run can be retried.");
        }

        await EnforceSingleActiveRunAsync(ownerUserId, entitlement, cancellationToken);
        var now = DateTime.UtcNow;
        await ReserveMonthlyRunSlotAsync(
            ownerUserId,
            entitlement,
            run.Id,
            ModelLabRunUsageTypes.Retry,
            now,
            cancellationToken);
        dbContext.ModelLabRunScopes.RemoveRange(dbContext.ModelLabRunScopes.Where(x => x.RunId == runId));
        dbContext.ModelLabRunPredictions.RemoveRange(dbContext.ModelLabRunPredictions.Where(x => x.RunId == runId));
        dbContext.ModelLabRunRatings.RemoveRange(dbContext.ModelLabRunRatings.Where(x => x.RunId == runId));
        dbContext.ModelLabRunEvolutionPoints.RemoveRange(dbContext.ModelLabRunEvolutionPoints.Where(x => x.RunId == runId));
        dbContext.ModelLabRunPeriodMetrics.RemoveRange(dbContext.ModelLabRunPeriodMetrics.Where(x => x.RunId == runId));
        dbContext.ModelLabRunMetricBreakdowns.RemoveRange(dbContext.ModelLabRunMetricBreakdowns.Where(x => x.RunId == runId));
        run.Status = ModelLabRunStatuses.Queued;
        run.HangfireJobId = null;
        run.ProgressPercent = 0;
        run.ProgressStage = "Waiting for a worker";
        run.StartedAtUtc = null;
        run.CompletedAtUtc = null;
        run.ErrorMessage = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSummaryResponse(run, await GetQueuePositionAsync(run, cancellationToken));
    }

    public async Task<ModelLabRunSummaryResponse?> RetainAsync(
        Guid ownerUserId,
        Guid runId,
        ModelLabEntitlement entitlement,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.ModelLabRuns
            .FirstOrDefaultAsync(x => x.Id == runId && x.OwnerUserId == ownerUserId, cancellationToken);
        if (run is null)
        {
            return null;
        }
        if (run.Status != ModelLabRunStatuses.Completed)
        {
            throw new ArgumentException("Only a completed result can be retained.");
        }
        if (!run.IsRetained)
        {
            await EnforceStoredRunLimitAsync(ownerUserId, entitlement, cancellationToken);
            run.IsRetained = true;
            run.ExpiresAtUtc = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return ToSummaryResponse(run, null);
    }

    public async Task<int> CleanupExpiredTemporaryRunsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (dbContext.Database.IsRelational())
        {
            return await dbContext.ModelLabRuns
                .Where(x => !x.IsRetained && x.ExpiresAtUtc < now &&
                    x.Status != ModelLabRunStatuses.Queued &&
                    x.Status != ModelLabRunStatuses.Running)
                .ExecuteDeleteAsync(cancellationToken);
        }

        var expired = await dbContext.ModelLabRuns
            .Where(x => !x.IsRetained && x.ExpiresAtUtc < now &&
                x.Status != ModelLabRunStatuses.Queued &&
                x.Status != ModelLabRunStatuses.Running)
            .ToListAsync(cancellationToken);
        dbContext.ModelLabRuns.RemoveRange(expired);
        await dbContext.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    private async Task<string> ResolvePoolKeyAsync(
        string leagueName,
        CreateModelLabRunRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.EloPoolKey))
        {
            if (!EloPoolKeys.IsSupported(request.EloPoolKey))
            {
                throw new ArgumentException($"Unknown ELO pool '{request.EloPoolKey}'.");
            }

            return EloPoolKeys.Normalize(request.EloPoolKey);
        }

        var requestedCompetitionIds = request.CompetitionIds?.Distinct().ToList() ?? [];
        var poolKeys = requestedCompetitionIds.Count > 0
            ? await dbContext.Competitions
                .AsNoTracking()
                .Where(x => requestedCompetitionIds.Contains(x.Id))
                .Select(x => x.EloPoolKey)
                .Distinct()
                .ToListAsync(cancellationToken)
            : await dbContext.Competitions
                .AsNoTracking()
                .Where(x => x.Name == leagueName)
                .Select(x => x.EloPoolKey)
                .Distinct()
                .ToListAsync(cancellationToken);

        var supportedPools = poolKeys
            .Where(EloPoolKeys.IsSupported)
            .Select(EloPoolKeys.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (supportedPools.Count != 1)
        {
            throw new ArgumentException("The selected competitions must resolve to exactly one ELO pool.");
        }

        return supportedPools[0];
    }

    private async Task EnforceSingleActiveRunAsync(
        Guid ownerUserId,
        ModelLabEntitlement entitlement,
        CancellationToken cancellationToken)
    {
        if (await HasActiveRunAsync(ownerUserId, null, cancellationToken))
        {
            throw ActiveRunLimitException(entitlement);
        }
    }

    private Task<bool> HasActiveRunAsync(
        Guid ownerUserId,
        Guid? excludedRunId,
        CancellationToken cancellationToken)
        => dbContext.ModelLabRuns
            .AsNoTracking()
            .AnyAsync(x =>
                x.OwnerUserId == ownerUserId &&
                (!excludedRunId.HasValue || x.Id != excludedRunId.Value) &&
                (x.Status == ModelLabRunStatuses.Queued || x.Status == ModelLabRunStatuses.Running),
                cancellationToken);

    private static ModelLabLimitException ActiveRunLimitException(ModelLabEntitlement entitlement)
        => new(
            "model_lab_run_already_active",
            "Only one Model Lab run can be queued or running per user. Wait for the active run to finish before starting another.",
            false,
            entitlement.SavedModelLimit,
            entitlement.RequiredLeagueName,
            entitlement.StoredRunLimit,
            entitlement.MonthlyRunLimit);

    private async Task<Dictionary<Guid, int>> GetQueuePositionsAsync(CancellationToken cancellationToken)
    {
        var queuedIds = await dbContext.ModelLabRuns
            .AsNoTracking()
            .Where(x => x.Status == ModelLabRunStatuses.Queued)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        return queuedIds
            .Select((id, index) => new { id, Position = index + 1 })
            .ToDictionary(x => x.id, x => x.Position);
    }

    private async Task<int?> GetQueuePositionAsync(ModelLabRun run, CancellationToken cancellationToken)
    {
        if (run.Status != ModelLabRunStatuses.Queued)
        {
            return null;
        }

        var positions = await GetQueuePositionsAsync(cancellationToken);
        return positions.TryGetValue(run.Id, out var position) ? position : null;
    }

    private static void EnforceScopeLimit(ModelLabEntitlement entitlement, string leagueName, string scopeType)
    {
        if (!string.IsNullOrWhiteSpace(entitlement.RequiredLeagueName) &&
            (!string.Equals(scopeType, ModelLabScopeTypes.SingleCompetition, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(leagueName, entitlement.RequiredLeagueName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ModelLabLimitException(
                "league_restricted",
                $"{entitlement.PlanKey} users can create Model Lab runs for {entitlement.RequiredLeagueName} only.",
                true,
                entitlement.SavedModelLimit,
                entitlement.RequiredLeagueName);
        }
    }

    private async Task EnforceStoredRunLimitAsync(
        Guid ownerUserId,
        ModelLabEntitlement entitlement,
        CancellationToken cancellationToken,
        int additionalRuns = 0)
    {
        if (!entitlement.StoredRunLimit.HasValue)
        {
            return;
        }

        var runCount = await CountRunsAsync(ownerUserId, cancellationToken);

        var withinLimit = additionalRuns > 0
            ? runCount + additionalRuns <= entitlement.StoredRunLimit.Value
            : runCount < entitlement.StoredRunLimit.Value;
        if (withinLimit)
        {
            return;
        }

        var message = entitlement.IsPaid
            ? $"Paid users can store up to {entitlement.StoredRunLimit.Value} Model Lab runs. Delete an old run before saving another."
            : $"Free users can store up to {entitlement.StoredRunLimit.Value} Model Lab runs. Delete an old run before starting another comparison.";

        throw new ModelLabLimitException(
            "stored_run_limit_reached",
            message,
            !entitlement.IsPaid,
            entitlement.SavedModelLimit,
            entitlement.RequiredLeagueName,
            entitlement.StoredRunLimit);
    }

    private Task<int> CountRunsAsync(Guid ownerUserId, CancellationToken cancellationToken)
        => dbContext.ModelLabRuns
            .AsNoTracking()
            .CountAsync(x =>
                x.OwnerUserId == ownerUserId &&
                x.IsRetained &&
                x.Status == ModelLabRunStatuses.Completed,
                cancellationToken);

    private async Task<ModelLabMonthlyRunUsage?> ReserveMonthlyRunSlotAsync(
        Guid ownerUserId,
        ModelLabEntitlement entitlement,
        Guid runId,
        string usageType,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (!entitlement.MonthlyRunLimit.HasValue)
        {
            return null;
        }

        var used = await EnforceMonthlyRunLimitAsync(ownerUserId, entitlement, 1, cancellationToken, nowUtc);
        var usage = new ModelLabMonthlyRunUsage
        {
            OwnerUserId = ownerUserId,
            MonthStartUtc = GetMonthStartUtc(nowUtc),
            SlotNumber = used + 1,
            RunId = runId,
            UsageType = usageType,
            CreatedAtUtc = nowUtc
        };
        dbContext.ModelLabMonthlyRunUsages.Add(usage);
        return usage;
    }

    private async Task<int> EnforceMonthlyRunLimitAsync(
        Guid ownerUserId,
        ModelLabEntitlement entitlement,
        int additionalRuns,
        CancellationToken cancellationToken,
        DateTime? nowUtc = null)
    {
        if (!entitlement.MonthlyRunLimit.HasValue)
        {
            return 0;
        }

        var now = nowUtc ?? DateTime.UtcNow;
        var used = await CountMonthlyRunsAsync(ownerUserId, now, cancellationToken);
        if (used + additionalRuns <= entitlement.MonthlyRunLimit.Value)
        {
            return used;
        }

        throw MonthlyRunLimitException(entitlement, GetMonthStartUtc(now).AddMonths(1));
    }

    private Task<int> CountMonthlyRunsAsync(
        Guid ownerUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var monthStart = GetMonthStartUtc(nowUtc);
        var nextMonth = monthStart.AddMonths(1);
        return dbContext.ModelLabMonthlyRunUsages
            .AsNoTracking()
            .CountAsync(x =>
                x.OwnerUserId == ownerUserId &&
                x.MonthStartUtc >= monthStart &&
                x.MonthStartUtc < nextMonth,
                cancellationToken);
    }

    private static DateTime GetMonthStartUtc(DateTime utc)
        => new(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ModelLabLimitException MonthlyRunLimitException(
        ModelLabEntitlement entitlement,
        DateTime resetsAtUtc)
        => new(
            "monthly_run_limit_reached",
            $"Premium users can run up to {entitlement.MonthlyRunLimit ?? 200} models per calendar month. Your allowance resets on {resetsAtUtc:MMMM d, yyyy} (UTC).",
            false,
            entitlement.SavedModelLimit,
            entitlement.RequiredLeagueName,
            entitlement.StoredRunLimit,
            entitlement.MonthlyRunLimit);

    private static IReadOnlyCollection<ModelLabRunMetricBreakdown> BuildMetricBreakdowns(
        Guid ownerUserId,
        ModelLabBacktestSummary summary,
        ModelLabBacktestSummary baselineSummary,
        IReadOnlyCollection<ModelLabPredictionRow> predictions,
        IReadOnlyCollection<ModelLabPredictionRow> baselinePredictions)
    {
        var baselineByGameId = baselinePredictions.ToDictionary(x => x.GameId);
        var rows = new List<ModelLabRunMetricBreakdown>
        {
            CreateMetricBreakdown(
                ownerUserId,
                ModelLabMetricSegmentTypes.FullRun,
                ModelLabMetricSegmentTypes.FullRun,
                "Full run",
                null,
                null,
                summary,
                baselineSummary)
        };

        rows.AddRange(predictions
            .GroupBy(x => x.Season)
            .OrderByDescending(x => x.Key)
            .Select(group => CreateMetricBreakdown(
                ownerUserId,
                ModelLabMetricSegmentTypes.Season,
                group.Key,
                group.Key,
                null,
                group.Key,
                BuildSummary(group),
                BuildSummary(GetBaselineRows(group, baselineByGameId)))));

        rows.AddRange(predictions
            .GroupBy(x => new { x.CompetitionId, x.CompetitionName })
            .OrderBy(x => x.Key.CompetitionName)
            .Select(group => CreateMetricBreakdown(
                ownerUserId,
                ModelLabMetricSegmentTypes.Competition,
                group.Key.CompetitionId.ToString(),
                group.Key.CompetitionName,
                group.Key.CompetitionId,
                null,
                BuildSummary(group),
                BuildSummary(GetBaselineRows(group, baselineByGameId)))));

        rows.AddRange(predictions
            .GroupBy(x => new DateTime(x.GameDateTimeUtc.Year, x.GameDateTimeUtc.Month, 1))
            .OrderBy(x => x.Key)
            .Select(group => CreateMetricBreakdown(
                ownerUserId,
                ModelLabMetricSegmentTypes.Month,
                group.Key.ToString("yyyy-MM"),
                group.Key.ToString("yyyy-MM"),
                null,
                null,
                BuildSummary(group),
                BuildSummary(GetBaselineRows(group, baselineByGameId)))));

        return rows;
    }

    private static ModelLabRunMetricBreakdown CreateMetricBreakdown(
        Guid ownerUserId,
        string segmentType,
        string segmentKey,
        string label,
        Guid? competitionId,
        string? season,
        ModelLabBacktestSummary summary,
        ModelLabBacktestSummary baselineSummary)
        => new()
        {
            OwnerUserId = ownerUserId,
            SegmentType = segmentType,
            SegmentKey = segmentKey,
            Label = label,
            CompetitionId = competitionId,
            Season = season,
            ScoredGames = summary.ScoredGames,
            CorrectWinners = summary.CorrectWinners,
            WinnerAccuracy = summary.WinnerAccuracy,
            BrierScore = summary.BrierScore,
            LogLoss = summary.LogLoss,
            AverageMarginError = summary.AverageMarginError,
            AveragePredictedHomeWinProbability = summary.AveragePredictedHomeWinProbability,
            BaselineScoredGames = baselineSummary.ScoredGames,
            BaselineCorrectWinners = baselineSummary.CorrectWinners,
            BaselineWinnerAccuracy = baselineSummary.WinnerAccuracy,
            BaselineBrierScore = baselineSummary.BrierScore,
            BaselineLogLoss = baselineSummary.LogLoss,
            BaselineAverageMarginError = baselineSummary.AverageMarginError,
            BaselineAveragePredictedHomeWinProbability = baselineSummary.AveragePredictedHomeWinProbability
        };

    private static IReadOnlyCollection<ModelLabPredictionRow> GetBaselineRows(
        IEnumerable<ModelLabPredictionRow> customRows,
        IReadOnlyDictionary<Guid, ModelLabPredictionRow> baselineByGameId)
        => customRows
            .Select(x => baselineByGameId.TryGetValue(x.GameId, out var baseline) ? baseline : null)
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

    private static ModelLabBacktestSummary BuildSummary(IEnumerable<ModelLabPredictionRow> predictions)
    {
        var rows = predictions.ToList();
        if (rows.Count == 0)
        {
            return new ModelLabBacktestSummary(0, 0, 0m, 0m, 0m, 0m, 0m);
        }

        var correct = rows.Count(x => x.PickedWinner);
        var brier = rows.Average(x =>
        {
            var actual = x.HomeScore > x.AwayScore ? 1m : 0m;
            return Math.Pow((double)(x.PredictedHomeWinProbability - actual), 2d);
        });
        var logLoss = rows.Average(x =>
        {
            var actual = x.HomeScore > x.AwayScore ? 1m : 0m;
            var probability = Math.Clamp(x.PredictedHomeWinProbability, 0.001m, 0.999m);
            return actual == 1m
                ? -Math.Log((double)probability)
                : -Math.Log((double)(1m - probability));
        });

        return new ModelLabBacktestSummary(
            rows.Count,
            correct,
            RoundPercentage(correct / (decimal)rows.Count),
            RoundProbability((decimal)brier),
            RoundProbability((decimal)logLoss),
            RoundRating(rows.Average(x => x.MarginError)),
            RoundPercentage(rows.Average(x => x.PredictedHomeWinProbability)));
    }

    private static int SegmentSort(string segmentType)
        => segmentType switch
        {
            ModelLabMetricSegmentTypes.FullRun => 0,
            ModelLabMetricSegmentTypes.Competition => 1,
            ModelLabMetricSegmentTypes.Season => 2,
            ModelLabMetricSegmentTypes.Month => 3,
            _ => 4
        };

    private static string NormalizeScopeType(string? scopeType)
        => scopeType?.Trim().ToLowerInvariant() switch
        {
            ModelLabScopeTypes.SelectedCompetitions => ModelLabScopeTypes.SelectedCompetitions,
            ModelLabScopeTypes.AllCompetitions => ModelLabScopeTypes.AllCompetitions,
            _ => ModelLabScopeTypes.SingleCompetition
        };

    private static ModelLabParameterSet ToParameterSet(ModelLabModelVersion version)
        => new(
            version.BaseRating,
            version.KFactor,
            version.HomeAdvantageElo,
            version.ProbabilityScale,
            version.UsesMarginAdjustment,
            version.PointsPerEloMargin,
            version.CompetitionWeight,
            version.MarginDampenerFactor,
            version.MaxMarginMultiplier);

    private static ModelLabRunSummaryResponse ToSummaryResponse(ModelLabRun run, int? queuePosition)
        => new(
            run.Id,
            run.ModelId,
            run.ModelVersionId,
            run.ModelName,
            run.LeagueName,
            run.EloPoolKey,
            run.ScopeType,
            run.Status,
            queuePosition,
            run.ProgressPercent,
            run.ProgressStage,
            run.IsRetained,
            run.ExpiresAtUtc,
            run.CreatedAtUtc,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.ErrorMessage,
            new ModelLabBacktestWindow(
                run.InitializationFromUtc,
                run.InitializationToUtc,
                run.InitializationGames),
            new ModelLabBacktestWindow(
                run.ScoredFromUtc,
                run.ScoredToUtc,
                run.ScoredGames),
            new ModelLabBacktestSummary(
                run.ScoredGames,
                run.CorrectWinners,
                run.WinnerAccuracy,
                run.BrierScore,
                run.LogLoss,
                run.AverageMarginError,
                run.AveragePredictedHomeWinProbability),
            new ModelLabBacktestSummary(
                run.BaselineScoredGames,
                run.BaselineCorrectWinners,
                run.BaselineWinnerAccuracy,
                run.BaselineBrierScore,
                run.BaselineLogLoss,
                run.BaselineAverageMarginError,
                run.BaselineAveragePredictedHomeWinProbability));

    private static ModelLabRunMetricBreakdownResponse ToMetricBreakdownResponse(ModelLabRunMetricBreakdown metric)
        => new(
            metric.SegmentType,
            metric.SegmentKey,
            metric.Label,
            metric.CompetitionId,
            metric.Season,
            new ModelLabBacktestSummary(
                metric.ScoredGames,
                metric.CorrectWinners,
                metric.WinnerAccuracy,
                metric.BrierScore,
                metric.LogLoss,
                metric.AverageMarginError,
                metric.AveragePredictedHomeWinProbability),
            new ModelLabBacktestSummary(
                metric.BaselineScoredGames,
                metric.BaselineCorrectWinners,
                metric.BaselineWinnerAccuracy,
                metric.BaselineBrierScore,
                metric.BaselineLogLoss,
                metric.BaselineAverageMarginError,
                metric.BaselineAveragePredictedHomeWinProbability));

    private static ModelLabPredictionRow ToPredictionRow(ModelLabRunPrediction prediction)
        => new(
            prediction.GameId,
            prediction.CompetitionId,
            prediction.CompetitionName,
            prediction.GameDateTimeUtc,
            prediction.Season,
            prediction.HomeTeamId,
            prediction.HomeTeamName,
            prediction.AwayTeamId,
            prediction.AwayTeamName,
            prediction.HomeScore,
            prediction.AwayScore,
            prediction.PredictedHomeWinProbability,
            prediction.PredictedHomeMargin,
            prediction.ActualHomeMargin,
            prediction.MarginError,
            prediction.PickedWinner);

    private static decimal RoundRating(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal RoundProbability(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static decimal RoundPercentage(decimal value) => Math.Round(value * 100m, 1, MidpointRounding.AwayFromZero);
}
