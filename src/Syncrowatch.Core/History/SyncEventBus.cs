namespace Syncrowatch.Core.History;

public sealed class SyncEventBus : ISyncEventBus
{
    private readonly object _gate = new();

    public event Action<SyncNotification>? Received;

    public void Publish(SyncNotification notification)
    {
        Action<SyncNotification>? handlers;
        lock (_gate)
        {
            handlers = Received;
        }

        if (handlers is null)
        {
            return;
        }

        foreach (Action<SyncNotification> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(notification);
            }
            catch (Exception)
            {
            }
        }
    }
}
