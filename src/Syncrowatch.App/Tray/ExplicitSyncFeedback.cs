using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;

namespace Syncrowatch.App.Tray;

public sealed class ExplicitSyncFeedback(ISyncEventBus bus)
{
    public void Settled(Result<SyncCycleReport> outcome)
    {
        if (outcome is not Result<SyncCycleReport>.Ok ok)
        {
            return;
        }

        var report = ok.Value;
        if (report.Pulled is null && report.Pushed is null)
        {
            return;
        }

        bus.Publish(new SyncNotification.ExplicitSyncCompleted(DateTimeOffset.UtcNow, report));
    }
}
