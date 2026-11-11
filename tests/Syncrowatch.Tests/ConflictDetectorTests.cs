using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Engine.Conflicts;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class ConflictDetectorTests : IDisposable
{
    private readonly ConflictSandbox _sandbox = new();

    [Fact]
    public async Task TextConflict_OursStaysInTree_TheirsBecomesConflictCopy()
    {
        await _sandbox.EditIntoConflict("shared.txt", "ALPHA from A\nmiddle\nomega\n", "ALPHA from B\nmiddle\nomega\n");
        var detector = _sandbox.DetectorFor(_sandbox.CloneB);

        var outcome = await detector.HandleConflicted(["shared.txt"], CancellationToken.None);
        var items = outcome.ShouldBeOfType<Result<IReadOnlyList<ConflictItem>>.Ok>().Value;

        items.Single().Kind.ShouldBe(ConflictKind.TextConflict);
        var copy = items.Single().CopyPath;
        copy.ShouldNotBeNull();
        Directory.GetFiles(_sandbox.CloneB, "shared (theirs *).txt").ShouldHaveSingleItem();
        File.ReadAllText(Path.Combine(_sandbox.CloneB, copy)).ShouldBe("ALPHA from A\nmiddle\nomega\n");
        File.ReadAllText(Path.Combine(_sandbox.CloneB, "shared.txt")).ShouldBe("ALPHA from B\nmiddle\nomega\n");
        GitCli.Run(_sandbox.CloneB, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
    }

    [Fact]
    public async Task Detect_Twice_DoesNotDuplicateRecordsOrCopies()
    {
        await _sandbox.EditIntoConflict("shared.txt", "ALPHA from A\n", "ALPHA from B\n");
        var detector = _sandbox.DetectorFor(_sandbox.CloneB);

        (await detector.HandleConflicted(["shared.txt"], CancellationToken.None))
            .ShouldBeOfType<Result<IReadOnlyList<ConflictItem>>.Ok>().Value.Count.ShouldBe(1);
        (await detector.HandleConflicted(["shared.txt"], CancellationToken.None))
            .ShouldBeOfType<Result<IReadOnlyList<ConflictItem>>.Ok>().Value.ShouldBeEmpty();

        Directory.GetFiles(_sandbox.CloneB, "shared (theirs *).txt").ShouldHaveSingleItem();
        var pending = await _sandbox.StoreFor(_sandbox.CloneB).Pending();
        ((Result<IReadOnlyList<ConflictRecord>>.Ok)pending).Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task BinaryDivergence_ClassifiedBinaryAndKeptBothByteExact()
    {
        byte[] theirs = [0x01, 0x00, 0xFE, 0x02];
        byte[] ours = [0x03, 0x00, 0xFF, 0x04];
        await _sandbox.EditIntoConflict("asset.bin", theirs, ours);
        var detector = _sandbox.DetectorFor(_sandbox.CloneB);

        var items = (await detector.HandleConflicted(["asset.bin"], CancellationToken.None))
            .ShouldBeOfType<Result<IReadOnlyList<ConflictItem>>.Ok>().Value;

        items.Single().Kind.ShouldBe(ConflictKind.BinaryConflict);
        File.ReadAllBytes(Path.Combine(_sandbox.CloneB, items.Single().CopyPath!))
            .ShouldBe(theirs);
        File.ReadAllBytes(Path.Combine(_sandbox.CloneB, "asset.bin"))
            .ShouldBe(ours);
    }

    [Fact]
    public async Task LocalDeleteVersusRemoteEdit_FileSurvivesAsConflictCopy()
    {
        await _sandbox.DeleteLocallyIntoConflict("shared.txt", "revived from A\n");
        var detector = _sandbox.DetectorFor(_sandbox.CloneB);

        var items = (await detector.HandleConflicted(["shared.txt"], CancellationToken.None))
            .ShouldBeOfType<Result<IReadOnlyList<ConflictItem>>.Ok>().Value;

        items.Single().Kind.ShouldBe(ConflictKind.DeletedLocallyModifiedRemotely);
        File.ReadAllText(Path.Combine(_sandbox.CloneB, items.Single().CopyPath!)).ShouldBe("revived from A\n");
        File.Exists(Path.Combine(_sandbox.CloneB, "shared.txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task RemoteDeleteVersusLocalEdit_CanonicalFileKeptWithoutCopy()
    {
        await _sandbox.RemoteDeleteIntoConflict("shared.txt", "kept from B\n");
        var detector = _sandbox.DetectorFor(_sandbox.CloneB);

        var items = (await detector.HandleConflicted(["shared.txt"], CancellationToken.None))
            .ShouldBeOfType<Result<IReadOnlyList<ConflictItem>>.Ok>().Value;

        items.Single().Kind.ShouldBe(ConflictKind.ModifiedLocallyDeletedRemotely);
        items.Single().CopyPath.ShouldBeNull();
        File.ReadAllText(Path.Combine(_sandbox.CloneB, "shared.txt")).ShouldBe("kept from B\n");
    }

    [Fact]
    public async Task MultiPathConflict_TracksEachPathIndependently()
    {
        var loopA = _sandbox.LoopFor(_sandbox.CloneA);
        await _sandbox.EditAndSync(_sandbox.CloneA, loopA, "alpha.txt", "alpha-A\n");
        await _sandbox.EditAndSync(_sandbox.CloneA, loopA, "beta.txt", "beta-A\n");

        File.WriteAllText(Path.Combine(_sandbox.CloneB, "alpha.txt"), "alpha-B\n");
        File.WriteAllText(Path.Combine(_sandbox.CloneB, "beta.txt"), "beta-B\n");
        var loopB = _sandbox.LoopFor(_sandbox.CloneB);
        loopB.AcceptBatch(["alpha.txt", "beta.txt"]);
        (await loopB.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Fail>();

        var items = (await _sandbox.DetectorFor(_sandbox.CloneB)
            .HandleConflicted(["alpha.txt", "beta.txt"], CancellationToken.None))
            .ShouldBeOfType<Result<IReadOnlyList<ConflictItem>>.Ok>().Value;

        items.Select(item => item.Path).OrderBy(path => path)
            .ShouldBe(["alpha.txt", "beta.txt"]);
    }

    public void Dispose() => _sandbox.Dispose();
}
