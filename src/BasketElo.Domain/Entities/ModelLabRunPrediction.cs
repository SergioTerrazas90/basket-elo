namespace BasketElo.Domain.Entities;

public class ModelLabRunPrediction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid GameId { get; set; }
    public Guid CompetitionId { get; set; }
    public string CompetitionName { get; set; } = string.Empty;
    public DateTime GameDateTimeUtc { get; set; }
    public string Season { get; set; } = string.Empty;
    public Guid HomeTeamId { get; set; }
    public Guid AwayTeamId { get; set; }
    public string HomeTeamName { get; set; } = string.Empty;
    public string AwayTeamName { get; set; } = string.Empty;
    public short HomeScore { get; set; }
    public short AwayScore { get; set; }
    public decimal PredictedHomeWinProbability { get; set; }
    public decimal PredictedHomeMargin { get; set; }
    public decimal ActualHomeMargin { get; set; }
    public decimal MarginError { get; set; }
    public bool PickedWinner { get; set; }

    public ModelLabRun Run { get; set; } = null!;
    public ApplicationUser OwnerUser { get; set; } = null!;
    public Game Game { get; set; } = null!;
    public Competition Competition { get; set; } = null!;
    public Team HomeTeam { get; set; } = null!;
    public Team AwayTeam { get; set; } = null!;
}
