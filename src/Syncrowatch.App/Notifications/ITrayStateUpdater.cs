namespace Syncrowatch.App.Notifications;

public interface ITrayStateUpdater
{
    void Update(TraySyncState state, string tooltip);
}
