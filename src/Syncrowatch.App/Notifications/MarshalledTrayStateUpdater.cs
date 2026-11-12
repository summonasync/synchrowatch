using Avalonia.Threading;

namespace Syncrowatch.App.Notifications;

public sealed class MarshalledTrayStateUpdater(ITrayStateUpdater inner) : ITrayStateUpdater
{
    private readonly ITrayStateUpdater _inner = inner;

    public void Update(TraySyncState state, string tooltip) =>
        Dispatcher.UIThread.Post(() => _inner.Update(state, tooltip));
}
