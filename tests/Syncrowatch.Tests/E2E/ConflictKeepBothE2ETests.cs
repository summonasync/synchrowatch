using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.Engine.Conflicts;
using Syncrowatch.Core.History;
using Xunit;

namespace Syncrowatch.Tests.E2E;

[Collection("git-config-sandbox")]
public sealed class ConflictKeepBothE2ETests : IDisposable
{
    private const string TextFile = "shared.txt";
    private const string BinaryFile = "asset.bin";
    private const string DeletedFile = "gone.txt";

    private static readonly byte[] BinaryFromA = [0x01, 0x00, 0xFE, 0x02];
    private static readonly byte[] BinaryFromB = [0x03, 0x00, 0xFF, 0x04];

    private readonly SyncE2EStand _stand = new();

    [Fact]
    public async Task TextBinaryAndDeletionConflictsDetectedFromTheBusSignal_KeepBothCopiesWithoutByteLoss()
    {
        await PublishDivergentBaseAsync();
        var (loop, conflicts) = await ConflictedCloneBAsync();

        await _stand.AwaitConflictsAsync(conflicts, 3);

        var pending = (await conflicts.Pending(CancellationToken.None))
            .ShouldBeOfType<Result<IReadOnlyList<Syncrowatch.Core.Engine.Conflicts.PendingConflict>>.Ok>().Value;
        pending.Single(record => record.Path == TextFile).Kind.ShouldBe(ConflictKind.TextConflict);
        pending.Single(record => record.Path == BinaryFile).Kind.ShouldBe(ConflictKind.BinaryConflict);
        pending.Single(record => record.Path == DeletedFile).Kind
            .ShouldBe(ConflictKind.ModifiedLocallyDeletedRemotely);

        File.ReadAllText(Path.Combine(_stand.CloneB, TextFile))
            .ShouldBe("ALPHA from B\nmiddle\nomega\n");
        File.ReadAllText(Path.Combine(_stand.CloneB, TheirCopyPath(TextFile)))
            .ShouldContain("ALPHA from A");
        File.ReadAllBytes(Path.Combine(_stand.CloneB, BinaryFile)).ShouldBe(BinaryFromB);
        File.ReadAllBytes(Path.Combine(_stand.CloneB, TheirCopyPath(BinaryFile))).ShouldBe(BinaryFromA);
        File.ReadAllText(Path.Combine(_stand.CloneB, DeletedFile)).ShouldBe("saved by B\n");

        GitCli.Run(_stand.CloneB, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
        GitCli.Run(_stand.CloneB, "log", "-1", "--pretty=%s").Output.Trim()
            .ShouldBe("sync: 3 files changed");

        _stand.SnapshotNotifications().OfType<SyncNotification.MergeConflicted>().ShouldHaveSingleItem()
            .ConflictedPaths.ShouldBe([BinaryFile, DeletedFile, TextFile]);
        (await _stand.EventsAsync(_stand.CloneB)).ShouldContain(@event => @event.Kind == SyncEventKind.MergeConflicted);
    }

    [Fact]
    public async Task EachResolutionOption_ProducesItsDocumentedOutcome_AndBothClonesConvergeAfterwards()
    {
        await PublishDivergentBaseAsync();
        var (loop, conflicts) = await ConflictedCloneBAsync();
        await _stand.AwaitConflictsAsync(conflicts, 3);

        var keepCopy = await conflicts.Resolve(TextFile, ConflictOption.KeepCopy, CancellationToken.None);
        keepCopy.ShouldBeOfType<Result<ConflictResolution>.Ok>().Value.CommitSha.ShouldBeNull();

        var takeTheirs = await conflicts.Resolve(BinaryFile, ConflictOption.TakeTheirs, CancellationToken.None);
        takeTheirs.ShouldBeOfType<Result<ConflictResolution>.Ok>().Value.RemainingPaths.ShouldBe([DeletedFile]);

        var keepMine = await conflicts.Resolve(DeletedFile, ConflictOption.KeepMine, CancellationToken.None);
        var merged = keepMine.ShouldBeOfType<Result<ConflictResolution>.Ok>().Value;
        merged.CommitSha.ShouldNotBeNull();

        File.ReadAllText(Path.Combine(_stand.CloneB, TextFile))
            .ShouldBe("ALPHA from B\nmiddle\nomega\n");
        File.ReadAllText(Path.Combine(_stand.CloneB, TheirCopyPath(TextFile)))
            .ShouldContain("ALPHA from A");
        File.ReadAllBytes(Path.Combine(_stand.CloneB, BinaryFile)).ShouldBe(BinaryFromA);
        File.ReadAllBytes(Path.Combine(_stand.CloneB, "asset (mine).bin")).ShouldBe(BinaryFromB);
        Directory.GetFiles(_stand.CloneB, "asset (theirs *.bin").ShouldBeEmpty();
        File.ReadAllText(Path.Combine(_stand.CloneB, DeletedFile)).ShouldBe("saved by B\n");

        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        await _stand.BindAsync(_stand.CloneA);
        using var loopA = await _stand.LoopForAsync(_stand.CloneA);
        (await loopA.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        GitCli.Head(_stand.CloneA).ShouldBe(GitCli.Head(_stand.CloneB));
        GitCli.Run(_stand.CloneA, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
        GitCli.Run(_stand.CloneB, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
        File.ReadAllText(Path.Combine(_stand.CloneA, TextFile)).ShouldBe("ALPHA from B\nmiddle\nomega\n");
        File.ReadAllBytes(Path.Combine(_stand.CloneA, BinaryFile)).ShouldBe(BinaryFromA);
        File.ReadAllText(Path.Combine(_stand.CloneA, DeletedFile)).ShouldBe("saved by B\n");

        var remoteTree = GitCli.Run(_stand.Bare, "ls-tree", "--name-only", "-r", "main").Output;
        remoteTree.ShouldNotContain(" (theirs ");
        remoteTree.ShouldNotContain(" (mine");
        remoteTree.ShouldContain(DeletedFile);
    }

    private async Task PublishDivergentBaseAsync()
    {
        await _stand.BindAsync(_stand.CloneA);
        using var loopA = await _stand.LoopForAsync(_stand.CloneA);

        File.WriteAllText(Path.Combine(_stand.CloneA, TextFile), "ALPHA from A\nmiddle\nomega\n");
        File.WriteAllBytes(Path.Combine(_stand.CloneA, BinaryFile), BinaryFromA);
        loopA.AcceptBatch([TextFile, BinaryFile]);
        (await loopA.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        File.Delete(Path.Combine(_stand.CloneA, DeletedFile));
        loopA.AcceptBatch([DeletedFile]);
        (await loopA.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();
    }

    private async Task<(SyncLoop Loop, ConflictService Conflicts)> ConflictedCloneBAsync()
    {
        await _stand.BindAsync(_stand.CloneB);
        var loop = await _stand.LoopForAsync(_stand.CloneB);
        var conflicts = await _stand.ConflictsForAsync(_stand.CloneB);

        File.WriteAllText(Path.Combine(_stand.CloneB, TextFile), "ALPHA from B\nmiddle\nomega\n");
        File.WriteAllBytes(Path.Combine(_stand.CloneB, BinaryFile), BinaryFromB);
        File.WriteAllText(Path.Combine(_stand.CloneB, DeletedFile), "saved by B\n");
        loop.AcceptBatch([TextFile, BinaryFile, DeletedFile]);
        (await loop.SyncOnce(CancellationToken.None))
            .ShouldBeOfType<Result<SyncCycleReport>.Fail>().Error.Code.ShouldBe("conflict");

        return (loop, conflicts);
    }

    private string TheirCopyPath(string relativePath)
    {
        var stem = Path.GetFileNameWithoutExtension(relativePath);
        var extension = Path.GetExtension(relativePath);
        var directory = Path.GetDirectoryName(relativePath) ?? string.Empty;
        var matches = Directory.GetFiles(
            Path.Combine(_stand.CloneB, directory),
            stem + " (theirs *" + extension);
        return Path.GetRelativePath(_stand.CloneB, matches.ShouldHaveSingleItem());
    }

    public void Dispose() => _stand.Dispose();
}
