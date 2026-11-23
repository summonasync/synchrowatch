using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.App.Notifications;
using Syncrowatch.Core.History;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class TrayStateWatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FailedEvent_MarksTrayAsError()
    {
        var harness = new Harness();

        harness.Bus.Publish(new SyncNotification.SyncFailed(Now, new SyncError("poll", "fetch refused")));

        var update = harness.Updates.ShouldHaveSingleItem();
        update.State.ShouldBe(TraySyncState.Error);
        update.Tooltip.ShouldContain("failed");
    }

    [Fact]
    public void ConflictedEvent_MarksTrayAsConflict()
    {
        var harness = new Harness();

        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["doc.md"]));

        harness.Updates.ShouldHaveSingleItem().State.ShouldBe(TraySyncState.Conflict);
    }

    [Fact]
    public void CompletedAndMergedEvents_MarkTrayAsIdle()
    {
        var harness = new Harness();

        harness.Bus.Publish(new SyncNotification.SyncCompleted(
            Now, new SyncCycleReport(Now, new SyncOutcome("main", "a", "b"), null)));
        harness.Bus.Publish(new SyncNotification.AutoMerged(Now, "main", ["a.txt"]));

        harness.Updates.Select(update => update.State).ShouldBe([TraySyncState.Idle, TraySyncState.Idle]);
    }

    [Fact]
    public void RelayPackageEvent_LeavesTrayStateAlone()
    {
        var harness = new Harness();

        harness.Bus.Publish(new SyncNotification.RelayPackageDetected(
            Now, "alpha", "fix-pack.synchr", "synchro apply \"alpha/fix-pack.synchr\" --peer \"Pavel\""));

        harness.Updates.ShouldBeEmpty();
    }

    [Fact]
    public void RelayWarningEvents_LeaveTrayStateAlone()
    {
        var harness = new Harness();

        harness.Bus.Publish(new SyncNotification.RelayEntriesSkipped(Now, 2));
        harness.Bus.Publish(new SyncNotification.RelayStateCorrupted(Now));

        harness.Updates.ShouldBeEmpty();
    }

    [Fact]
    public void Dispose_UnsubscribesFromTheBus()
    {
        var harness = new Harness();
        harness.Watcher.Dispose();

        harness.Bus.Publish(new SyncNotification.SyncFailed(Now, new SyncError("poll", "fetch refused")));

        harness.Updates.ShouldBeEmpty();
    }

    private sealed class Harness
    {
        public Harness()
        {
            Updater = new RecordingUpdater();
            Watcher = new TrayStateWatcher(Bus, Updater);
        }

        public SyncEventBus Bus { get; } = new();

        public RecordingUpdater Updater { get; }

        public TrayStateWatcher Watcher { get; }

        public List<(TraySyncState State, string Tooltip)> Updates => Updater.Updates;
    }

    private sealed class RecordingUpdater : ITrayStateUpdater
    {
        public List<(TraySyncState State, string Tooltip)> Updates { get; } = [];

        public void Update(TraySyncState state, string tooltip) => Updates.Add((state, tooltip));
    }
}
