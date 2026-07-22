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
             !string.Equals(request.Provider, BasketballReferenceBasketballDataProvider.Source, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(request.Provider, FiveThirtyEightBasketballDataProvider.Source, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(request.Provider, FibaBasketballDataProvider.Source, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(request.Provider, GlobalSportsArchiveBasketballDataProvider.Source, StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest("Supported providers are 'api-sports', 'basketball-reference', 'fivethirtyeight', 'fiba' and 'global-sports-archive'.");
        }

        var job = BuildJob(request, FindConfiguredLeague(request.Provider, request.Country, request.LeagueName));

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

    [HttpDelete("jobs/{jobId:guid}")]
    public async Task<IActionResult> RemoveBackfillJob(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await dbContext.BackfillJobs
            .FirstOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        if (job is null)
        {
            return NotFound("Backfill job was not found.");
        }

        if (!CanRemoveBackfillJob(job.Status))
        {
            return Conflict("Only pending, failed, or unresolved completed-with-warnings backfills can be removed.");
        }

        var configuredLeague = FindConfiguredLeague(job.Provider, job.Country, job.LeagueName);
        var season = SeasonLabelNormalizer.ToCanonicalSeasonLabel(
            job.Season,
            configuredLeague?.UsesSingleYearSeasonLabel == true);
        var decision = await dbContext.BackfillInspectionDecisions
            .FirstOrDefaultAsync(
                x =>
                    x.Provider == job.Provider &&
                    x.Country == job.Country &&
                    x.LeagueName == job.LeagueName &&
                    x.Season == season,
                cancellationToken);
        if (decision?.Status == BackfillInspectionStatus.Resolved)
        {
            return Conflict("A resolved backfill cannot be removed.");
        }

        if (decision is not null)
        {
            dbContext.BackfillInspectionDecisions.Remove(decision);
        }

        dbContext.BackfillJobs.Remove(job);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
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
        var configuredLeague = FindConfiguredLeague(request.Provider, request.Country, request.LeagueName);
        var season = SeasonLabelNormalizer.ToCanonicalSeasonLabel(
            request.Season,
            configuredLeague?.UsesSingleYearSeasonLabel == true);

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
            (string.IsNullOrWhiteSpace(request.DisplayName) &&
             (string.IsNullOrWhiteSpace(request.Provider) ||
              string.IsNullOrWhiteSpace(request.Country) ||
              string.IsNullOrWhiteSpace(request.LeagueName))))
        {
            return BadRequest("displayName or provider, country and leagueName are required.");
        }

        var leagues = FindConfiguredLeagues(
            request.DisplayName,
            request.Provider,
            request.Country,
            request.LeagueName);
        if (leagues.Count == 0)
        {
            return NotFound("Configured league was not found in the backfill catalog.");
        }

        var responses = new List<QueueBackfillJobsResponse>();
        foreach (var league in leagues.OrderBy(x => SeasonLabelNormalizer.ParseStartYear(x.StartSeason)))
        {
            responses.Add(await QueueLeagueJobsAsync(
                league,
                backfillCatalog.GetSeasonsForLeague(league).ToList(),
                onlyMissing: false,
                replaceExisting: false,
                newestFirst: false,
                request.DryRun,
                request.MaxRequests,
                cancellationToken));
        }

        return Accepted(AggregateQueueResponses(responses, leagues, request.DisplayName));
    }

    [HttpPost("leagues/range/jobs")]
    public async Task<IActionResult> TriggerLeagueRangeBackfill(
        [FromBody] TriggerLeagueRangeBackfillRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            (string.IsNullOrWhiteSpace(request.DisplayName) &&
             (string.IsNullOrWhiteSpace(request.Provider) ||
              string.IsNullOrWhiteSpace(request.Country) ||
              string.IsNullOrWhiteSpace(request.LeagueName))))
        {
            return BadRequest("displayName or provider, country and leagueName are required.");
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

        var leagues = FindConfiguredLeagues(
            request.DisplayName,
            request.Provider,
            request.Country,
            request.LeagueName);
        if (leagues.Count == 0)
        {
            return NotFound("Configured league was not found in the backfill catalog.");
        }

        var requestedYears = Enumerable.Range(startYear, endYear - startYear + 1).ToList();
        var routes = new List<(ConfiguredBackfillLeague League, string Season)>();
        foreach (var year in requestedYears)
        {
            var matchingLeagues = leagues
                .Where(league =>
                {
                    var season = SeasonLabelNormalizer.ToCanonicalSeasonLabel(
                        year.ToString(),
                        league.UsesSingleYearSeasonLabel);
                    return backfillCatalog.GetSeasonsForLeague(league).Contains(season);
                })
                .ToList();
            var requestedSeason = $"{year}-{year + 1}";
            if (matchingLeagues.Count == 0)
            {
                return BadRequest($"Season {requestedSeason} is not configured for this league.");
            }

            if (matchingLeagues.Count > 1)
            {
                return BadRequest($"Season {requestedSeason} is configured for multiple sources; resolve the catalog overlap before queueing it.");
            }

            routes.Add((
                matchingLeagues[0],
                SeasonLabelNormalizer.ToCanonicalSeasonLabel(
                    requestedSeason,
                    matchingLeagues[0].UsesSingleYearSeasonLabel)));
        }

        var groupedRoutes = routes.GroupBy(x => x.League);
        var routeGroups = request.NewestFirst
            ? groupedRoutes
                .OrderByDescending(group => group.Max(x => SeasonLabelNormalizer.ParseStartYear(x.Season)))
                .ToList()
            : groupedRoutes
                .OrderBy(group => group.Min(x => SeasonLabelNormalizer.ParseStartYear(x.Season)))
                .ToList();
        var responses = new List<QueueBackfillJobsResponse>();
        foreach (var group in routeGroups)
        {
            responses.Add(await QueueLeagueJobsAsync(
                group.Key,
                group.Select(x => x.Season).OrderBy(SeasonLabelNormalizer.ParseStartYear).ToList(),
                request.OnlyMissing,
                request.ReplaceExisting,
                request.NewestFirst,
                request.DryRun,
                request.MaxRequests,
                cancellationToken));
        }

        return Accepted(AggregateQueueResponses(responses, leagues, request.DisplayName));
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

    private IReadOnlyCollection<ConfiguredBackfillLeague> FindConfiguredLeagues(
        string displayName,
        string provider,
        string country,
        string leagueName)
    {
        var leagues = backfillCatalog.GetLeagues();
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return leagues
                .Where(x => string.Equals(x.DisplayName, displayName.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return leagues
            .Where(x =>
                string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Country, country, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.LeagueName, leagueName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static QueueBackfillJobsResponse AggregateQueueResponses(
        IReadOnlyCollection<QueueBackfillJobsResponse> responses,
        IReadOnlyCollection<ConfiguredBackfillLeague> leagues,
        string displayName)
    {
        if (responses.Count == 1)
        {
            return responses.Single();
        }

        var firstSeason = responses.MinBy(x => SeasonLabelNormalizer.ParseStartYear(x.StartSeason))!.StartSeason;
        var lastSeason = responses.MaxBy(x => SeasonLabelNormalizer.ParseStartYear(x.EndSeason))!.EndSeason;
        var leagueName = leagues.Select(x => x.LeagueName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            ? leagues.First().LeagueName
            : displayName;
        var countrySeparator = displayName.IndexOf(':');
        var country = countrySeparator > 0
            ? displayName[..countrySeparator].Trim()
            : string.Join(", ", leagues.Select(x => x.Country).Distinct(StringComparer.OrdinalIgnoreCase));

        return new QueueBackfillJobsResponse(
            "multiple",
            country,
            leagueName,
            firstSeason,
            lastSeason,
            responses.First().OnlyMissing,
            responses.First().ReplaceExisting,
            responses.First().NewestFirst,
            responses.Sum(x => x.RequestedSeasons),
            responses.Sum(x => x.QueuedJobs),
            responses.Sum(x => x.SkippedActiveJobs),
            responses.Sum(x => x.SkippedExistingSeasons));
    }

    private async Task<QueueBackfillJobsResponse> QueueLeagueJobsAsync(
        ConfiguredBackfillLeague league,
        IReadOnlyCollection<string> seasons,
        bool onlyMissing,
        bool replaceExisting,
        bool newestFirst,
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
        var orderedSeasons = newestFirst
            ? seasons.OrderByDescending(SeasonLabelNormalizer.ParseStartYear)
            : seasons.OrderBy(SeasonLabelNormalizer.ParseStartYear);
        foreach (var season in orderedSeasons)
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
            }, league));
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
            newestFirst,
            seasons.Count,
            jobs.Count,
            skippedActive,
            skippedExisting);
    }

    private ConfiguredBackfillLeague? FindConfiguredLeague(string provider, string country, string leagueName)
        => backfillCatalog.GetLeagues().FirstOrDefault(x =>
            string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Country, country, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.LeagueName, leagueName, StringComparison.OrdinalIgnoreCase));

    private static BackfillJob BuildJob(
        CreateBackfillJobRequest request,
        ConfiguredBackfillLeague? configuredLeague)
    {
        return new BackfillJob
        {
            Id = Guid.NewGuid(),
            Provider = request.Provider.Trim().ToLowerInvariant(),
            Country = request.Country.Trim(),
            LeagueName = request.LeagueName.Trim(),
            Season = SeasonLabelNormalizer.ToCanonicalSeasonLabel(
                request.Season,
                configuredLeague?.UsesSingleYearSeasonLabel == true),
            DryRun = request.DryRun,
            // Zero is an intentional unlimited budget for discovered archives
            // such as Global Sports Archive; negative values retain the safe
            // default for malformed/manual requests.
            MaxRequests = request.MaxRequests < 0 ? 2 : request.MaxRequests,
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

    private static bool CanRemoveBackfillJob(string status) =>
        status is BackfillJobStatus.Pending or
            BackfillJobStatus.Failed or
            BackfillJobStatus.CompletedWithWarnings;
}
