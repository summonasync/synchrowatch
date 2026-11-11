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

    public SyncLoopModesTests()
    {
        _store = new JsonSyncEventStore(_historyDirectory);
        _bus.Received += _notifications.Add;
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
    public async Task SyncOnce_SemiAuto_AheadOfRemote_HoldsPushAndAnnouncesPending()
    {
        var remote = Substitute.For<IRemoteRepository>();
        using var loop = BuildLoop(CleanLocal(), remote: remote, mode: SyncRunMode.SemiAuto);

        var outcome = await loop.SyncOnce(CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        ok.Value.Pushed.ShouldBeNull();
        await remote.DidNotReceive().Push(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _notifications.ShouldHaveSingleItem().ShouldBeOfType<SyncNotification.PushPending>()
            .Push.Sha.ShouldBe("shaHead");
    }

    [Fact]
    public async Task SyncOnce_SemiAuto_UpToDate_StaysQuiet()
    {
        var remote = Substitute.For<IRemoteRepository>();
        using var loop = BuildLoop(CleanLocal(), remote: remote, mode: SyncRunMode.SemiAuto, remoteTip: "shaHead");

        var outcome = await loop.SyncOnce(CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncCycleReport>.Ok>().Value.Pushed.ShouldBeNull();
        _notifications.ShouldBeEmpty();
        await remote.DidNotReceive().Push(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmPendingPush_ReleasesExactlyTheStagedBatch_Once()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote-sha", "shaHead")));
        using var loop = BuildLoop(CleanLocal(), remote: remote, mode: SyncRunMode.SemiAuto);
        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        var confirmed = await loop.ConfirmPendingPush(CancellationToken.None);

        confirmed.ShouldBeOfType<Result<SyncOutcome>.Ok>().Value.ToSha.ShouldBe("shaHead");
        _notifications.OfType<SyncNotification.PushPending>().ShouldHaveSingleItem();
        _notifications.OfType<SyncNotification.SyncCompleted>().ShouldHaveSingleItem();

        var repeat = await loop.ConfirmPendingPush(CancellationToken.None);

        repeat.ShouldBeOfType<Result<SyncOutcome>.Fail>().Error.Code.ShouldBe("no_pending_push");
        await remote.Received(1).Push(Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await RecordedEvents()).Select(@event => @event.Kind).ShouldBe([SyncEventKind.CommitOut]);
    }

    [Fact]
    public async Task ConfirmPendingPush_WhenPushFails_KeepsPendingAliveForRetry()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(
                new Result<SyncOutcome>.Fail(new SyncError("rejected", "non-fast-forward")),
                new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote-sha", "shaHead")));
        using var loop = BuildLoop(CleanLocal(), remote: remote, mode: SyncRunMode.SemiAuto);
        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        (await loop.ConfirmPendingPush(CancellationToken.None)).ShouldBeOfType<Result<SyncOutcome>.Fail>();
        (await loop.ConfirmPendingPush(CancellationToken.None)).ShouldBeOfType<Result<SyncOutcome>.Ok>();

        await remote.Received(2).Push(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncOnce_SemiAuto_PendingPushFreezesLocalCommits_ButKeepsPulling()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "shaRemote", "shaRemote")));
        var local = CleanLocal();
        using var loop = BuildLoop(local, remote: remote, mode: SyncRunMode.SemiAuto);
        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        loop.AcceptBatch(["more.txt"]);

        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        await local.DidNotReceive().CommitPaths(
            Arg.Any<string>(), Arg.Any<CommitIdentity>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await remote.Received(2).Pull(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await remote.DidNotReceive().Push(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetMode_AwayFromSemiAuto_DropsThePendingPush()
    {
        var remote = Substitute.For<IRemoteRepository>();
        using var loop = BuildLoop(CleanLocal(), remote: remote, mode: SyncRunMode.SemiAuto);
        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        loop.SetMode(SyncRunMode.Auto);

        var outcome = await loop.ConfirmPendingPush(CancellationToken.None);
        outcome.ShouldBeOfType<Result<SyncOutcome>.Fail>().Error.Code.ShouldBe("no_pending_push");
        await remote.DidNotReceive().Push(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SemiAuto_ConfirmationSeamAccepts_PushesWithoutManualConfirm()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote-sha", "shaHead")));
        var confirmation = Substitute.For<IPushConfirmation>();
        confirmation.RequestAsync(Arg.Any<PendingPush>(), Arg.Any<CancellationToken>()).Returns(true);
        using var loop = BuildLoop(CleanLocal(), remote: remote, mode: SyncRunMode.SemiAuto, confirmation: confirmation);

        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        await AwaitAsync(() => PushCount(remote) >= 1);

        await remote.Received(1).Push(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _notifications.OfType<SyncNotification.SyncCompleted>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task SyncOnce_ManualMode_ExplicitCommandRunsTheFullCycle()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.Push("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "remote-sha", "shaHead")));
        using var loop = BuildLoop(CleanLocal(), remote: remote, mode: SyncRunMode.Manual);
        loop.AcceptBatch(["notes.txt"]);

        var outcome = await loop.SyncOnce(CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncCycleReport>.Ok>().Value.Pushed.ShouldNotBeNull().ToSha.ShouldBe("shaHead");
    }

    private SyncLoop BuildLoop(
        ILocalGit local,
        FakeTimeProvider? time = null,
        IRemoteRepository? remote = null,
        SyncRunMode mode = SyncRunMode.Auto,
        string remoteTip = "remote-sha",
        IPushConfirmation? confirmation = null)
    {
        var remoteImplementation = remote ?? Substitute.For<IRemoteRepository>();
        remoteImplementation.Pull("main", Arg.Any<CancellationToken>())
            .Returns(new Result<SyncOutcome>.Ok(new SyncOutcome("main", "sha0", "sha0")));
        remoteImplementation.TrackingTip("main", Arg.Any<CancellationToken>())
            .Returns(new Result<string>.Ok(remoteTip));
        return new SyncLoop(Sample(mode), remoteImplementation, local, _store, _bus, time, confirmation);
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
