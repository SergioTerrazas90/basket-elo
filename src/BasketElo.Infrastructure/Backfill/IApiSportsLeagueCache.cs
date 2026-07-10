using BasketElo.Domain.Backfill;

namespace BasketElo.Infrastructure.Backfill;

public interface IApiSportsLeagueCache
{
    bool TryGet(string country, string leagueName, out BasketballProviderLeague? league);

    void Set(string country, string leagueName, BasketballProviderLeague? league);
}
