using Syncrowatch.App.Tray;

namespace Syncrowatch.App.Notifications;

public sealed class TrayStateTrackerUpdater(TrayStateTracker tracker) : ITrayStateUpdater
{
    private readonly TrayStateTracker _tracker = tracker;

    public void Update(TraySyncState state, string tooltip) => _tracker.SetState(state switch
    {
        TraySyncState.Idle => TrayState.Idle,
        TraySyncState.Syncing => TrayState.Syncing,
        TraySyncState.Conflict => TrayState.Conflict,
        _ => TrayState.Error
    });
}
