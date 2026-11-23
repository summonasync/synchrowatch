using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.App.Tray;
using Syncrowatch.App.Wizard;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class SyncNowRunnerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly SyncEventBus _bus = new();
    private readonly List<SyncNotification> _notifications = [];
    private readonly TrayStateTracker _tracker;
    private readonly SyncNowRunner _runner;

    public SyncNowRunnerTests()
    {
        _bus.Received += _notifications.Add;
        _tracker = new TrayStateTracker(_bus);
        _runner = new SyncNowRunner(_tracker, new ExplicitSyncFeedback(_bus));
    }

    [Fact]
    public async Task RunAsync_PushOutcome_SettlesTheTrayAndAnnouncesTheCompletion()
    {
        Result<SyncCycleReport> pushed = new Result<SyncCycleReport>.Ok(new SyncCycleReport(Now, null, new SyncOutcome("main", "a", "b")));

        var outcome = await _runner.RunAsync(() => Task.FromResult(pushed));

        outcome.ShouldBe(pushed);
        _tracker.State.ShouldBe(TrayState.Idle);
        _notifications.OfType<SyncNotification.ExplicitSyncCompleted>().ShouldHaveSingleItem()
            .Report.Pushed.ShouldNotBeNull().ToSha.ShouldBe("b");
    }

    [Fact]
    public async Task RunAsync_PullOutcome_AnnouncesTheCompletionToo()
    {
        Result<SyncCycleReport> pulled = new Result<SyncCycleReport>.Ok(new SyncCycleReport(Now, new SyncOutcome("main", "a", "b"), null));

        await _runner.RunAsync(() => Task.FromResult(pulled));

        _notifications.OfType<SyncNotification.ExplicitSyncCompleted>().ShouldHaveSingleItem()
            .Report.Pulled.ShouldNotBeNull();
    }

    [Fact]
    public async Task RunAsync_QuietOutcome_SettlesWithoutAnnouncing()
    {
        Result<SyncCycleReport> quiet = new Result<SyncCycleReport>.Ok(new SyncCycleReport(Now, null, null));

        await _runner.RunAsync(() => Task.FromResult(quiet));

        _tracker.State.ShouldBe(TrayState.Idle);
        _notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_FailedOutcome_LandsInErrorWithoutAnnouncing()
    {
        Result<SyncCycleReport> failed = new Result<SyncCycleReport>.Fail(new SyncError("unreachable", "could not resolve host"));

        await _runner.RunAsync(() => Task.FromResult(failed));

        _tracker.State.ShouldBe(TrayState.Error);
        _notifications.OfType<SyncNotification.ExplicitSyncCompleted>().ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_ConflictOutcome_LandsInConflict()
    {
        Result<SyncCycleReport> conflicted = new Result<SyncCycleReport>.Fail(SyncError.Conflict("merge conflicted over notes.txt"));

        await _runner.RunAsync(() => Task.FromResult(conflicted));

        _tracker.State.ShouldBe(TrayState.Conflict);
        _notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_CycleThrow_SettlesTheTrayAndLetsTheErrorThrough()
    {
        await Should.ThrowAsync<InvalidOperationException>(
            () => _runner.RunAsync(() => throw new InvalidOperationException("cycle blew up")));

        _tracker.State.ShouldBe(TrayState.Error);
        _tracker.ToolTipText.ShouldNotContain("Synchronizing");
        _notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_UnderTheCommandDoor_ThrowReachesTheErrorSinkAndTraySettles()
    {
        var errors = new List<Exception>();
        var command = new AsyncCommand(
            () => _runner.RunAsync(() => throw new InvalidOperationException("cycle blew up")),
            onError: errors.Add);

        await command.ExecuteAsync(null);

        errors.ShouldHaveSingleItem().ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("cycle blew up");
        _tracker.State.ShouldBe(TrayState.Error);
        _tracker.State.ShouldNotBe(TrayState.Syncing);
    }

    public void Dispose() => _tracker.Dispose();
}
