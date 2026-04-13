using BasketElo.Domain.Backfill;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/backfill")]
public class BackfillController(
    BasketEloDbContext dbContext,
    IBackfillCoverageService coverageService,
    IBackfillCatalog backfillCatalog) : ControllerBase
{
    [HttpGet("coverage")]
    public async Task<ActionResult<BackfillCoverageResponse>> GetCoverage(
        [FromQuery] string? provider,
        [FromQuery] string? country,
        [FromQuery] string? leagueName,
        CancellationToken cancellationToken)
    {
        var response = await coverageService.GetCoverageAsync(provider, country, leagueName, cancellationToken);
        return Ok(response);
    }

    [HttpPost("jobs")]
    public async Task<IActionResult> CreateBackfillJob([FromBody] CreateBackfillJobRequest request, CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Country) ||
            string.IsNullOrWhiteSpace(request.LeagueName) ||
            string.IsNullOrWhiteSpace(request.Season))
        {
            return BadRequest("provider, country, leagueName and season are required.");
        }

        if (string.IsNullOrWhiteSpace(request.Provider) ||
            !string.Equals(request.Provider, "api-sports", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only provider 'api-sports' is supported in MVP.");
        }

        var job = BuildJob(request);

        dbContext.BackfillJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Accepted(new
        {
            jobId = job.Id,
            provider = job.Provider,
            country = job.Country,
            leagueName = job.LeagueName,
            season = job.Season,
            dryRun = job.DryRun,
            maxRequests = job.MaxRequests,
            status = job.Status
        });
    }

    [HttpPost("leagues/jobs")]
    public async Task<IActionResult> TriggerLeagueBackfill([FromBody] TriggerLeagueBackfillRequest request, CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Country) ||
            string.IsNullOrWhiteSpace(request.LeagueName))
        {
            return BadRequest("provider, country and leagueName are required.");
        }

        var league = backfillCatalog.GetLeagues()
            .FirstOrDefault(x =>
                string.Equals(x.Provider, request.Provider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Country, request.Country, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.LeagueName, request.LeagueName, StringComparison.OrdinalIgnoreCase));

        if (league is null)
        {
            return NotFound("Configured league was not found in the backfill catalog.");
        }

        var existingPendingSeasons = await dbContext.BackfillJobs
            .Where(x =>
                x.Provider == request.Provider &&
                x.Country == request.Country &&
                x.LeagueName == request.LeagueName &&
                (x.Status == BackfillJobStatus.Pending || x.Status == BackfillJobStatus.Running))
            .Select(x => x.Season)
            .ToListAsync(cancellationToken);

        var jobs = new List<BackfillJob>();
        foreach (var season in backfillCatalog.GetSeasonsForLeague(league))
        {
            if (existingPendingSeasons.Contains(season, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            jobs.Add(BuildJob(new CreateBackfillJobRequest
            {
                Provider = request.Provider,
                Country = request.Country,
                LeagueName = request.LeagueName,
                Season = season,
                DryRun = request.DryRun,
                MaxRequests = request.MaxRequests
            }));
        }

        if (jobs.Count > 0)
        {
            dbContext.BackfillJobs.AddRange(jobs);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Accepted(new
        {
            provider = request.Provider,
            country = request.Country,
            leagueName = request.LeagueName,
            queuedJobs = jobs.Count
        });
    }

    private static BackfillJob BuildJob(CreateBackfillJobRequest request)
    {
        return new BackfillJob
        {
            Id = Guid.NewGuid(),
            Provider = request.Provider.Trim().ToLowerInvariant(),
            Country = request.Country.Trim(),
            LeagueName = request.LeagueName.Trim(),
            Season = request.Season.Trim(),
            DryRun = request.DryRun,
            MaxRequests = request.MaxRequests <= 0 ? 2 : request.MaxRequests,
            Status = BackfillJobStatus.Pending,
            RequestsUsed = 0,
            WarningCount = 0,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }
}
