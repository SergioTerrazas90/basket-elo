using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Elo;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/elo")]
public class EloController(
    BasketEloDbContext dbContext,
    IEloRebuildNotificationPublisher notificationPublisher) : ControllerBase
{
    [HttpGet("rulesets")]
    public ActionResult<EloRulesetCatalogResponse> GetRulesets()
    {
        return Ok(BuildRulesetCatalog());
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<EloDashboardResponse>> GetDashboard(
        [FromQuery] string? rulesetVersion,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var selectedRuleset = ResolveRulesetOrDefault(rulesetVersion);
        if (selectedRuleset is null)
        {
            return BadRequest($"Unsupported ELO ruleset '{rulesetVersion}'.");
        }

        limit = Math.Clamp(limit, 1, 50);

        var completedGamesQuery = dbContext.Games
            .AsNoTracking()
            .Where(x => x.HomeScore.HasValue && x.AwayScore.HasValue && x.HomeScore != x.AwayScore);

        var summary = new EloDashboardSummary(
            selectedRuleset,
            await completedGamesQuery.CountAsync(cancellationToken),
            await completedGamesQuery.CountAsync(
                x => !dbContext.RatingHistories.Any(history =>
                    history.GameId == x.Id &&
                    history.RulesetVersion == selectedRuleset),
                cancellationToken),
            await dbContext.TeamRatings
                .AsNoTracking()
                .CountAsync(x => x.RulesetVersion == selectedRuleset, cancellationToken),
            await completedGamesQuery.MaxAsync(x => (DateTime?)x.GameDateTimeUtc, cancellationToken),
            await dbContext.EloRebuildRuns
                .AsNoTracking()
                .Where(x =>
                    x.RulesetVersion == selectedRuleset &&
                    x.Status == EloRebuildRunStatus.Completed)
                .MaxAsync(x => x.FinishedAtUtc, cancellationToken),
            await dbContext.EloRebuildRuns
                .AsNoTracking()
                .Where(x => x.RulesetVersion == selectedRuleset)
                .MaxAsync(x => (DateTime?)x.QueuedAtUtc, cancellationToken));

        var runs = await dbContext.EloRebuildRuns
            .AsNoTracking()
            .OrderByDescending(x => x.QueuedAtUtc)
            .Take(limit)
            .Select(x => new EloRebuildRunDto(
                x.Id,
                x.RulesetVersion,
                x.Status,
                x.GamesProcessed,
                x.TeamsRated,
                x.QueuedAtUtc,
                x.StartedAtUtc,
                x.FinishedAtUtc,
                x.FromGameDateTimeUtc,
                x.Notes))
            .ToListAsync(cancellationToken);

        return Ok(new EloDashboardResponse(
            BuildRulesetCatalog(),
            summary,
            runs));
    }

    [HttpPost("rebuilds")]
    public async Task<ActionResult<IReadOnlyList<EloRebuildRunDto>>> Rebuild(
        [FromBody] EloRebuildRequest? request,
        CancellationToken cancellationToken)
    {
        var requestedRuleset = request?.RulesetVersion;
        IReadOnlyList<string> rulesets;
        if (string.IsNullOrWhiteSpace(requestedRuleset) ||
            string.Equals(requestedRuleset, "all", StringComparison.OrdinalIgnoreCase))
        {
            rulesets = EloRulesetVersions.All;
        }
        else
        {
            var normalized = requestedRuleset.Trim().ToLowerInvariant();
            if (!EloRulesetVersions.All.Contains(normalized))
            {
                return BadRequest($"Unsupported ELO ruleset '{requestedRuleset}'.");
            }

            rulesets = [normalized];
        }

        var activeRulesets = await dbContext.EloRebuildRuns
            .Where(x => rulesets.Contains(x.RulesetVersion) &&
                (x.Status == EloRebuildRunStatus.Pending || x.Status == EloRebuildRunStatus.Running))
            .Select(x => x.RulesetVersion)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (activeRulesets.Count > 0)
        {
            return Conflict($"An ELO rebuild is already queued or running for: {string.Join(", ", activeRulesets)}.");
        }

        var queuedAtUtc = DateTime.UtcNow;
        var runs = rulesets.Select(ruleset => new EloRebuildRun
        {
            Id = Guid.NewGuid(),
            RulesetVersion = ruleset,
            Status = EloRebuildRunStatus.Pending,
            QueuedAtUtc = queuedAtUtc,
            CreatedAtUtc = queuedAtUtc
        }).ToList();

        dbContext.EloRebuildRuns.AddRange(runs);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Conflict($"An ELO rebuild is already queued or running for: {string.Join(", ", rulesets)}.");
        }

        return Accepted(runs.Select(ToDto).ToList());
    }

    [HttpPost("rebuilds/{runId:guid}/cancel")]
    public async Task<ActionResult<EloRebuildRunDto>> CancelRebuild(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.EloRebuildRuns.SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        if (run.Status != EloRebuildRunStatus.Pending)
        {
            return Conflict($"Only pending rebuilds can be canceled. Run '{runId}' is {run.Status}.");
        }

        run.Status = EloRebuildRunStatus.Canceled;
        run.FinishedAtUtc = DateTime.UtcNow;
        run.Notes = "Canceled by an internal admin operator before the worker started it.";
        await dbContext.SaveChangesAsync(cancellationToken);
        await PublishNotificationAsync(run, cancellationToken);

        return Ok(ToDto(run));
    }

    [HttpPost("rebuilds/{runId:guid}/retry")]
    public async Task<ActionResult<EloRebuildRunDto>> RetryRebuild(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var sourceRun = await dbContext.EloRebuildRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (sourceRun is null)
        {
            return NotFound();
        }

        if (sourceRun.Status is not (EloRebuildRunStatus.Failed or EloRebuildRunStatus.Canceled))
        {
            return Conflict($"Only failed or canceled rebuilds can be retried. Run '{runId}' is {sourceRun.Status}.");
        }

        var activeExists = await dbContext.EloRebuildRuns.AnyAsync(x =>
            x.RulesetVersion == sourceRun.RulesetVersion &&
            (x.Status == EloRebuildRunStatus.Pending || x.Status == EloRebuildRunStatus.Running),
            cancellationToken);
        if (activeExists)
        {
            return Conflict($"An ELO rebuild is already queued or running for: {sourceRun.RulesetVersion}.");
        }

        var queuedAtUtc = DateTime.UtcNow;
        var retryRun = new EloRebuildRun
        {
            Id = Guid.NewGuid(),
            RulesetVersion = sourceRun.RulesetVersion,
            Status = EloRebuildRunStatus.Pending,
            QueuedAtUtc = queuedAtUtc,
            CreatedAtUtc = queuedAtUtc,
            Notes = $"Retry queued from rebuild run {sourceRun.Id}."
        };

        dbContext.EloRebuildRuns.Add(retryRun);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Conflict($"An ELO rebuild is already queued or running for: {sourceRun.RulesetVersion}.");
        }

        return Accepted(ToDto(retryRun));
    }

    private static EloRulesetCatalogResponse BuildRulesetCatalog()
        => new(EloRulesetVersions.Default, EloRulesetVersions.All);

    private static string? ResolveRulesetOrDefault(string? rulesetVersion)
    {
        if (string.IsNullOrWhiteSpace(rulesetVersion))
        {
            return EloRulesetVersions.Default;
        }

        var normalized = rulesetVersion.Trim().ToLowerInvariant();
        return EloRulesetVersions.All.Contains(normalized) ? normalized : null;
    }

    private static EloRebuildRunDto ToDto(EloRebuildRun run)
        => new(
            run.Id,
            run.RulesetVersion,
            run.Status,
            run.GamesProcessed,
            run.TeamsRated,
            run.QueuedAtUtc,
            run.StartedAtUtc,
            run.FinishedAtUtc,
            run.FromGameDateTimeUtc,
            run.Notes);

    private Task PublishNotificationAsync(EloRebuildRun run, CancellationToken cancellationToken)
        => notificationPublisher.PublishAsync(
            new EloRebuildRunNotification(
                run.Id,
                run.RulesetVersion,
                run.Status,
                DateTime.UtcNow),
            cancellationToken);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
