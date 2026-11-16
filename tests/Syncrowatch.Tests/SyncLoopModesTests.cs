using System.Reflection;
using NSubstitute;
using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class SyncLoopModesTests : IDisposable
{
    private readonly string _historyDirectory = TempDir.Create();
    private readonly JsonSyncEventStore _store;
    private readonly SyncEventBus _bus = new();
    private readonly List<SyncNotification> _notifications = [];
    private readonly object _notificationGate = new();

    public SyncLoopModesTests()
    {
        _store = new JsonSyncEventStore(_historyDirectory);
        _bus.Received += notification =>
        {
            lock (_notificationGate)
            {
                _notifications.Add(notification);
            }
        };
    }

    [Fact]
    public void Start_ManualConfig_NeverArmsThePollTimer()
    {
        var time = new FakeTimeProvider();
        using var loop = BuildLoop(CleanLocal(), time: time, mode: SyncRunMode.Manual);

        loop.Start();

        time.RequestedDueTimes.ShouldBe([Timeout.InfiniteTimeSpan]);
    }

    [Fact]
    public void Start_AutoConfig_ArmsThePollTimerAtTheConfiguredInterval()
    {
        var time = new FakeTimeProvider();
        using var loop = BuildLoop(CleanLocal(), time: time, mode: SyncRunMode.Auto);

        loop.Start();

        time.RequestedDueTimes[^1].ShouldBe(SyncConfig.DefaultPollInterval);
    }

    [Fact]
    public void SetMode_Manual_DisarmsPolling_BackToAuto_Rearms()
    {
        var time = new FakeTimeProvider();
        using var loop = BuildLoop(CleanLocal(), time: time, mode: SyncRunMode.Auto);
        loop.Start();

        loop.SetMode(SyncRunMode.Manual);
        time.RequestedDueTimes[^1].ShouldBe(Timeout.InfiniteTimeSpan);

        loop.SetMode(SyncRunMode.Auto);
        time.RequestedDueTimes[^1].ShouldBe(SyncConfig.DefaultPollInterval);
    }

    [Fact]
    public async Task Pause_StopsPollingCycles_Resume_RestartsThem()
    {
        var time = new FakeTimeProvider();
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0")));
        using var loop = BuildLoop(CleanLocal(), time: time, remote: remote);
        loop.Start();

        loop.Pause();
        time.Advance(SyncConfig.DefaultPollInterval * 3);
        await Task.Delay(100);
        PullCount(remote).ShouldBe(0);

        loop.Resume();
        time.Advance(SyncConfig.DefaultPollInterval);
        await AwaitAsync(() => PullCount(remote) >= 1);
    }

    [Fact]
    public async Task Tick_LandingAfterPause_DoesNotRearmOrCycle_ResumeRestarts()
    {
        var time = new FakeTimeProvider();
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0")));
        using var loop = BuildLoop(CleanLocal(), time: time, remote: remote);
        loop.Start();

        loop.Pause();
        time.FireQueuedCallbacks();
        await Task.Delay(100);
        PullCount(remote).ShouldBe(0);
        time.RequestedDueTimes[^1].ShouldBe(Timeout.InfiniteTimeSpan);

        loop.Resume();
        time.Advance(SyncConfig.DefaultPollInterval);
        await AwaitAsync(() => PullCount(remote) >= 1);
    }

    [Fact]
    public async Task Tick_LandingAfterManualSwitch_DoesNotRearm_SyncNowStillWorks()
    {
        var time = new FakeTimeProvider();
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0")));
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote-sha", "shaHead")));
        using var loop = BuildLoop(CleanLocal(), time: time, remote: remote);
        loop.Start();

        loop.SetMode(SyncRunMode.Manual);
        time.FireQueuedCallbacks();
        await Task.Delay(100);
        PullCount(remote).ShouldBe(0);
        time.RequestedDueTimes[^1].ShouldBe(Timeout.InfiniteTimeSpan);

        var outcome = await loop.SyncOnce(CancellationToken.None);
        outcome.ShouldBeOfType<Result<SyncCycleReport>.Ok>().Value.Pushed.ShouldNotBeNull();
    }

    [Fact]
    public async Task Tick_SemiAuto_PendingPushFreezesQueuedEdits_ExplicitSyncReleasesTheCommit()
    {
        var remote = Substitute.For<IRemoteRepository>();
        var unchanged = new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0"));
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(unchanged, unchanged, unchanged);
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote-sha", "shaHead")));
        var local = CleanLocal();
        local.IsWorkingTreeEmpty(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new Result<bool>.Ok(true),
                new Result<bool>.Ok(false),
                new Result<bool>.Ok(true));
        var time = new FakeTimeProvider();
        using var loop = BuildLoop(local, time: time, remote: remote, mode: SyncRunMode.SemiAuto);

        await StagePendingPushAsync(loop, time);
        loop.AcceptBatch(["more.txt"]);

        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>()
            .Value.Pushed.ShouldNotBeNull().ToSha.ShouldBe("shaHead");

        await remote.Received(2).Pull(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await remote.DidNotReceive().MergeFetchedHead(Arg.Any<CancellationToken>());
        await local.DidNotReceive().CommitPaths(
            Arg.Any<string>(), Arg.Any<CommitIdentity>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        SnapshotNotifications().OfType<SyncNotification.SyncFailed>().ShouldBeEmpty();

        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        await local.Received(1).CommitPaths(
            Arg.Any<string>(), Arg.Any<CommitIdentity>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncOnce_SemiAuto_DirtyTreeRefusingMerge_SkipsMergeQuietlyWhilePending()
    {
        var remote = Substitute.For<IRemoteRepository>();
        var unchanged = new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0"));
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(
                unchanged,
                new Result<SyncOutcome>.Fail(SyncError.Conflict("not possible to fast-forward, aborting")));
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote-sha", "shaHead")));
        var local = CleanLocal();
        local.IsWorkingTreeEmpty(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new Result<bool>.Ok(true),
                new Result<bool>.Ok(false),
                new Result<bool>.Ok(false));
        var time = new FakeTimeProvider();
        using var loop = BuildLoop(local, time: time, remote: remote, mode: SyncRunMode.SemiAuto);

        await StagePendingPushAsync(loop, time);
        loop.AcceptBatch(["more.txt"]);

        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        await remote.Received(2).Pull(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await remote.DidNotReceive().MergeFetchedHead(Arg.Any<CancellationToken>());
        SnapshotNotifications().OfType<SyncNotification.SyncFailed>().ShouldBeEmpty();
        await local.DidNotReceive().CommitPaths(
            Arg.Any<string>(), Arg.Any<CommitIdentity>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_SemiAuto_PullRefusesOverlappingPaths_TypedFailureKeepsPendingAndResumes()
    {
        var remote = Substitute.For<IRemoteRepository>();
        var unchanged = new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0"));
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(
                unchanged,
                new Result<SyncOutcome>.Fail(new SyncError("local_changes", "your local changes would be overwritten by merge")),
                unchanged);
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote-sha", "shaHead")));
        var local = CleanLocal();
        var time = new FakeTimeProvider();
        using var loop = BuildLoop(local, time: time, remote: remote, mode: SyncRunMode.SemiAuto);

        await StagePendingPushAsync(loop, time);
        loop.AcceptBatch(["notes.txt"]);

        var outcome = await loop.SyncOnce(CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncCycleReport>.Fail>().Error.Code.ShouldBe("local_changes");
        await remote.DidNotReceive().MergeFetchedHead(Arg.Any<CancellationToken>());
        SnapshotNotifications().OfType<SyncNotification.SyncFailed>().ShouldHaveSingleItem();
        (await RecordedEvents()).Select(@event => @event.Kind).ShouldContain(SyncEventKind.SyncFailed);
        await local.DidNotReceive().CommitPaths(
            Arg.Any<string>(), Arg.Any<CommitIdentity>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());

        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>()
            .Value.Pushed.ShouldNotBeNull();
        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        await local.Received(1).CommitPaths(
            Arg.Any<string>(), Arg.Any<CommitIdentity>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncOnce_ExplicitCycle_CompletingAStagedPush_RefreshesEngineStatus()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0")));
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote-sha", "shaHead")));
        var time = new FakeTimeProvider();
        using var loop = BuildLoop(CleanLocal(), time: time, remote: remote, mode: SyncRunMode.SemiAuto);
        await StagePendingPushAsync(loop, time);

        (await loop.GetStatus(CancellationToken.None)).ShouldBeOfType<Result<EngineStatus>.Ok>()
            .Value.LastCycleUtc.ShouldBeNull();

        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>()
            .Value.Pushed.ShouldNotBeNull();

        var status = (await loop.GetStatus(CancellationToken.None)).ShouldBeOfType<Result<EngineStatus>.Ok>().Value;
        status.LastCycleUtc.ShouldNotBeNull();
        status.LastError.ShouldBeNull();
    }

    [Fact]
    public async Task SyncOnce_SemiAuto_ExplicitCycle_CompletesTheStagedPushInOnePress()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0")));
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote-sha", "shaHead")));
        var local = CleanLocal();
        var time = new FakeTimeProvider();
        using var loop = BuildLoop(local, time: time, remote: remote, mode: SyncRunMode.SemiAuto);
        await StagePendingPushAsync(loop, time);

        var outcome = await loop.SyncOnce(CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncCycleReport>.Ok>().Value.Pushed.ShouldNotBeNull()
            .ToSha.ShouldBe("shaHead");
        await remote.Received(1).Push(Arg.Any<string>(), Arg.Any<CancellationToken>());
        SnapshotNotifications().OfType<SyncNotification.PushPending>().ShouldHaveSingleItem();
        SnapshotNotifications().OfType<SyncNotification.SyncCompleted>().ShouldHaveSingleItem();
        (await RecordedEvents()).Select(@event => @event.Kind).ShouldBe([SyncEventKind.CommitOut]);

        loop.AcceptBatch(["late.txt"]);
        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        await local.Received(1).CommitPaths(
            Arg.Any<string>(), Arg.Any<CommitIdentity>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_SemiAuto_AheadOfRemote_HoldsPushAndAnnouncesPending()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0")));
        var time = new FakeTimeProvider();
        using var loop = BuildLoop(CleanLocal(), time: time, remote: remote, mode: SyncRunMode.SemiAuto);

        await StagePendingPushAsync(loop, time);

        SnapshotNotifications().ShouldHaveSingleItem().ShouldBeOfType<SyncNotification.PushPending>()
            .Push.Sha.ShouldBe("shaHead");
        await remote.DidNotReceive().Push(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncOnce_SemiAuto_UpToDate_StaysQuiet()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0")));
        using var loop = BuildLoop(CleanLocal(), remote: remote, mode: SyncRunMode.SemiAuto, remoteTip: "shaHead");

        var outcome = await loop.SyncOnce(CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncCycleReport>.Ok>().Value.Pushed.ShouldBeNull();
        SnapshotNotifications().ShouldBeEmpty();
        await remote.DidNotReceive().Push(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncOnce_SemiAuto_PushFails_KeepsPendingAliveForRetry()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0")));
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(
                new Result<SyncOutcome>.Fail(new SyncError("rejected", "non-fast-forward")),
                new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote-sha", "shaHead")));
        var time = new FakeTimeProvider();
        using var loop = BuildLoop(CleanLocal(), time: time, remote: remote, mode: SyncRunMode.SemiAuto);
        await StagePendingPushAsync(loop, time);

        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Fail>();
        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>()
            .Value.Pushed.ShouldNotBeNull();

        await remote.Received(2).Push(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_SemiAuto_PendingPushFreezesLocalCommits_ButKeepsPulling()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "shaRemote", "shaRemote")));
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote-sha", "shaHead")));
        var time = new FakeTimeProvider();
        var local = CleanLocal();
        using var loop = BuildLoop(local, time: time, remote: remote, mode: SyncRunMode.SemiAuto);

        await StagePendingPushAsync(loop, time);
        loop.AcceptBatch(["more.txt"]);

        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        await local.DidNotReceive().CommitPaths(
            Arg.Any<string>(), Arg.Any<CommitIdentity>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await remote.Received(2).Pull(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetMode_AwayFromSemiAuto_DropsThePendingPush()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0")));
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote-sha", "shaHead")));
        var local = CleanLocal();
        var time = new FakeTimeProvider();
        using var loop = BuildLoop(local, time: time, remote: remote, mode: SyncRunMode.SemiAuto);
        await StagePendingPushAsync(loop, time);

        loop.SetMode(SyncRunMode.Auto);
        loop.AcceptBatch(["late.txt"]);
        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        await local.Received(1).CommitPaths(
            Arg.Any<string>(), Arg.Any<CommitIdentity>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await remote.Received(1).Push(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Restart_SemiAuto_NextAutoCycleRestagesAndReannounces()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0")));
        var time = new FakeTimeProvider();
        var first = BuildLoop(CleanLocal(), time: time, remote: remote, mode: SyncRunMode.SemiAuto);
        await StagePendingPushAsync(first, time);
        first.Dispose();

        lock (_notificationGate)
        {
            _notifications.Clear();
        }

        var second = BuildLoop(CleanLocal(), time: time, remote: remote, mode: SyncRunMode.SemiAuto);
        await StagePendingPushAsync(second, time);

        SnapshotNotifications().OfType<SyncNotification.PushPending>().ShouldHaveSingleItem()
            .Push.Sha.ShouldBe("shaHead");
        second.Dispose();
    }

    [Fact]
    public async Task SyncOnce_ManualMode_ExplicitCommandRunsTheFullCycle()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0")));
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote-sha", "shaHead")));
        using var loop = BuildLoop(CleanLocal(), remote: remote, mode: SyncRunMode.Manual);
        loop.AcceptBatch(["notes.txt"]);

        var outcome = await loop.SyncOnce(CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncCycleReport>.Ok>().Value.Pushed.ShouldNotBeNull().ToSha.ShouldBe("shaHead");
    }

    private async Task StagePendingPushAsync(SyncLoop loop, FakeTimeProvider time)
    {
        loop.Start();
        time.Advance(SyncConfig.DefaultPollInterval);
        await AwaitAsync(() => SnapshotNotifications().OfType<SyncNotification.PushPending>().Any());
    }

    private SyncLoop BuildLoop(
        ILocalGit local,
        FakeTimeProvider? time = null,
        IRemoteRepository? remote = null,
        SyncRunMode mode = SyncRunMode.Auto,
        string remoteTip = "remote-sha")
    {
        var remoteImplementation = remote ?? Substitute.For<IRemoteRepository>();
        if (remote is null)
        {
            remoteImplementation.Pull("main", Arg.Any<CancellationToken>())
                .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0")));
        }

        remoteImplementation.TrackingTip("main", Arg.Any<CancellationToken>())
            .Returns(new Result<string>.Ok(remoteTip));
        return new SyncLoop(Sample(mode), remoteImplementation, local, _store, _bus, time);
    }

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
        local.HeadInfo(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<LocalHead>.Ok(new LocalHead("main", "shaHead")));
        return local;
    }

    private SyncConfig Sample(SyncRunMode mode) => new()
    {
        WatchedFolder = Path.Combine(_historyDirectory, "watched"),
        RemoteUrl = "https://github.com/acme/private.git",
        Branch = "main",
        Mode = mode
    };

    private async Task<IReadOnlyList<SyncEvent>> RecordedEvents()
    {
        var outcome = await _store.Query(new HistoryQuery { Limit = 100 }, CancellationToken.None);
        return outcome.ShouldBeOfType<Result<IReadOnlyList<SyncEvent>>.Ok>().Value;
    }

    private IReadOnlyList<SyncNotification> SnapshotNotifications()
    {
        lock (_notificationGate)
        {
            return [.. _notifications];
        }
    }

    private static async Task AwaitAsync(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        ready().ShouldBeTrue();
    }

    private static int PullCount(IRemoteRepository remote) =>
        remote.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IRemoteRepository.Pull));

    private static int PushCount(IRemoteRepository remote) =>
        remote.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IRemoteRepository.Push));

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
