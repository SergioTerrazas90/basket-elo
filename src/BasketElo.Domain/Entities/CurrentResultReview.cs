namespace BasketElo.Domain.Entities;

public class CurrentResultReview
{
    public Guid Id { get; set; }
    public Guid? RunId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SourceGameId { get; set; } = string.Empty;
    public string? SourceCompetitionId { get; set; }
    public string? SourceUrl { get; set; }
    public DateOnly SourceDate { get; set; }
    public DateTime GameDateTimeUtc { get; set; }
    public string CountryName { get; set; } = string.Empty;
    public string CompetitionName { get; set; } = string.Empty;
    public string? StageName { get; set; }
    public string HomeTeamName { get; set; } = string.Empty;
    public string AwayTeamName { get; set; } = string.Empty;
    public string HomeTeamSourceId { get; set; } = string.Empty;
    public string AwayTeamSourceId { get; set; } = string.Empty;
    public short? HomeScore { get; set; }
    public short? AwayScore { get; set; }
    public string ResultStatus { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? SuggestedCompetitionName { get; set; }
    public string? SuggestedCompetitionCountryCode { get; set; }
    public Guid? TournamentCycleId { get; set; }
    public string? ParserVersion { get; set; }
    public string? SourceRevision { get; set; }
    public Guid? AssignedGameId { get; set; }
    public string? ResolutionAction { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public CurrentResultsRun? Run { get; set; }
    public TournamentCycle? TournamentCycle { get; set; }
}
