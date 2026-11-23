using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.App.Tray;
using Syncrowatch.App.Wizard;
using Syncrowatch.Core.History;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class AsyncCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_RunsTheWork()
    {
        var ran = false;
        var command = new AsyncCommand(() => { ran = true; return Task.CompletedTask; });

        await command.ExecuteAsync(null);

        ran.ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_GuardRefusing_SkipsTheWork()
    {
        var ran = false;
        var command = new AsyncCommand(() => { ran = true; return Task.CompletedTask; }, () => false);

        await command.ExecuteAsync(null);

        ran.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhileRunning_RefusesTheSecondEntry()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var command = new AsyncCommand(async () =>
        {
            count++;
            started.TrySetResult(true);
            await release.Task;
        });

        var first = command.ExecuteAsync(null);
        await started.Task;

        var second = command.ExecuteAsync(null);

        release.TrySetResult(true);
        await first;
        await second;

        count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowingWork_ReportsToTheErrorSink()
    {
        var errors = new List<Exception>();
        var command = new AsyncCommand(() => throw new InvalidOperationException("cycle blew up"), onError: errors.Add);

        await command.ExecuteAsync(null);

        errors.ShouldHaveSingleItem().ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("cycle blew up");
    }

    [Fact]
    public async Task Execute_RunsTheWorkWithoutAwaiting()
    {
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncCommand(async () =>
        {
            await Task.Yield();
            finished.TrySetResult(true);
        });

        command.Execute(null);

        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DialogSyncNow_TraversesTheTrackedDoor_AndSettlesTheTray()
    {
        var bus = new SyncEventBus();
        using var tracker = new TrayStateTracker(bus);
        var prompt = new FakePrompt();
        var cycle = new TaskCompletionSource<Result<SyncCycleReport>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new List<Exception>();
        var command = new AsyncCommand(
            async () =>
            {
                tracker.BeginSync();
                tracker.SettleSync(await cycle.Task);
            },
            () => true,
            errors.Add);
        using var detection = new DetectionPromptController(bus, prompt, () => command.ExecuteAsync(null), errors.Add);

        var accept = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        prompt.Enqueue(accept.Task);
        bus.Publish(new SyncNotification.ChangesDetected(Now, ["notes.txt"]));
        accept.TrySetResult(true);

        await WaitUntilAsync(() => tracker.State == TrayState.Syncing);
        tracker.ToolTipText.ShouldBe("Syncrowatch — Synchronizing…");

        cycle.TrySetResult(new Result<SyncCycleReport>.Ok(Quiet()));
        await WaitUntilAsync(() => tracker.State == TrayState.Idle);
        tracker.ToolTipText.ShouldBe("Syncrowatch — idle");
        errors.ShouldBeEmpty();
    }

    private static SyncCycleReport Quiet() => new(Now, null, null);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        condition().ShouldBeTrue();
    }

    private sealed class FakePrompt : IDetectionPrompt
    {
        private readonly Queue<Task<bool>> _replies = new();

        public void Enqueue(Task<bool> reply) => _replies.Enqueue(reply);

        public Task<bool> RequestAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
        {
            return _replies.Count > 0 ? _replies.Dequeue() : Task.FromResult(false);
        }
    }
}
