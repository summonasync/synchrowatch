using Syncrowatch.Core.History;

namespace Syncrowatch.App.Notifications;

public sealed class TrayStateWatcher : IDisposable
{
    private readonly ISyncEventBus _bus;
    private readonly ITrayStateUpdater _updater;

    public TrayStateWatcher(ISyncEventBus bus, ITrayStateUpdater updater)
    {
        _bus = bus;
        _updater = updater;
        _bus.Received += OnNotification;
    }

    public void Dispose() => _bus.Received -= OnNotification;

    private void OnNotification(SyncNotification notification)
    {
        var state = notification switch
        {
            SyncNotification.SyncCompleted or SyncNotification.AutoMerged => TraySyncState.Idle,
            SyncNotification.MergeConflicted => TraySyncState.Conflict,
            SyncNotification.SyncFailed => TraySyncState.Error,
            _ => (TraySyncState?)null
        };

        if (state is { } value)
        {
            _updater.Update(value, Tooltip(value));
        }
    }

    private static string Tooltip(TraySyncState state) => state switch
    {
        TraySyncState.Idle => "Syncrowatch — up to date",
        TraySyncState.Syncing => "Syncrowatch — syncing…",
        TraySyncState.Conflict => "Syncrowatch — conflict needs attention",
        _ => "Syncrowatch — last sync failed"
    };
}
