using Shouldly;
using Synchro.Core;
using Syncrowatch.App.Tray;
using Syncrowatch.Core;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class TrayStateTrackerTests : IDisposable
{
    private readonly SyncEventBus _bus = new();
    private readonly TrayStateTracker _tracker;

    public TrayStateTrackerTests()
    {
        _tracker = new TrayStateTracker(_bus);
    }

    [Fact]
    public void FreshTracker_StartsIdle()
    {
        _tracker.State.ShouldBe(TrayState.Idle);
        _tracker.ToolTipText.ShouldBe("Syncrowatch — idle");
    }

    [Fact]
    public void BeginSync_ShowsSyncingUntilTheBusSettlesIt()
    {
        _tracker.BeginSync();

        _tracker.State.ShouldBe(TrayState.Syncing);
        _tracker.ToolTipText.ShouldBe("Syncrowatch — syncing");

        _bus.Publish(new SyncNotification.SyncCompleted(DateTimeOffset.UtcNow, Quiet()));
        _tracker.State.ShouldBe(TrayState.Idle);
    }

    [Fact]
    public void SettleSync_FailingOutcomeWithoutConflict_LandsInError()
    {
        _tracker.BeginSync();

        _tracker.SettleSync(new Result<SyncCycleReport>.Fail(new SyncError("unreachable", "could not resolve host")));

        _tracker.State.ShouldBe(TrayState.Error);
        _tracker.ToolTipText.ShouldContain("error");
    }

    [Fact]
    public void SettleSync_ConflictOutcome_LandsInConflict()
    {
        _tracker.SettleSync(new Result<SyncCycleReport>.Fail(SyncError.Conflict("merge conflicted over notes.txt")));

        _tracker.State.ShouldBe(TrayState.Conflict);
        _tracker.ToolTipText.ShouldBe("Syncrowatch — merge conflict");
    }

    [Fact]
    public void BusConflictEvent_SwitchesStateWithoutAnyExplicitSync()
    {
        _bus.Publish(new SyncNotification.MergeConflicted(DateTimeOffset.UtcNow, "main", ["notes.txt"]));

        _tracker.State.ShouldBe(TrayState.Conflict);
    }

    [Fact]
    public void BusFailureEvent_LandsInError()
    {
        _bus.Publish(new SyncNotification.SyncFailed(DateTimeOffset.UtcNow, new SyncError("dirty_tree", "uncommitted changes")));

        _tracker.State.ShouldBe(TrayState.Error);
    }

    [Fact]
    public void BusAutoMergedEvent_SettlesBackToIdle()
    {
        _bus.Publish(new SyncNotification.AutoMerged(DateTimeOffset.UtcNow, "main", ["notes.txt"]));

        _tracker.State.ShouldBe(TrayState.Idle);
    }

    [Fact]
    public void PushPending_SurfacesInTooltipAndClearsOnCompletion()
    {
        _bus.Publish(new SyncNotification.PushPending(new PendingPush("main", "shaHead", DateTimeOffset.UtcNow)));

        _tracker.PushPending.ShouldBeTrue();
        _tracker.ToolTipText.ShouldBe("Syncrowatch — idle, push pending confirmation");

        _bus.Publish(new SyncNotification.SyncCompleted(DateTimeOffset.UtcNow, Quiet()));
        _tracker.PushPending.ShouldBeFalse();
        _tracker.ToolTipText.ShouldNotContain("pending");
    }

    [Fact]
    public void SetPaused_OverlaysTheBaseState()
    {
        _tracker.SetPaused(true);
        _tracker.ToolTipText.ShouldBe("Syncrowatch — idle, paused");

        _bus.Publish(new SyncNotification.PushPending(new PendingPush("main", "shaHead", DateTimeOffset.UtcNow)));
        _tracker.ToolTipText.ShouldBe("Syncrowatch — idle, paused, push pending confirmation");

        _tracker.SetPaused(false);
        _tracker.ToolTipText.ShouldBe("Syncrowatch — idle, push pending confirmation");
    }

    private static SyncCycleReport Quiet() => new(DateTimeOffset.UtcNow, null, null);

    public void Dispose() => _tracker.Dispose();
}
