namespace BasketElo.Domain.Entities;

public class ModelLabRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public Guid ModelId { get; set; }
    public Guid ModelVersionId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string ScopeType { get; set; } = string.Empty;
    public string Status { get; set; } = ModelLabRunStatuses.Completed;
    public DateTime InitializationFromUtc { get; set; }
    public DateTime InitializationToUtc { get; set; }
    public int InitializationGames { get; set; }
    public DateTime ScoredFromUtc { get; set; }
    public DateTime ScoredToUtc { get; set; }
    public int ScoredGames { get; set; }
    public int CorrectWinners { get; set; }
    public decimal WinnerAccuracy { get; set; }
    public decimal BrierScore { get; set; }
    public decimal LogLoss { get; set; }
    public decimal AverageMarginError { get; set; }
    public decimal AveragePredictedHomeWinProbability { get; set; }
    public int BaselineScoredGames { get; set; }
    public int BaselineCorrectWinners { get; set; }
    public decimal BaselineWinnerAccuracy { get; set; }
    public decimal BaselineBrierScore { get; set; }
    public decimal BaselineLogLoss { get; set; }
    public decimal BaselineAverageMarginError { get; set; }
    public decimal BaselineAveragePredictedHomeWinProbability { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }

    public ApplicationUser OwnerUser { get; set; } = null!;
    public ModelLabModel Model { get; set; } = null!;
    public ModelLabModelVersion ModelVersion { get; set; } = null!;
    public ICollection<ModelLabRunScope> Scopes { get; set; } = new List<ModelLabRunScope>();
    public ICollection<ModelLabRunPrediction> Predictions { get; set; } = new List<ModelLabRunPrediction>();
    public ICollection<ModelLabRunRating> Ratings { get; set; } = new List<ModelLabRunRating>();
    public ICollection<ModelLabRunPeriodMetric> PeriodMetrics { get; set; } = new List<ModelLabRunPeriodMetric>();
}

public static class ModelLabRunStatuses
{
    public const string Completed = "completed";
    public const string Failed = "failed";
}
