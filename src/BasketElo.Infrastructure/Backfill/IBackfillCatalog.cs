using BasketElo.Domain.Backfill;

namespace BasketElo.Infrastructure.Backfill;

public interface IBackfillCatalog
{
    IReadOnlyCollection<ConfiguredBackfillLeague> GetLeagues();
    IReadOnlyCollection<string> GetSeasonsForLeague(ConfiguredBackfillLeague league);
}
