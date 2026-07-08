using BasketElo.Domain.Elo;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/elo")]
public class EloController(
    BasketEloDbContext dbContext) : ControllerBase
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
                .MaxAsync(x => (DateTime?)x.StartedAtUtc, cancellationToken));

        var runs = await dbContext.EloRebuildRuns
            .AsNoTracking()
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(limit)
            .Select(x => new EloRebuildRunDto(
                x.Id,
                x.RulesetVersion,
                x.Status,
                x.GamesProcessed,
                x.TeamsRated,
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
        var runs = rulesets.Select(ruleset => new Domain.Entities.EloRebuildRun
        {
            Id = Guid.NewGuid(),
            RulesetVersion = ruleset,
            Status = EloRebuildRunStatus.Pending,
            StartedAtUtc = queuedAtUtc,
            CreatedAtUtc = queuedAtUtc
        }).ToList();

        dbContext.EloRebuildRuns.AddRange(runs);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = runs.Select(x => new EloRebuildRunDto(
            x.Id, x.RulesetVersion, x.Status, x.GamesProcessed, x.TeamsRated,
            x.StartedAtUtc, x.FinishedAtUtc, x.FromGameDateTimeUtc, x.Notes)).ToList();

        return Accepted(response);
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
}
