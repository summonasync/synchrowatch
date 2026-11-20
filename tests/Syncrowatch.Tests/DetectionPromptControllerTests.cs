using Shouldly;
using Syncrowatch.App.Tray;
using Syncrowatch.Core;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class DetectionPromptControllerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly SyncEventBus _bus = new();
    private readonly FakePrompt _prompt = new();
    private readonly List<Exception> _errors = [];
    private readonly DetectionPromptController _controller;
    private int _syncCount;

    public DetectionPromptControllerTests()
    {
        _controller = new DetectionPromptController(_bus, _prompt, SyncNowAsync, error => _errors.Add(error));
    }

    [Fact]
    public void DetectionWhileIdle_PresentsPromptWithTheChangedPaths()
    {
        _bus.Publish(new SyncNotification.ChangesDetected(Now, ["notes.txt"]));

        _prompt.Requests.ShouldHaveSingleItem().ShouldBe(["notes.txt"]);
    }

    [Fact]
    public void DetectionWhilePromptOpen_AppliesOncePerWindow()
    {
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _prompt.Enqueue(reply.Task);

        _bus.Publish(new SyncNotification.ChangesDetected(Now, ["a.txt"]));
        _bus.Publish(new SyncNotification.ChangesDetected(Now, ["b.txt"]));

        _prompt.Requests.ShouldHaveSingleItem();
        reply.TrySetResult(false);
    }

    [Fact]
    public async Task SyncNowDecision_RunsTheFullSync()
    {
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _prompt.Enqueue(reply.Task);
        _bus.Publish(new SyncNotification.ChangesDetected(Now, ["notes.txt"]));

        reply.TrySetResult(true);
        await WaitUntilAsync(() => _syncCount == 1);

        _syncCount.ShouldBe(1);
        _errors.ShouldBeEmpty();
    }

    [Fact]
    public async Task LaterDecision_PerformsNoSyncAndFreesTheWindow()
    {
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _prompt.Enqueue(reply.Task);
        _bus.Publish(new SyncNotification.ChangesDetected(Now, ["notes.txt"]));
        reply.TrySetResult(false);

        for (var attempt = 0; attempt < 500 && _prompt.Requests.Count == 1; attempt++)
        {
            _bus.Publish(new SyncNotification.ChangesDetected(Now, ["again.txt"]));
            await Task.Delay(10);
        }

        _prompt.Requests.Count.ShouldBe(2);
        _prompt.Requests[^1].ShouldBe(["again.txt"]);
        _syncCount.ShouldBe(0);
        _errors.ShouldBeEmpty();
    }

    [Fact]
    public void DetectionWhilePushPending_StaysSilentUntilThePendingPushClears()
    {
        _bus.Publish(new SyncNotification.PushPending(new PendingPush("main", "shaHead", Now)));

        _bus.Publish(new SyncNotification.ChangesDetected(Now, ["notes.txt"]));
        _prompt.Requests.ShouldBeEmpty();

        _bus.Publish(new SyncNotification.SyncCompleted(Now, Quiet()));
        _bus.Publish(new SyncNotification.ChangesDetected(Now, ["notes.txt"]));

        _prompt.Requests.ShouldHaveSingleItem().ShouldBe(["notes.txt"]);
    }

    [Fact]
    public void LaterFollowedByStagedPush_QuietsEveryFurtherDetection()
    {
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _prompt.Enqueue(reply.Task);
        _bus.Publish(new SyncNotification.ChangesDetected(Now, ["notes.txt"]));
        reply.TrySetResult(false);

        _bus.Publish(new SyncNotification.PushPending(new PendingPush("main", "shaHead", Now)));
        _bus.Publish(new SyncNotification.ChangesDetected(Now, ["more.txt"]));

        _prompt.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public void Dispose_StopsListeningToTheBus()
    {
        _controller.Dispose();

        _bus.Publish(new SyncNotification.ChangesDetected(Now, ["notes.txt"]));

        _prompt.Requests.ShouldBeEmpty();
    }

    private async Task SyncNowAsync()
    {
        Interlocked.Increment(ref _syncCount);
        await Task.CompletedTask;
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

    public void Dispose() => _controller.Dispose();

    private sealed class FakePrompt : IDetectionPrompt
    {
        private readonly Queue<Task<bool>> _replies = new();

        public List<IReadOnlyList<string>> Requests { get; } = [];

        public void Enqueue(Task<bool> reply) => _replies.Enqueue(reply);

        public Task<bool> RequestAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
        {
            Requests.Add(paths);
            return _replies.Count > 0 ? _replies.Dequeue() : Task.FromResult(false);
        }
    }
}
