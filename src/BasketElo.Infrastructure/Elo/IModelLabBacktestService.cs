using BasketElo.Domain.Elo;

namespace BasketElo.Infrastructure.Elo;

public interface IModelLabBacktestService
{
    Task<ModelLabOptionsResponse> GetOptionsAsync(CancellationToken cancellationToken);

    Task<ModelLabBacktestResponse> RunAsync(ModelLabBacktestRequest request, CancellationToken cancellationToken);
}
