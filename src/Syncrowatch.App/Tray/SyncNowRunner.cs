using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Engine;

namespace Syncrowatch.App.Tray;

public sealed class SyncNowRunner(TrayStateTracker tracker, ExplicitSyncFeedback feedback)
{
    public async Task<Result<SyncCycleReport>> RunAsync(Func<Task<Result<SyncCycleReport>>> cycle)
    {
        tracker.BeginSync();
        try
        {
            var outcome = await cycle().ConfigureAwait(true);
            tracker.SettleSync(outcome);
            feedback.Settled(outcome);
            return outcome;
        }
        finally
        {
            if (tracker.State == TrayState.Syncing)
            {
                tracker.SetState(TrayState.Error);
            }
        }
    }
}
