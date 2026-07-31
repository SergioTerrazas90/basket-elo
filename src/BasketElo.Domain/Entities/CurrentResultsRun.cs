namespace BasketElo.Domain.Entities;

public class CurrentResultsRun
{
    public Guid Id { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int PagesRead { get; set; }
    public int CandidatesRead { get; set; }
    public int GamesUpserted { get; set; }
    public int ReviewsOpened { get; set; }
    public int EloPoolsQueued { get; set; }
    public string? DeferredEloPoolsJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
