namespace Syncrowatch.Core.History;

public interface ISyncEventBus
{
    event Action<SyncNotification>? Received;

    void Publish(SyncNotification notification);
}
