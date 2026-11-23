using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.History;
using Xunit;

namespace Syncrowatch.Tests.E2E;

[Collection("git-config-sandbox")]
public sealed class SemiAutoHoldE2ETests : IDisposable
{
    private const string HeldFile = "report.md";
    private const string IncomingFile = "roster.txt";

    private readonly SyncE2EStand _stand = new();

    [Fact]
    public async Task SemiAuto_PushHeldSilently_SyncNowMergesAndLandsEverything()
    {
        await _stand.BindAsync(_stand.CloneB, SyncRunMode.SemiAuto);
        var time = new FakeTimeProvider();
        using var loopB = await _stand.LoopForAsync(_stand.CloneB, time);
        loopB.Start();

        File.WriteAllText(Path.Combine(_stand.CloneB, HeldFile), "quarterly numbers, draft\n");
        loopB.AcceptBatch([HeldFile]);
        time.Advance(SyncConfig.DefaultPollInterval);
        await SyncE2EStand.AwaitAsync(
            () => Task.FromResult(_stand.SnapshotNotifications().OfType<SyncNotification.PushPending>().Any()),
            "the held push to stage");

        var heldSha = GitCli.Head(_stand.CloneB);
        RemoteTip().ShouldNotBe(heldSha);
        _stand.SnapshotNotifications().OfType<SyncNotification.PushPending>().ShouldHaveSingleItem()
            .Push.Sha.ShouldBe(heldSha);

        await _stand.BindAsync(_stand.CloneA);
        using var loopA = await _stand.LoopForAsync(_stand.CloneA);
        File.WriteAllText(Path.Combine(_stand.CloneA, IncomingFile), "teammates on call\n");
        loopA.AcceptBatch([IncomingFile]);
        (await loopA.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        var synced = await loopB.SyncOnce(CancellationToken.None);

        synced.ShouldBeOfType<Result<SyncCycleReport>.Ok>().Value.Pushed.ShouldNotBeNull();
        var landedSha = GitCli.Head(_stand.CloneB);
        RemoteTip().ShouldBe(landedSha);
        _stand.SnapshotNotifications().OfType<SyncNotification.PushPending>().ShouldHaveSingleItem();
        (await _stand.EventsAsync(_stand.CloneB)).Select(@event => @event.Kind)
            .ShouldBe([SyncEventKind.CommitOut, SyncEventKind.AutoMerged]);
        RemoteBlobText(HeldFile).ShouldBe("quarterly numbers, draft\n");
        RemoteBlobText(IncomingFile).ShouldBe("teammates on call\n");
    }

    [Fact]
    public async Task SemiAuto_PullStaysAutomatic_WhileTheLocalPushKeepsWaiting()
    {
        await _stand.BindAsync(_stand.CloneB, SyncRunMode.SemiAuto);
        using var loopB = await _stand.LoopForAsync(_stand.CloneB);

        await _stand.BindAsync(_stand.CloneA);
        using var loopA = await _stand.LoopForAsync(_stand.CloneA);
        File.WriteAllText(Path.Combine(_stand.CloneA, IncomingFile), "incoming roster\n");
        loopA.AcceptBatch([IncomingFile]);
        (await loopA.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        var outcome = await loopB.SyncOnce(CancellationToken.None);

        var report = outcome.ShouldBeOfType<Result<SyncCycleReport>.Ok>().Value;
        report.Pushed.ShouldBeNull();
        report.Pulled.ShouldNotBeNull();
        File.ReadAllText(Path.Combine(_stand.CloneB, IncomingFile)).ShouldBe("incoming roster\n");
        _stand.SnapshotNotifications().OfType<SyncNotification.PushPending>().ShouldBeEmpty();
        RemoteTip().ShouldBe(GitCli.Head(_stand.CloneA));
    }

    private string RemoteTip() =>
        GitCli.Run(_stand.Bare, "rev-parse", "main").Output.Trim();

    private string RemoteBlobText(string relativePath) =>
        GitCli.Run(_stand.Bare, "show", $"main:{relativePath}").Output;

    public void Dispose() => _stand.Dispose();
}
