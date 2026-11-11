using System.Reflection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class SyncLoopTests : IDisposable
{
    private readonly string _historyDirectory = TempDir.Create();
    private readonly JsonSyncEventStore _store;
    private readonly SyncEventBus _bus = new();
    private readonly List<SyncNotification> _notifications = [];

    public SyncLoopTests()
    {
        _store = new JsonSyncEventStore(_historyDirectory);
        _bus.Received += _notifications.Add;
    }

    [Fact]
    public async Task SyncOnce_UnchangedRemote_CompletesQuietlyWithoutPushOrNotifications()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha1", "sha1")));
        using var loop = BuildLoop(remote, CleanLocal());

        var outcome = await loop.SyncOnce(CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        ok.Value.Pulled.ShouldBeNull();
        ok.Value.Pushed.ShouldBeNull();
        await remote.DidNotReceive().Push(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await remote.DidNotReceive().MergeFetchedHead(Arg.Any<CancellationToken>());
        _notifications.ShouldBeEmpty();
        (await RecordedEvents()).ShouldBeEmpty();
    }

    [Fact]
    public async Task SyncOnce_FastForwardPull_RecordsCommitInEvent()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "shaA", "shaB")));
        using var loop = BuildLoop(remote, CleanLocal());

        var outcome = await loop.SyncOnce(CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        ok.Value.Pulled.ShouldBe(new SyncOutcome("main", "shaA", "shaB"));
        var events = await RecordedEvents();
        events.Select(@event => @event.Kind).ShouldBe([SyncEventKind.CommitIn]);
        events[0].Detail.ShouldBe("shaA..shaB");
    }

    [Fact]
    public async Task SyncOnce_DivergentPull_AutoMergesThenPushes()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Fail(SyncError.Conflict("not possible to fast-forward, aborting")));
        remote.MergeFetchedHead(Arg.Any<CancellationToken>())
            .Returns(new Result<MergeOutcome>.Ok(new MergeOutcome.Merged("shaLocal", "shaMerge", ["notes.txt"])));
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote", "shaMerge")));
        using var loop = BuildLoop(remote, CleanLocal());

        var outcome = await loop.SyncOnce(CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        ok.Value.Pulled.ShouldBe(new SyncOutcome("main", "shaLocal", "shaMerge"));
        ok.Value.Pushed.ShouldNotBeNull().ToSha.ShouldBe("shaMerge");
        Received.InOrder(() =>
        {
            remote.Pull("main", Arg.Any<CancellationToken>());
            remote.MergeFetchedHead(Arg.Any<CancellationToken>());
            remote.Push("main", Arg.Any<CancellationToken>());
        });

        var events = await RecordedEvents();
        events.Select(@event => @event.Kind).ShouldBe([SyncEventKind.CommitOut, SyncEventKind.AutoMerged]);
        events[0].Detail.ShouldBe("pushed shaMerge");
        events[1].Paths.ShouldBe(["notes.txt"]);
        events[1].Detail.ShouldBe("merge commit shaMerge");
        var notifications = _notifications.Select(notification => notification.GetType()).ToList();
        notifications.ShouldBe(
            [typeof(SyncNotification.AutoMerged), typeof(SyncNotification.SyncCompleted)]);
        _notifications.OfType<SyncNotification.AutoMerged>().Single().Files.ShouldBe(["notes.txt"]);
    }

    [Fact]
    public async Task SyncOnce_ConflictedMerge_StopsWithoutPushAndCarriesPaths()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Fail(SyncError.Conflict("not possible to fast-forward, aborting")));
        remote.MergeFetchedHead(Arg.Any<CancellationToken>())
            .Returns(new Result<MergeOutcome>.Ok(new MergeOutcome.Conflicted(["notes.txt", "docs/spec.md"])));
        using var loop = BuildLoop(remote, CleanLocal());

        var outcome = await loop.SyncOnce(CancellationToken.None);

        var fail = outcome.ShouldBeOfType<Result<SyncCycleReport>.Fail>();
        fail.Error.Code.ShouldBe("conflict");
        fail.Error.Message.ShouldContain("notes.txt");
        fail.Error.Message.ShouldContain("docs/spec.md");
        await remote.DidNotReceive().Push(Arg.Any<string>(), Arg.Any<CancellationToken>());

        var events = await RecordedEvents();
        events.Select(@event => @event.Kind).ShouldBe([SyncEventKind.MergeConflicted]);
        events[0].Paths.ShouldBe(["notes.txt", "docs/spec.md"]);
        _notifications.Single().ShouldBeOfType<SyncNotification.MergeConflicted>()
            .ConflictedPaths.ShouldBe(["notes.txt", "docs/spec.md"]);
    }

    [Fact]
    public async Task SyncOnce_UnreachableRemote_FailsWithoutMergeAttempt()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Fail(new SyncError("unreachable", "could not resolve host")));
        using var loop = BuildLoop(remote, CleanLocal());

        var outcome = await loop.SyncOnce(CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncCycleReport>.Fail>().Error.Code.ShouldBe("unreachable");
        await remote.DidNotReceive().MergeFetchedHead(Arg.Any<CancellationToken>());
        (await RecordedEvents()).Single().Kind.ShouldBe(SyncEventKind.SyncFailed);
        _notifications.Single().ShouldBeOfType<SyncNotification.SyncFailed>().Error.Code.ShouldBe("unreachable");
    }

    [Fact]
    public async Task SyncOnce_DirtyWorkingTree_RefusesBeforeAnyPull()
    {
        var remote = Substitute.For<IRemoteRepository>();
        var local = Substitute.For<ILocalGit>();
        local.IsWorkingTreeEmpty(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<bool>.Ok(false));
        using var loop = BuildLoop(remote, local);

        var outcome = await loop.SyncOnce(CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncCycleReport>.Fail>().Error.Code.ShouldBe("dirty_tree");
        await remote.DidNotReceive().Pull(Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await RecordedEvents()).Single().Kind.ShouldBe(SyncEventKind.SyncFailed);
    }

    [Fact]
    public async Task SyncOnce_PendingBatch_CommitsBeforePull()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote", "remote")));
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote", "shaLocal")));
        var local = CleanLocal();
        using var loop = BuildLoop(remote, local);
        loop.AcceptBatch(["notes.txt"]);

        var outcome = await loop.SyncOnce(CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncCycleReport>.Ok>().Value.Pushed.ShouldNotBeNull().ToSha.ShouldBe("shaLocal");
        Received.InOrder(() =>
        {
            local.CommitPaths(
                Arg.Any<string>(), Arg.Any<CommitIdentity>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
            remote.Pull("main", Arg.Any<CancellationToken>());
        });
        (await RecordedEvents()).Select(@event => @event.Kind).ShouldBe([SyncEventKind.CommitOut]);
    }

    [Fact]
    public async Task SyncOnce_BatchCommitFailure_StopsBeforePull()
    {
        var remote = Substitute.For<IRemoteRepository>();
        var local = CleanLocal();
        local.CommitPaths(
                Arg.Any<string>(), Arg.Any<CommitIdentity>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Result<CommitOutcome>.Fail(SyncError.Conflict("commit collapsed")));
        using var loop = BuildLoop(remote, local);
        loop.AcceptBatch(["notes.txt"]);

        var outcome = await loop.SyncOnce(CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncCycleReport>.Fail>().Error.Code.ShouldBe("conflict");
        await remote.DidNotReceive().Pull(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncOnce_PreCancelledToken_RetainsPendingBatchUntouched()
    {
        var remote = Substitute.For<IRemoteRepository>();
        var local = CleanLocal();
        using var loop = BuildLoop(remote, local);
        loop.AcceptBatch(["half-done.txt"]);
        using var canceller = new CancellationTokenSource();
        canceller.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => loop.SyncOnce(canceller.Token));

        loop.PendingBatchCount.ShouldBe(1);
        await local.DidNotReceive().CommitPaths(
            Arg.Any<string>(), Arg.Any<CommitIdentity>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        (await RecordedEvents()).ShouldBeEmpty();
    }

    [Fact]
    public async Task SyncOnce_WhileCycleRunning_ReturnsBusyWithoutSideEffects()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            entered.TrySetResult();
            return GatedOutcome(release);
        });
        using var loop = BuildLoop(remote, CleanLocal());
        var first = FireCycle(loop);
        await entered.Task;

        var second = await loop.SyncOnce(CancellationToken.None);

        second.ShouldBeOfType<Result<SyncCycleReport>.Fail>().Error.Code.ShouldBe("busy");
        release.TrySetResult();
        (await first).ShouldBeOfType<Result<SyncCycleReport>.Ok>();
    }

    [Fact]
    public async Task GetStatus_ReportsIdleBeforeStartAndLastErrorAfterFailedCycle()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Fail(new SyncError("unreachable", "could not resolve host")));
        using var loop = BuildLoop(remote, CleanLocal());

        var before = await loop.GetStatus(CancellationToken.None);

        var idle = before.ShouldBeOfType<Result<EngineStatus>.Ok>().Value;
        idle.Running.ShouldBeFalse();
        idle.LastCycleUtc.ShouldBeNull();
        idle.LastError.ShouldBeNull();

        await loop.SyncOnce(CancellationToken.None);
        var after = await loop.GetStatus(CancellationToken.None);

        var failed = after.ShouldBeOfType<Result<EngineStatus>.Ok>().Value;
        failed.Running.ShouldBeFalse();
        failed.LastCycleUtc.ShouldNotBeNull();
        failed.LastError.ShouldNotBeNull().Code.ShouldBe("unreachable");
    }

    [Fact]
    public void Start_FlagsEngineRunningUntilDisposed()
    {
        var remote = Substitute.For<IRemoteRepository>();
        using var loop = BuildLoop(remote, CleanLocal());

        loop.IsRunning.ShouldBeFalse();
        loop.Start();
        loop.IsRunning.ShouldBeTrue();
        loop.Dispose();
        loop.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task Start_PollIntervalTicks_RunCyclesAndRearm()
    {
        var time = new FakeTimeProvider();
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha1", "sha1")));
        using var loop = BuildLoop(remote, CleanLocal(), time);
        loop.Start();

        time.Advance(TimeSpan.FromSeconds(60));
        await AwaitCyclesAsync(remote, 1);

        time.RequestedDueTimes.ShouldContain(TimeSpan.FromSeconds(60));
        time.Advance(TimeSpan.FromSeconds(60));
        await AwaitCyclesAsync(remote, 2);
        await remote.Received(2).Pull("main", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_CancelledToken_StopsTimerWithoutPendingWork()
    {
        var time = new FakeTimeProvider();
        var remote = Substitute.For<IRemoteRepository>();
        using var canceller = new CancellationTokenSource();
        using var loop = BuildLoop(remote, CleanLocal(), time);
        loop.Start(canceller.Token);

        canceller.Cancel();
        time.Advance(TimeSpan.FromSeconds(180));
        await Task.Delay(50);

        loop.IsRunning.ShouldBeFalse();
        await remote.DidNotReceive().Pull(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private SyncLoop BuildLoop(IRemoteRepository remote, ILocalGit local, TimeProvider? timeProvider = null) =>
        new(Config(), remote, local, _store, _bus, timeProvider);

    private static SyncConfig Config() => new()
    {
        WatchedFolder = Path.Combine(Path.GetTempPath(), "syncloop-sample"),
        RemoteUrl = "https://github.com/acme/private.git",
        Branch = "main",
        Mode = SyncMode.Git
    };

    private static ILocalGit CleanLocal()
    {
        var local = Substitute.For<ILocalGit>();
        local.IsWorkingTreeEmpty(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<bool>.Ok(true));
        local.StagePaths(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Result<StageOutcome>.Ok(new StageOutcome([])));
        local.CommitPaths(
                Arg.Any<string>(), Arg.Any<CommitIdentity>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Result<CommitOutcome>.Ok(new CommitOutcome([], "shaLocal")));
        return local;
    }

    private async Task<IReadOnlyList<SyncEvent>> RecordedEvents()
    {
        var outcome = await _store.Query(new HistoryQuery { Limit = 100 }, CancellationToken.None);
        return outcome.ShouldBeOfType<Result<IReadOnlyList<SyncEvent>>.Ok>().Value;
    }

    private static async Task AwaitCyclesAsync(IRemoteRepository remote, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (PullCount(remote) < expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        PullCount(remote).ShouldBeGreaterThanOrEqualTo(expected);
    }

    private static int PullCount(IRemoteRepository remote) =>
        remote.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IRemoteRepository.Pull));

    private static Task<Result<SyncCycleReport>> FireCycle(SyncLoop loop) => loop.SyncOnce(CancellationToken.None);

    private static async Task<Result<SyncOutcome>> GatedOutcome(TaskCompletionSource release)
    {
        await release.Task;
        return new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha1", "sha1"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_historyDirectory, true);
        }
        catch (IOException)
        {
        }
    }
}
