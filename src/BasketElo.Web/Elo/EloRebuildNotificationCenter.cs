using BasketElo.Domain.Elo;

namespace BasketElo.Web.Elo;

public sealed class EloRebuildNotificationCenter(ILogger<EloRebuildNotificationCenter> logger)
{
    private readonly object _lock = new();
    private readonly List<Func<EloRebuildRunNotification, Task>> _handlers = [];

    public IDisposable Subscribe(Func<EloRebuildRunNotification, Task> handler)
    {
        lock (_lock)
        {
            _handlers.Add(handler);
        }

        return new Subscription(this, handler);
    }

    public async Task PublishAsync(EloRebuildRunNotification notification)
    {
        Func<EloRebuildRunNotification, Task>[] handlers;
        lock (_lock)
        {
            handlers = _handlers.ToArray();
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler(notification);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "ELO rebuild notification handler failed for run {runId}.",
                    notification.RunId);
            }
        }
    }

    private void Unsubscribe(Func<EloRebuildRunNotification, Task> handler)
    {
        lock (_lock)
        {
            _handlers.Remove(handler);
        }
    }

    private sealed class Subscription(
        EloRebuildNotificationCenter center,
        Func<EloRebuildRunNotification, Task> handler) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            center.Unsubscribe(handler);
            _disposed = true;
        }
    }
}
