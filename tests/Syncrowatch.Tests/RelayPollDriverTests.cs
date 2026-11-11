using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Relay;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class RelayPollDriverTests : IDisposable
{
    private readonly string _directory = TempDir.Create();

    [Fact]
    public async Task Start_WithSavedConfig_PollsWatcherOnEachInterval()
    {
        var time = new FakeTimeProvider();
        var store = new JsonSyncConfigStore(_directory);
        await store.Save(ValidConfig(), CancellationToken.None);
        var watcher = new FakeRelayWatcher();
        using var driver = new RelayPollDriver(store, _ => watcher, new SyncEventBus(), time);

        driver.Start();
        await WaitForPollAsync(() => watcher.PollCount, time, 1);
        await WaitForPollAsync(() => watcher.PollCount, time, 2);

        watcher.PollCount.ShouldBe(2);
    }

    [Fact]
    public async Task Start_WithoutSavedConfig_NeverTouchesTheWatcher()
    {
        var time = new FakeTimeProvider();
        var watcher = new FakeRelayWatcher();
        using var driver = new RelayPollDriver(
            new JsonSyncConfigStore(_directory), _ => watcher, new SyncEventBus(), time);

        driver.Start();
        time.Advance(SyncConfig.DefaultPollInterval);
        await Task.Delay(250);

        watcher.PollCount.ShouldBe(0);
    }

    [Fact]
    public async Task PollFailure_PublishesRedactedSyncFailedOnTheBus()
    {
        var time = new FakeTimeProvider();
        var store = new JsonSyncConfigStore(_directory);
        await store.Save(ValidConfig(), CancellationToken.None);
        var watcher = new FakeRelayWatcher
        {
            Next = new Result<IReadOnlyList<RelayPackage>>.Fail(
                new SyncError("relay_tree", "fatal: cannot reach https://github.com/acme/private.git"))
        };
        var bus = new SyncEventBus();
        var announced = AnnounceSyncFailed(bus);
        using var driver = new RelayPollDriver(store, _ => watcher, bus, time);

        driver.Start();
        await WaitForPollAsync(() => watcher.PollCount, time, 1);
        var failure = await announced.Task.WaitAsync(TimeSpan.FromSeconds(5));

        failure.Error.Code.ShouldBe("relay_tree");
        failure.Error.Message.ShouldNotContain("https://github.com");
    }

    [Fact]
    public async Task PollException_PublishesRelayPollErrorOnTheBus()
    {
        var time = new FakeTimeProvider();
        var store = new JsonSyncConfigStore(_directory);
        await store.Save(ValidConfig(), CancellationToken.None);
        var watcher = new ThrowingRelayWatcher();
        var bus = new SyncEventBus();
        var announced = AnnounceSyncFailed(bus);
        using var driver = new RelayPollDriver(store, _ => watcher, bus, time);

        driver.Start();
        await WaitForPollAsync(() => watcher.PollCount, time, 1);
        var failure = await announced.Task.WaitAsync(TimeSpan.FromSeconds(5));

        failure.Error.Code.ShouldBe("relay_poll_error");
        failure.Error.Message.ShouldContain("relay transport exploded");
    }

    private static TaskCompletionSource<SyncNotification.SyncFailed> AnnounceSyncFailed(SyncEventBus bus)
    {
        var announced = new TaskCompletionSource<SyncNotification.SyncFailed>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bus.Received += notification =>
        {
            if (notification is SyncNotification.SyncFailed failure)
            {
                announced.TrySetResult(failure);
            }
        };
        return announced;
    }

    private static async Task WaitForPollAsync(Func<int> pollCount, FakeTimeProvider time, int targetCount)
    {
        var budget = TimeSpan.FromSeconds(5);
        while (pollCount() < targetCount)
        {
            budget -= TimeSpan.FromMilliseconds(20);
            if (budget < TimeSpan.Zero)
            {
                throw new TimeoutException($"relay watcher never reached {targetCount} polls");
            }

            time.Advance(SyncConfig.DefaultPollInterval);
            await Task.Delay(20);
        }
    }

    private static SyncConfig ValidConfig() => new()
    {
        WatchedFolder = Path.GetTempPath(),
        RemoteUrl = "https://github.com/acme/private.git",
        Branch = "main",
        Identity = CommitIdentity.Default
    };

    public void Dispose() => Directory.Delete(_directory, true);

    private sealed class FakeRelayWatcher : IRelayWatcher
    {
        public Result<IReadOnlyList<RelayPackage>> Next { get; init; } =
            new Result<IReadOnlyList<RelayPackage>>.Ok([]);

        public int PollCount;

        public Task<Result<IReadOnlyList<RelayPackage>>> Poll(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref PollCount);
            return Task.FromResult(Next);
        }
    }

    private sealed class ThrowingRelayWatcher : IRelayWatcher
    {
        public int PollCount;

        public Task<Result<IReadOnlyList<RelayPackage>>> Poll(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref PollCount);
            throw new InvalidOperationException("relay transport exploded");
        }
    }
}
