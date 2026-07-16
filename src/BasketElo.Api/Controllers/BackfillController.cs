using BasketElo.Api.Auth;
using BasketElo.Domain.Backfill;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/backfill")]
[RequireInternalAdmin]
public class BackfillController(
    BasketEloDbContext dbContext,
    IBackfillCoverageService coverageService,
    IBackfillCatalog backfillCatalog,
    INbaCurrentSeasonRefreshService nbaRefreshService) : ControllerBase
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
            (!string.Equals(request.Provider, ApiSportsBasketballDataProvider.Source, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(request.Provider, BasketballReferenceBasketballDataProvider.Source, StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest("Supported providers are 'api-sports' and 'basketball-reference'.");
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

    [HttpPost("coverage/decisions")]
    public async Task<IActionResult> SaveInspectionDecision(
        [FromBody] SaveBackfillInspectionDecisionRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Provider) ||
            string.IsNullOrWhiteSpace(request.Country) ||
            string.IsNullOrWhiteSpace(request.LeagueName) ||
            string.IsNullOrWhiteSpace(request.Season))
        {
            return BadRequest("provider, country, leagueName and season are required.");
        }

        var status = NormalizeInspectionStatus(request.Status);
        if (status is null)
        {
            return BadRequest("status must be confirmed_empty, provider_gap, covid_partial_missing or resolved.");
        }

        var provider = request.Provider.Trim().ToLowerInvariant();
        var country = request.Country.Trim();
        var leagueName = request.LeagueName.Trim();
        var season = SeasonLabelNormalizer.ToFullSeasonLabel(request.Season);

        var decision = await dbContext.BackfillInspectionDecisions
            .FirstOrDefaultAsync(
                x =>
                    x.Provider == provider &&
                    x.Country == country &&
                    x.LeagueName == leagueName &&
                    x.Season == season,
                cancellationToken);

        if (decision is null)
        {
            decision = new BackfillInspectionDecision
            {
                Id = Guid.NewGuid(),
                Provider = provider,
                Country = country,
                LeagueName = leagueName,
                Season = season
            };
            dbContext.BackfillInspectionDecisions.Add(decision);
        }

        decision.Status = status;
        decision.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        decision.ReviewedBy = string.IsNullOrWhiteSpace(request.ReviewedBy) ? "admin" : request.ReviewedBy.Trim();
        decision.ReviewedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            decision.Id,
            decision.Provider,
            decision.Country,
            decision.LeagueName,
            decision.Season,
            decision.Status,
            decision.Note,
            decision.ReviewedBy,
            decision.ReviewedAtUtc
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

        var seasons = backfillCatalog.GetSeasonsForLeague(league).ToList();
        var response = await QueueLeagueJobsAsync(
            league,
            seasons,
            onlyMissing: false,
            replaceExisting: false,
            request.DryRun,
            request.MaxRequests,
            cancellationToken);
        return Accepted(response);
    }

    [HttpPost("leagues/range/jobs")]
    public async Task<IActionResult> TriggerLeagueRangeBackfill(
        [FromBody] TriggerLeagueRangeBackfillRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Provider) ||
            string.IsNullOrWhiteSpace(request.Country) ||
            string.IsNullOrWhiteSpace(request.LeagueName))
        {
            return BadRequest("provider, country and leagueName are required.");
        }

        if (request.OnlyMissing && request.ReplaceExisting)
        {
            return BadRequest("onlyMissing and replaceExisting cannot both be true.");
        }

        if (!SeasonLabelNormalizer.TryParseSeason(request.StartSeason, out _, out var startYear) ||
            !SeasonLabelNormalizer.TryParseSeason(request.EndSeason, out _, out var endYear) ||
            startYear > endYear)
        {
            return BadRequest("startSeason and endSeason must be valid consecutive-year seasons, and startSeason must not be after endSeason.");
        }

        var league = FindConfiguredLeague(request.Provider, request.Country, request.LeagueName);
        if (league is null)
        {
            return NotFound("Configured league was not found in the backfill catalog.");
        }

        var configuredSeasons = backfillCatalog.GetSeasonsForLeague(league).ToList();
        var firstConfiguredYear = SeasonLabelNormalizer.ParseStartYear(configuredSeasons[0]);
        var lastConfiguredYear = SeasonLabelNormalizer.ParseStartYear(configuredSeasons[^1]);
        if (startYear < firstConfiguredYear || endYear > lastConfiguredYear)
        {
            return BadRequest($"Season range must be within {configuredSeasons[0]} and {configuredSeasons[^1]} for this league.");
        }

        var seasons = configuredSeasons
            .Where(season =>
            {
                var year = SeasonLabelNormalizer.ParseStartYear(season);
                return year >= startYear && year <= endYear;
            })
            .ToList();

        var response = await QueueLeagueJobsAsync(
            league,
            seasons,
            request.OnlyMissing,
            request.ReplaceExisting,
            request.DryRun,
            request.MaxRequests,
            cancellationToken);
        return Accepted(response);
    }

    [HttpPost("nba/current/jobs")]
    public async Task<IActionResult> TriggerCurrentNbaRefresh(
        [FromBody] NbaCurrentSeasonRefreshRequest? request,
        CancellationToken cancellationToken)
    {
        var response = await nbaRefreshService.QueueManualAsync(
            request?.DryRun ?? false,
            request?.MaxRequests,
            cancellationToken);
        return response.Queued ? Accepted(response) : Ok(response);
    }

    private ConfiguredBackfillLeague? FindConfiguredLeague(string provider, string country, string leagueName) =>
        backfillCatalog.GetLeagues().FirstOrDefault(x =>
            string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Country, country, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.LeagueName, leagueName, StringComparison.OrdinalIgnoreCase));

    private async Task<QueueBackfillJobsResponse> QueueLeagueJobsAsync(
        ConfiguredBackfillLeague league,
        IReadOnlyCollection<string> seasons,
        bool onlyMissing,
        bool replaceExisting,
        bool dryRun,
        int maxRequests,
        CancellationToken cancellationToken)
    {
        var activeSeasons = await dbContext.BackfillJobs
            .Where(x =>
                x.Provider == league.Provider &&
                x.Country == league.Country &&
                x.LeagueName == league.LeagueName &&
                (x.Status == BackfillJobStatus.Pending || x.Status == BackfillJobStatus.Running))
            .Select(x => x.Season)
            .ToListAsync(cancellationToken);
        var active = activeSeasons.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existing = onlyMissing
            ? (await dbContext.Games
                .Where(x => x.Source == league.Provider && x.Competition.Name == league.LeagueName)
                .Select(x => x.Season.Label)
                .Distinct()
                .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        var jobs = new List<BackfillJob>();
        var skippedActive = 0;
        var skippedExisting = 0;
        foreach (var season in seasons)
        {
            if (active.Contains(season))
            {
                skippedActive++;
                continue;
            }

            if (existing.Contains(season))
            {
                skippedExisting++;
                continue;
            }

            jobs.Add(BuildJob(new CreateBackfillJobRequest
            {
                Provider = league.Provider,
                Country = league.Country,
                LeagueName = league.LeagueName,
                Season = season,
                DryRun = dryRun,
                MaxRequests = maxRequests
            }));
        }

        if (jobs.Count > 0)
        {
            dbContext.BackfillJobs.AddRange(jobs);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new QueueBackfillJobsResponse(
            league.Provider,
            league.Country,
            league.LeagueName,
            seasons.First(),
            seasons.Last(),
            onlyMissing,
            replaceExisting,
            seasons.Count,
            jobs.Count,
            skippedActive,
            skippedExisting);
    }

    private static BackfillJob BuildJob(CreateBackfillJobRequest request)
    {
        return new BackfillJob
        {
            Id = Guid.NewGuid(),
            Provider = request.Provider.Trim().ToLowerInvariant(),
            Country = request.Country.Trim(),
            LeagueName = request.LeagueName.Trim(),
            Season = SeasonLabelNormalizer.ToFullSeasonLabel(request.Season),
            DryRun = request.DryRun,
            MaxRequests = request.MaxRequests <= 0 ? 2 : request.MaxRequests,
            Status = BackfillJobStatus.Pending,
            RequestsUsed = 0,
            WarningCount = 0,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static string? NormalizeInspectionStatus(string status)
    {
        var normalized = status.Trim().ToLowerInvariant();
        return normalized is BackfillInspectionStatus.ConfirmedEmpty or
            BackfillInspectionStatus.ProviderGap or
            BackfillInspectionStatus.CovidPartialMissing or
            BackfillInspectionStatus.Resolved
            ? normalized
            : null;
    }
}
