namespace Syncrowatch.App.Notifications;

/// <summary>
/// Tray tooltip sink. Update is always invoked on the Avalonia UI thread;
/// the composition layer wraps implementations with
/// <see cref="MarshalledTrayStateUpdater" /> so bus callbacks never touch
/// tray controls off-thread.
/// </summary>
public interface ITrayStateUpdater
{
    void Update(TraySyncState state, string tooltip);
}
