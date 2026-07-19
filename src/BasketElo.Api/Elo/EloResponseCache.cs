using System.Collections.Concurrent;
using BasketElo.Domain.Elo;
using Microsoft.Extensions.Caching.Memory;

namespace BasketElo.Api.Elo;

public sealed class EloResponseCache(
    IMemoryCache cache,
    ILogger<EloResponseCache> logger)
{
    private readonly ConcurrentDictionary<string, CacheScope> trackedKeys = new(StringComparer.Ordinal);

    public bool TryGet<T>(string key, out T? value)
    {
        if (cache.TryGetValue(key, out value))
        {
            logger.LogDebug("ELO response cache hit for {cacheKey}.", key);
            return true;
        }

        logger.LogDebug("ELO response cache miss for {cacheKey}.", key);
        value = default;
        return false;
    }

    public void Set<T>(string key, T value, string poolKey, string rulesetVersion)
    {
        cache.Set(
            key,
            value,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15),
                SlidingExpiration = TimeSpan.FromMinutes(5)
            });

        trackedKeys[key] = new CacheScope(poolKey, rulesetVersion);
        logger.LogDebug("Stored ELO response in cache under {cacheKey}.", key);
    }

    public void Invalidate(EloRebuildRunNotification notification)
    {
        if (!string.Equals(notification.Status, EloRebuildRunStatus.Completed, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(notification.EloPoolKey))
        {
            return;
        }

        var poolKey = EloPoolKeys.Normalize(notification.EloPoolKey);
        var rulesetVersion = notification.RulesetVersion.Trim().ToLowerInvariant();
        var removed = 0;

        foreach (var entry in trackedKeys)
        {
            if (!string.Equals(entry.Value.PoolKey, poolKey, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(entry.Value.RulesetVersion, rulesetVersion, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (trackedKeys.TryRemove(entry.Key, out _))
            {
                cache.Remove(entry.Key);
                removed++;
            }
        }

        cache.Remove($"elo:ranking-filter-options:{poolKey}:{rulesetVersion}");
        logger.LogInformation(
            "Invalidated {responseCount} cached ELO response(s) and ranking filter options for pool {poolKey}, ruleset {rulesetVersion} after rebuild {runId}.",
            removed,
            poolKey,
            rulesetVersion,
            notification.RunId);
    }

    public static string RankingsKey(
        string poolKey,
        string rulesetVersion,
        string teamScope,
        string? country,
        string? competition,
        string? season,
        DateTime? fromUtc,
        DateTime? toUtc,
        DateTime? asOfDate,
        int minGames,
        string? team,
        int page,
        int pageSize)
        => $"elo:rankings:{poolKey}:{rulesetVersion}:{teamScope}:{Normalize(country)}:{Normalize(competition)}:{Normalize(season)}:{FormatDate(fromUtc)}:{FormatDate(toUtc)}:{FormatDate(asOfDate)}:{minGames}:{Normalize(team)}:{page}:{pageSize}";

    public static string EvolutionKey(
        string poolKey,
        string rulesetVersion,
        IEnumerable<Guid> teamIds,
        string? competition,
        string? season,
        DateTime? fromUtc,
        DateTime? toUtc,
        int pointsPerTeam)
        => $"elo:evolution:{poolKey}:{rulesetVersion}:{string.Join(',', teamIds)}:{Normalize(competition)}:{Normalize(season)}:{FormatDate(fromUtc)}:{FormatDate(toUtc)}:{pointsPerTeam}";

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string FormatDate(DateTime? value) => value?.ToUniversalTime().ToString("O") ?? string.Empty;

    private sealed record CacheScope(string PoolKey, string RulesetVersion);
}
