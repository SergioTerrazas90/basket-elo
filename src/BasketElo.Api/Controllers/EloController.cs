using BasketElo.Api.Auth;
using BasketElo.Api.Elo;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Elo;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using NpgsqlTypes;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/elo")]
public class EloController(
    BasketEloDbContext dbContext,
    IEloRebuildNotificationPublisher notificationPublisher,
    IIdentityHealthCheckService identityHealthCheckService,
    IMemoryCache cache,
    EloResponseCache responseCache) : ControllerBase
{
    [HttpGet("rulesets")]
    public ActionResult<EloRulesetCatalogResponse> GetRulesets()
    {
        return Ok(BuildRulesetCatalog());
    }

    [HttpGet("pools")]
    public async Task<ActionResult<EloPoolCatalogResponse>> GetPools(CancellationToken cancellationToken)
    {
        var options = new List<EloPoolOption>();
        foreach (var descriptor in EloPoolCatalog.All.OrderBy(x => x.DisplayOrder))
        {
            var ratings = dbContext.TeamRatings.AsNoTracking()
                .Where(x => x.EloPoolKey == descriptor.Key && x.RulesetVersion == EloRulesetVersions.Default);
            options.Add(new EloPoolOption(
                descriptor.Key,
                descriptor.DisplayName,
                await ratings.AnyAsync(cancellationToken),
                await ratings.CountAsync(cancellationToken),
                await ratings.Select(x => x.LastGame!.GameDateTimeUtc).MaxAsync(x => (DateTime?)x, cancellationToken),
                await dbContext.EloRebuildRuns.AsNoTracking()
                    .Where(x => x.EloPoolKey == descriptor.Key &&
                        x.RulesetVersion == EloRulesetVersions.Default &&
                        x.Status == EloRebuildRunStatus.Completed)
                    .MaxAsync(x => x.FinishedAtUtc, cancellationToken)));
        }

        return Ok(new EloPoolCatalogResponse(EloPoolKeys.Default, options));
    }

    [HttpGet("browse")]
    public async Task<ActionResult<EloBrowseResponse>> GetBrowse(
        [FromQuery] string? pool,
        [FromQuery] string? country,
        [FromQuery] string? competition,
        [FromQuery] string? season,
        [FromQuery] int? teamLimit,
        CancellationToken cancellationToken = default)
    {
        var poolKey = ResolvePoolOrDefault(pool);
        if (poolKey is null)
        {
            return BadRequest($"Unsupported ELO pool '{pool}'.");
        }

        var cacheKey = EloResponseCache.BrowseKey(poolKey, country, competition, season, teamLimit);
        if (responseCache.TryGet<EloBrowseResponse>(cacheKey, out var cachedResponse) && cachedResponse is not null)
        {
            return Ok(cachedResponse);
        }

        var uncachedResult = await GetBrowseUncached(
            poolKey,
            country,
            competition,
            season,
            teamLimit,
            cancellationToken);
        if (uncachedResult.Result is OkObjectResult { Value: EloBrowseResponse response })
        {
            responseCache.Set(cacheKey, response, poolKey, EloRulesetVersions.Default);
        }

        return uncachedResult;
    }

    private async Task<ActionResult<EloBrowseResponse>> GetBrowseUncached(
        string poolKey,
        string? country,
        string? competition,
        string? season,
        int? teamLimit,
        CancellationToken cancellationToken)
    {
