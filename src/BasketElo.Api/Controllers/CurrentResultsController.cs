using BasketElo.Api.Auth;
using BasketElo.Domain.CurrentResults;
using BasketElo.Infrastructure.CurrentResults;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/current-results")]
[RequireInternalAdmin]
public class CurrentResultsController(
    BasketEloDbContext dbContext,
    ICurrentResultsIngestionService ingestionService,
    IOptions<CurrentResultsOptions> options,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost("run")]
    public async Task<ActionResult<CurrentResultsRunSummary>> Run(
        [FromBody] CurrentResultsRunRequest? request,
        CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var from = request?.FromDate ?? today.AddDays(-Math.Max(0, configuration.ReconcileDaysBack));
        var to = request?.ToDate ?? today.AddDays(Math.Max(0, configuration.ScheduleDaysAhead));
        var dryRun = request?.DryRun ?? configuration.DryRun;
        return Ok(await ingestionService.RunAsync(from, to, dryRun, cancellationToken));
    }

    [HttpGet("runs")]
    public async Task<IActionResult> GetRuns([FromQuery] int limit = 25, CancellationToken cancellationToken = default)
    {
        var runs = await dbContext.CurrentResultsRuns
            .AsNoTracking()
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
        return Ok(runs);
    }

    [HttpGet("reviews")]
    public async Task<ActionResult<IReadOnlyCollection<CurrentResultReviewDto>>> GetReviews(
        [FromQuery] string? status = "open",
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.CurrentResultReviews.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        var reviews = await query
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(x => new CurrentResultReviewDto(
                x.Id, x.SourceGameId, x.SourceDate, x.SourceUrl, x.CountryName, x.CompetitionName, x.StageName,
                x.HomeTeamName, x.AwayTeamName, x.Reason, x.Status, x.SuggestedCompetitionName,
                x.SuggestedCompetitionCountryCode, x.CreatedAtUtc, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(reviews);
    }
}

public sealed record CurrentResultsRunRequest(DateOnly? FromDate = null, DateOnly? ToDate = null, bool? DryRun = null);
