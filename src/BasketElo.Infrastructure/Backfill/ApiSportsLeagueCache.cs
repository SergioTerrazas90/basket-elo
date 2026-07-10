using System.Collections.Concurrent;
using BasketElo.Domain.Backfill;

namespace BasketElo.Infrastructure.Backfill;

public class ApiSportsLeagueCache : IApiSportsLeagueCache
{
    private readonly ConcurrentDictionary<string, BasketballProviderLeague?> _leagues = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string country, string leagueName, out BasketballProviderLeague? league)
        => _leagues.TryGetValue(CacheKey(country, leagueName), out league);

    public void Set(string country, string leagueName, BasketballProviderLeague? league)
        => _leagues[CacheKey(country, leagueName)] = league;

    private static string CacheKey(string country, string leagueName)
        => $"{country.Trim()}|{leagueName.Trim()}";
}
