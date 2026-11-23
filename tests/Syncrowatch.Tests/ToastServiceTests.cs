using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.App.Notifications;
using Syncrowatch.Core.History;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class ToastServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AutoMergedEvent_ShowsToastWithFileCount()
    {
        var harness = new Harness();
        harness.Bus.Publish(new SyncNotification.AutoMerged(Now, "main", ["a.txt", "b.txt", "c.txt"]));

        var toast = harness.Factory.Presented.ShouldHaveSingleItem();
        toast.Kind.ShouldBe(ToastKind.AutoMerged);
        toast.Title.ShouldBe("Auto-merged");
        toast.Message.ShouldContain("3 files");
        toast.Message.ShouldContain("main");
    }

    [Fact]
    public void ConflictedEvent_ShowsToastWithActionNeeded()
    {
        var harness = new Harness();
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["doc.md", "code.cs"]));

        var toast = harness.Factory.Presented.ShouldHaveSingleItem();
        toast.Kind.ShouldBe(ToastKind.Conflict);
        toast.Title.ShouldBe("Conflict — action needed");
        toast.Message.ShouldContain("2 files");
        toast.Message.ShouldContain("doc.md");
    }

    [Fact]
    public void FailedEvent_ShowsToastWithRedactedReason()
    {
        var harness = new Harness();
        harness.Bus.Publish(new SyncNotification.SyncFailed(
            Now, new SyncError("conflict", "merge conflicted over notes.txt")));

        var toast = harness.Factory.Presented.ShouldHaveSingleItem();
        toast.Kind.ShouldBe(ToastKind.Failure);
        toast.Title.ShouldBe("Sync failed");
        toast.Message.ShouldBe("merge conflicted over notes.txt");
    }

    [Fact]
    public void ExplicitCompletedEvent_ShowsTheSyncCompletedToast()
    {
        var harness = new Harness();
        harness.Bus.Publish(new SyncNotification.ExplicitSyncCompleted(
            Now, new SyncCycleReport(Now, new SyncOutcome("main", "a", "b"), new SyncOutcome("main", "b", "c"))));

        var toast = harness.Factory.Presented.ShouldHaveSingleItem();
        toast.Kind.ShouldBe(ToastKind.SyncCompleted);
        toast.Title.ShouldBe("Sync completed");
        toast.Message.ShouldBe("pulled and pushed on main");
    }

    [Fact]
    public void ExplicitCompletedPushOnlyEvent_NamesJustThePush()
    {
        var harness = new Harness();
        harness.Bus.Publish(new SyncNotification.ExplicitSyncCompleted(
            Now, new SyncCycleReport(Now, null, new SyncOutcome("main", "b", "c"))));

        harness.Factory.Presented.ShouldHaveSingleItem().Message.ShouldBe("pushed on main");
    }

    [Fact]
    public void RelayPackageEvent_ShowsToastWithCliCommand()
    {
        var harness = new Harness();
        harness.Bus.Publish(new SyncNotification.RelayPackageDetected(
            Now, "alpha", "fix-pack.synchr", "synchro apply \"alpha/fix-pack.synchr\" --peer \"Pavel\""));

        var toast = harness.Factory.Presented.ShouldHaveSingleItem();
        toast.Kind.ShouldBe(ToastKind.RelayPackage);
        toast.Title.ShouldContain("alpha");
        toast.Message.ShouldBe("fix-pack.synchr");
        toast.Command.ShouldBe("synchro apply \"alpha/fix-pack.synchr\" --peer \"Pavel\"");
        toast.HasCommand.ShouldBeTrue();
    }

    [Fact]
    public void SkippedRelayEntriesEvent_ShowsWarningToast()
    {
        var harness = new Harness();
        harness.Bus.Publish(new SyncNotification.RelayEntriesSkipped(Now, 3));

        var toast = harness.Factory.Presented.ShouldHaveSingleItem();
        toast.Kind.ShouldBe(ToastKind.RelayWarning);
        toast.Title.ShouldBe("Relay entry skipped");
        toast.Message.ShouldContain("3 malformed relay entries skipped");
        toast.HasCommand.ShouldBeFalse();
    }

    [Fact]
    public void CorruptedRelayStateEvent_ShowsResetToast()
    {
        var harness = new Harness();
        harness.Bus.Publish(new SyncNotification.RelayStateCorrupted(Now));

        var toast = harness.Factory.Presented.ShouldHaveSingleItem();
        toast.Kind.ShouldBe(ToastKind.RelayWarning);
        toast.Title.ShouldBe("Relay state reset");
    }

    [Fact]
    public void SlotAssignment_StaysUniqueAcrossForceCloseAndExpiry()
    {
        var harness = new Harness();
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["one.md"]));
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["two.md"]));
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["three.md"]));
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["four.md"]));

        var surviving = harness.Factory.Presented.Skip(1).ToList();
        surviving.Select(toast => toast.Slot).ShouldBe([0, 1, 2]);
    }

    [Fact]
    public void CompletedEvent_DoesNotProduceToast()
    {
        var harness = new Harness();
        harness.Bus.Publish(new SyncNotification.SyncCompleted(
            Now, new SyncCycleReport(Now, null, new SyncOutcome("main", "a", "b"))));

        harness.Factory.Presented.ShouldBeEmpty();
    }

    [Fact]
    public void RepeatedIdenticalConflict_CoalescesIntoOneToast()
    {
        var harness = new Harness();
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["doc.md"]));
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["doc.md"]));

        harness.Factory.Presented.Count.ShouldBe(1);
    }

    [Fact]
    public void FourthDistinctEvent_ClosesOldestToastAndKeepsCap()
    {
        var harness = new Harness();
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["one.md"]));
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["two.md"]));
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["three.md"]));
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["four.md"]));

        harness.Factory.Presented.Count.ShouldBe(4);
        harness.Factory.Dismissed.Select(toast => toast.Message)
            .ShouldBe(["1 file needs a pick — action needed (one.md)"]);
        harness.Factory.LiveCount.ShouldBe(ToastService.MaxVisible);
    }

    [Fact]
    public void ExpiredToast_FreesCapacityForNewEvents()
    {
        var harness = new Harness();
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["one.md"]));
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["two.md"]));
        harness.Factory.Expire(harness.Factory.Presented[0]);

        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["three.md"]));

        harness.Factory.LiveCount.ShouldBe(2);
        harness.Factory.Presented[^1].Slot.ShouldBe(1);
    }

    [Fact]
    public void Dispose_StopsListeningToTheBus()
    {
        var harness = new Harness();
        harness.Service.Dispose();

        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["doc.md"]));

        harness.Factory.Presented.ShouldBeEmpty();
    }

    [Fact]
    public void Show_PresentsOneOffToastImmediately()
    {
        var harness = new Harness();

        harness.Service.Show(new ToastContent(ToastKind.LinkCopied, "Link copied", "notes.txt"));

        var toast = harness.Factory.Presented.ShouldHaveSingleItem();
        toast.Kind.ShouldBe(ToastKind.LinkCopied);
        toast.Title.ShouldBe("Link copied");
        toast.Message.ShouldBe("notes.txt");
    }

    [Fact]
    public void Show_RepeatsAreNotCoalesced()
    {
        var harness = new Harness();
        var content = new ToastContent(ToastKind.LinkCopied, "Link copied", "notes.txt");

        harness.Service.Show(content);
        harness.Service.Show(content);

        harness.Factory.Presented.Count.ShouldBe(2);
    }

    [Fact]
    public void Show_RespectsTheVisibleCapByDroppingTheOldest()
    {
        var harness = new Harness();
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["one.md"]));
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["two.md"]));
        harness.Bus.Publish(new SyncNotification.MergeConflicted(Now, "main", ["three.md"]));

        harness.Service.Show(new ToastContent(ToastKind.LinkCopied, "Link copied", "notes.txt"));

        harness.Factory.Presented.Count.ShouldBe(4);
        harness.Factory.Dismissed.Select(toast => toast.Message)
            .ShouldBe(["1 file needs a pick — action needed (one.md)"]);
        harness.Factory.LiveCount.ShouldBe(ToastService.MaxVisible);
    }

    private sealed class Harness
    {
        public Harness()
        {
            Factory = new FakeToastWindowFactory();
            Service = new ToastService(Bus, Factory);
        }

        public SyncEventBus Bus { get; } = new();

        public FakeToastWindowFactory Factory { get; }

        public ToastService Service { get; }
    }

    private sealed class FakeToastWindowFactory : IToastWindowFactory
    {
        private readonly List<(ToastViewModel Toast, Action Expired)> _pending = [];

        public List<ToastViewModel> Presented { get; } = [];

        public List<ToastViewModel> Dismissed { get; } = [];

        public int LiveCount => _pending.Count;

        public void Present(ToastViewModel toast, Action expired)
        {
            Presented.Add(toast);
            _pending.Add((toast, expired));
        }

        public void Dismiss(ToastViewModel toast)
        {
            Dismissed.Add(toast);
            Release(toast);
        }

        public void Expire(ToastViewModel toast)
        {
            _pending.First(entry => entry.Toast == toast).Expired();
            Release(toast);
        }

        private void Release(ToastViewModel toast) => _pending.RemoveAll(entry => entry.Toast == toast);
    }
}
