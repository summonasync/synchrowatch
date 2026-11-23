using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.App.Tray;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class ExplicitSyncFeedbackTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PushOutcome_PublishesExplicitCompletion()
    {
        var bus = new SyncEventBus();
        var published = new List<SyncNotification>();
        bus.Received += published.Add;
        var feedback = new ExplicitSyncFeedback(bus);
        var report = new SyncCycleReport(Now, null, new SyncOutcome("main", "a", "b"));

        feedback.Settled(new Result<SyncCycleReport>.Ok(report));

        published.OfType<SyncNotification.ExplicitSyncCompleted>().ShouldHaveSingleItem().Report.ShouldBe(report);
    }

    [Fact]
    public void PullOutcome_PublishesExplicitCompletion()
    {
        var bus = new SyncEventBus();
        var published = new List<SyncNotification>();
        bus.Received += published.Add;
        var feedback = new ExplicitSyncFeedback(bus);

        feedback.Settled(new Result<SyncCycleReport>.Ok(new SyncCycleReport(Now, new SyncOutcome("main", "a", "b"), null)));

        published.OfType<SyncNotification.ExplicitSyncCompleted>().ShouldHaveSingleItem();
    }

    [Fact]
    public void QuietOutcome_StaysSilent()
    {
        var bus = new SyncEventBus();
        var published = new List<SyncNotification>();
        bus.Received += published.Add;
        var feedback = new ExplicitSyncFeedback(bus);

        feedback.Settled(new Result<SyncCycleReport>.Ok(new SyncCycleReport(Now, null, null)));

        published.ShouldBeEmpty();
    }

    [Fact]
    public void FailedOutcome_StaysSilent()
    {
        var bus = new SyncEventBus();
        var published = new List<SyncNotification>();
        bus.Received += published.Add;
        var feedback = new ExplicitSyncFeedback(bus);

        feedback.Settled(new Result<SyncCycleReport>.Fail(new SyncError("unreachable", "could not resolve host")));

        published.ShouldBeEmpty();
    }
}
