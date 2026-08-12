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
                x.Id, x.SourceGameId, x.SourceCompetitionId, x.SourceDate, x.SourceUrl, x.CountryName, x.CompetitionName, x.StageName,
                x.HomeTeamName, x.AwayTeamName, x.GameDateTimeUtc, x.HomeScore, x.AwayScore, x.ResultStatus,
                x.Reason, x.Status, x.SuggestedCompetitionName, x.SuggestedCompetitionCountryCode,
                x.AssignedGameId, x.ResolutionAction, x.ResolutionNote, x.CreatedAtUtc, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(reviews);
    }

    [HttpGet("reviews/unmatched-competitions")]
    public async Task<ActionResult<IReadOnlyList<CurrentResultsUnmatchedCompetitionDto>>> GetUnmatchedCompetitions(
        CancellationToken cancellationToken) =>
        Ok(await ingestionService.GetUnmatchedCompetitionsAsync(cancellationToken));

    [HttpPost("reviews/unmatched-competitions/merge")]
    public async Task<ActionResult<object>> MergeUnmatchedCompetition(
        [FromBody] MergeUnmatchedCompetitionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var count = await ingestionService.MergeUnmatchedCompetitionAsync(request, cancellationToken);
            return Ok(new { reviewsUpdated = count });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(exception.Message);
        }
    }

    [HttpPost("reviews/unmatched-competitions/ignore")]
    public async Task<ActionResult<object>> IgnoreUnmatchedCompetition(
        [FromBody] IgnoreUnmatchedCompetitionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var count = await ingestionService.IgnoreUnmatchedCompetitionAsync(request, cancellationToken);
            return Ok(new { reviewsIgnored = count });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpGet("reviews/{reviewId:guid}/matches")]
    public async Task<ActionResult<IReadOnlyCollection<CurrentResultReviewMatchDto>>> GetReviewMatches(
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.CurrentResultReviews
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == reviewId, cancellationToken);
        if (review is null) return NotFound();

        var minimumDateTimeUtc = review.GameDateTimeUtc.AddHours(-36);
        var maximumDateTimeUtc = review.GameDateTimeUtc.AddHours(36);
        var query = dbContext.Games
            .AsNoTracking()
            .Where(x => x.Status == CurrentResultStatuses.Scheduled &&
                        x.GameDateTimeUtc >= minimumDateTimeUtc &&
                        x.GameDateTimeUtc <= maximumDateTimeUtc);
        if (!string.IsNullOrWhiteSpace(review.SuggestedCompetitionName))
        {
            query = query.Where(x => x.Competition.Name == review.SuggestedCompetitionName);
        }

        var matches = await query
            .OrderBy(x => x.GameDateTimeUtc)
            .Take(250)
            .Select(x => new CurrentResultReviewMatchDto(
                x.Id,
                x.Source,
                x.SourceGameId,
                x.GameDateTimeUtc,
                x.Competition.Name,
                x.Season.Label,
                x.TournamentCycle == null ? null : x.TournamentCycle.Key,
                x.HomeTeam.CanonicalName,
                x.AwayTeam.CanonicalName,
                x.Status))
            .ToListAsync(cancellationToken);
        return Ok(matches);
    }

    [HttpPost("reviews/{reviewId:guid}/resolve")]
    public async Task<ActionResult<CurrentResultReviewResolutionDto>> ResolveReview(
        Guid reviewId,
        [FromBody] CurrentResultReviewResolutionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await ingestionService.ResolveReviewAsync(reviewId, request, cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(exception.Message);
        }
    }
}

public sealed record CurrentResultsRunRequest(DateOnly? FromDate = null, DateOnly? ToDate = null, bool? DryRun = null);
