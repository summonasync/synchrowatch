using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.Core.History;
using Xunit;

namespace Syncrowatch.Tests.E2E;

[Collection("git-config-sandbox")]
public sealed class DivergenceConvergenceE2ETests : IDisposable
{
    private readonly SyncE2EStand _stand = new();

    [Fact]
    public async Task CleanTextDivergenceOnBothClones_ConvergesWithMergeHistoryAndToastNotification()
    {
        await _stand.BindAsync(_stand.CloneA);
        using var loopA = await _stand.LoopForAsync(_stand.CloneA);
        await _stand.BindAsync(_stand.CloneB);
        using var loopB = await _stand.LoopForAsync(_stand.CloneB);

        File.WriteAllText(Path.Combine(_stand.CloneA, SyncE2EStand.SharedFile), "ALPHA from A\nmiddle\nomega\n");
        loopA.AcceptBatch([SyncE2EStand.SharedFile]);
        (await loopA.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        File.WriteAllText(Path.Combine(_stand.CloneB, SyncE2EStand.SharedFile), "alpha\nmiddle\nOMEGA from B\n");
        loopB.AcceptBatch([SyncE2EStand.SharedFile]);
        (await loopB.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        File.ReadAllText(Path.Combine(_stand.CloneB, SyncE2EStand.SharedFile))
            .ShouldBe("ALPHA from A\nmiddle\nOMEGA from B\n");

        (await loopA.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        File.ReadAllText(Path.Combine(_stand.CloneA, SyncE2EStand.SharedFile))
            .ShouldBe("ALPHA from A\nmiddle\nOMEGA from B\n");
        GitCli.Run(_stand.CloneA, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
        GitCli.Run(_stand.CloneB, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
        GitCli.Head(_stand.CloneA).ShouldBe(GitCli.Head(_stand.CloneB));

        (await _stand.EventsAsync(_stand.CloneB)).Select(@event => @event.Kind)
            .ShouldBe([SyncEventKind.CommitOut, SyncEventKind.AutoMerged]);
        (await _stand.EventsAsync(_stand.CloneA)).Select(@event => @event.Kind)
            .ShouldBe([SyncEventKind.CommitIn, SyncEventKind.CommitOut]);

        var notifications = _stand.SnapshotNotifications();
        notifications.OfType<SyncNotification.AutoMerged>().ShouldHaveSingleItem()
            .Files.ShouldBe([SyncE2EStand.SharedFile]);
        notifications.OfType<SyncNotification.SyncCompleted>().Count().ShouldBeGreaterThanOrEqualTo(2);
    }

    public void Dispose() => _stand.Dispose();
}
