using BasketElo.Domain.CurrentResults;

namespace BasketElo.Infrastructure.CurrentResults;

public interface ICurrentResultsProvider
{
    string Source { get; }
    Task<CurrentResultFetchResult> FetchAsync(DateOnly date, CancellationToken cancellationToken);
}
