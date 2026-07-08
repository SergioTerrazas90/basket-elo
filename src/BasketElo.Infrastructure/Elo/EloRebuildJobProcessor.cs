using BasketElo.Domain.Elo;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BasketElo.Infrastructure.Elo;

public sealed class EloRebuildJobProcessor(
    BasketEloDbContext dbContext,
    IEloRebuildService rebuildService,
    ILogger<EloRebuildJobProcessor> logger) : IEloRebuildJobProcessor
{
    public async Task<bool> TryProcessNextPendingJobAsync(CancellationToken cancellationToken)
    {
        var runId = await dbContext.EloRebuildRuns
            .Where(x => x.Status == EloRebuildRunStatus.Pending)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!runId.HasValue)
        {
            return false;
        }

        var startedAtUtc = DateTime.UtcNow;
        var claimed = await dbContext.EloRebuildRuns
            .Where(x => x.Id == runId.Value && x.Status == EloRebuildRunStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, EloRebuildRunStatus.Running)
                .SetProperty(x => x.StartedAtUtc, startedAtUtc), cancellationToken);

        if (claimed == 0)
        {
            return true;
        }

        logger.LogInformation("Processing ELO rebuild run {runId}.", runId.Value);
        await rebuildService.RebuildAsync(runId.Value, cancellationToken);
        return true;
    }
}
