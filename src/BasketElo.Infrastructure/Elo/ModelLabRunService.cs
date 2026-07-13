using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Infrastructure.Elo;

public sealed class ModelLabRunService(
    BasketEloDbContext dbContext,
    IModelLabBacktestService backtestService) : IModelLabRunService
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

        var scopeType = NormalizeScopeType(request.ScopeType);
        EnforceScopeLimit(entitlement, model.LeagueName, scopeType);
        await EnforceStoredRunLimitAsync(ownerUserId, entitlement, cancellationToken);

        var backtestRequest = new ModelLabBacktestRequest(
            model.Name,
            ToParameterSet(version),
            model.LeagueName,
            request.InitializationFromUtc,
            request.InitializationToUtc,
            request.ScoredFromUtc,
            request.ScoredToUtc,
            scopeType,
            request.CompetitionIds);

        var execution = await backtestService.RunDetailedAsync(backtestRequest, cancellationToken);
        var now = DateTime.UtcNow;
        var result = execution.Response;
        var run = new ModelLabRun
        {
            OwnerUserId = ownerUserId,
            ModelId = model.Id,
            ModelVersionId = version.Id,
            ModelName = model.Name,
            LeagueName = result.LeagueName,
            ScopeType = scopeType,
            Status = ModelLabRunStatuses.Completed,
            InitializationFromUtc = result.InitializationWindow.FromUtc,
            InitializationToUtc = result.InitializationWindow.ToUtc,
            InitializationGames = result.InitializationWindow.Games,
            ScoredFromUtc = result.ScoredWindow.FromUtc,
            ScoredToUtc = result.ScoredWindow.ToUtc,
            ScoredGames = result.Summary.ScoredGames,
            CorrectWinners = result.Summary.CorrectWinners,
            WinnerAccuracy = result.Summary.WinnerAccuracy,
            BrierScore = result.Summary.BrierScore,
            LogLoss = result.Summary.LogLoss,
            AverageMarginError = result.Summary.AverageMarginError,
            AveragePredictedHomeWinProbability = result.Summary.AveragePredictedHomeWinProbability,
            BaselineScoredGames = result.BaselineSummary.ScoredGames,
            BaselineCorrectWinners = result.BaselineSummary.CorrectWinners,
            BaselineWinnerAccuracy = result.BaselineSummary.WinnerAccuracy,
            BaselineBrierScore = result.BaselineSummary.BrierScore,
            BaselineLogLoss = result.BaselineSummary.LogLoss,
            BaselineAverageMarginError = result.BaselineSummary.AverageMarginError,
            BaselineAveragePredictedHomeWinProbability = result.BaselineSummary.AveragePredictedHomeWinProbability,
            CreatedAtUtc = now,
            CompletedAtUtc = now
        };

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
                OwnerUserId = ownerUserId,
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

        foreach (var rating in execution.Ratings)
        {
            run.Ratings.Add(new ModelLabRunRating
            {
                OwnerUserId = ownerUserId,
                Rank = rating.Rank,
                TeamId = rating.TeamId,
                TeamName = rating.TeamName,
                Elo = rating.Elo,
                GamesPlayed = rating.GamesPlayed,
                RecentMovement = rating.RecentMovement
            });
        }

        foreach (var period in result.Periods)
        {
            run.PeriodMetrics.Add(new ModelLabRunPeriodMetric
            {
                OwnerUserId = ownerUserId,
                PeriodKey = period.Label,
                Games = period.Games,
                WinnerAccuracy = period.WinnerAccuracy,
                AverageMarginError = period.AverageMarginError
            });
        }

        var autoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            dbContext.ModelLabRuns.Add(run);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
        }

        return new ModelLabRunCreateResponse(
            run.Id,
            run.ModelId,
            run.ModelVersionId,
            run.Status,
            run.CreatedAtUtc,
            run.CompletedAtUtc,
            result with { RunId = run.Id });
    }

    public async Task<IReadOnlyCollection<ModelLabRunSummaryResponse>> ListAsync(
        Guid ownerUserId,
        int take,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(take, 1, MaxRunsReturned);

        var runs = await dbContext.ModelLabRuns
            .AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return runs.Select(ToSummaryResponse).ToList();
    }

    public async Task<ModelLabRunQuotaResponse> GetQuotaAsync(
        Guid ownerUserId,
        ModelLabEntitlement entitlement,
        CancellationToken cancellationToken)
    {
        var runCount = await CountRunsAsync(ownerUserId, cancellationToken);
        return new ModelLabRunQuotaResponse(
            runCount,
            entitlement.StoredRunLimit,
            entitlement.StoredRunLimit.HasValue && runCount >= entitlement.StoredRunLimit.Value);
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
                x.CountryCode))
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
                x.RecentMovement))
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

        return new ModelLabRunDetailResponse(
            ToSummaryResponse(run),
            scopes,
            ratings,
            biggestMisses,
            periods);
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

        dbContext.ModelLabRuns.Remove(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
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
        CancellationToken cancellationToken)
    {
        if (!entitlement.StoredRunLimit.HasValue)
        {
            return;
        }

        var runCount = await CountRunsAsync(ownerUserId, cancellationToken);

        if (runCount < entitlement.StoredRunLimit.Value)
        {
            return;
        }

        var message = entitlement.IsPaid
            ? $"Paid users can store up to {entitlement.StoredRunLimit.Value} Model Lab runs. Delete an old run before saving another."
            : $"Free users can store one Model Lab run. Delete your existing run or upgrade for up to 100 saved runs.";

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
            .CountAsync(x => x.OwnerUserId == ownerUserId, cancellationToken);

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
            version.CompetitionWeight);

    private static ModelLabRunSummaryResponse ToSummaryResponse(ModelLabRun run)
        => new(
            run.Id,
            run.ModelId,
            run.ModelVersionId,
            run.ModelName,
            run.LeagueName,
            run.ScopeType,
            run.Status,
            run.CreatedAtUtc,
            run.CompletedAtUtc,
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
}
