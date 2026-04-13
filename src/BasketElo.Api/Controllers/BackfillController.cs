using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/backfill/jobs")]
public class BackfillController(BasketEloDbContext dbContext) : ControllerBase
{
    [HttpPost]
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

        var job = new BackfillJob
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
}

public class CreateBackfillJobRequest
{
    public string Provider { get; set; } = "api-sports";
    public string Country { get; set; } = "Spain";
    public string LeagueName { get; set; } = "ACB";
    public string Season { get; set; } = "2024-2025";
    public bool DryRun { get; set; } = true;
    public int MaxRequests { get; set; } = 2;
}
