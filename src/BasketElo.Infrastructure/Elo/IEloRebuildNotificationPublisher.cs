using BasketElo.Domain.Elo;

namespace BasketElo.Infrastructure.Elo;

public interface IEloRebuildNotificationPublisher
{
    Task PublishAsync(EloRebuildRunNotification notification, CancellationToken cancellationToken);
}
