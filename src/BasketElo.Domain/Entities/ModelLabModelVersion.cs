namespace BasketElo.Domain.Entities;

public class ModelLabModelVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ModelId { get; set; }
    public int VersionNumber { get; set; }
    public string ParameterSchemaVersion { get; set; } = "model-lab-v1";
    public decimal BaseRating { get; set; }
    public int KFactor { get; set; }
    public decimal HomeAdvantageElo { get; set; }
    public decimal ProbabilityScale { get; set; }
    public bool UsesMarginAdjustment { get; set; }
    public decimal? PointsPerEloMargin { get; set; }
    public decimal CompetitionWeight { get; set; }
    public string? ExtensionDataJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ModelLabModel Model { get; set; } = null!;
    public ICollection<ModelLabRun> Runs { get; set; } = new List<ModelLabRun>();
}
