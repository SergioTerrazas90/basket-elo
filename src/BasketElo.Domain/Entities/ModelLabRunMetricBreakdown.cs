namespace BasketElo.Domain.Entities;

public class ModelLabRunMetricBreakdown
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string SegmentType { get; set; } = string.Empty;
    public string SegmentKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public Guid? CompetitionId { get; set; }
    public string? Season { get; set; }
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

    public ModelLabRun Run { get; set; } = null!;
    public ApplicationUser OwnerUser { get; set; } = null!;
    public Competition? Competition { get; set; }
}
