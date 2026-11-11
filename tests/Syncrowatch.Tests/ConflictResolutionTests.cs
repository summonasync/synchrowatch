using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.Engine.Conflicts;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class ConflictResolutionTests : IDisposable
{
    private const string Shared = "shared.txt";
    private const string ContentA = "ALPHA from A\nmiddle\nomega\n";
    private const string ContentB = "ALPHA from B\nmiddle\nomega\n";

    private readonly ConflictSandbox _sandbox = new();

    [Fact]
    public async Task TakeTheirs_TextConflict_TreeBecomesTheirs_OursKeptAsMineCopy()
    {
        await PrepareTextConflict();

        var outcome = await ResolveAsync(Shared, ConflictOption.TakeTheirs);
        var resolution = outcome.ShouldBeOfType<Result<ConflictResolution>.Ok>().Value;

        resolution.CommitSha.ShouldNotBeNull();
        resolution.ResolvedPaths.ShouldBe([Shared]);
        resolution.RemainingPaths.ShouldBeEmpty();
        GitCli.Head(_sandbox.CloneB).ShouldBe(resolution.CommitSha);
        GitCli.Run(_sandbox.CloneB, "log", "-1", "--pretty=%p").Output.Trim().Split(' ').Length.ShouldBe(2);
        File.ReadAllText(Path.Combine(_sandbox.CloneB, Shared)).ShouldBe(ContentA);
        File.ReadAllText(Path.Combine(_sandbox.CloneB, "shared (mine).txt")).ShouldBe(ContentB);
        _sandbox.ConflictCopiesOf(_sandbox.CloneB, Shared).ShouldBeEmpty();
        GitCli.Run(_sandbox.CloneB, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
        MergeHeadPresent().ShouldBeFalse();
    }

    [Fact]
    public async Task TakeTheirs_ResolutionPushesAndBothClonesConverge()
    {
        await PrepareTextConflict();
        await ResolveAsync(Shared, ConflictOption.TakeTheirs);

        var loopB = _sandbox.LoopFor(_sandbox.CloneB);
        (await loopB.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        var loopA = _sandbox.LoopFor(_sandbox.CloneA);
        (await loopA.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        GitCli.Head(_sandbox.CloneA).ShouldBe(GitCli.Head(_sandbox.CloneB));
        File.ReadAllText(Path.Combine(_sandbox.CloneA, Shared)).ShouldBe(ContentA);
        GitCli.Run(_sandbox.CloneA, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
    }

    [Fact]
    public async Task KeepMine_TextConflict_TreeKeepsOursAndTheirsCopyIsDiscarded()
    {
        await PrepareTextConflict();

        var outcome = await ResolveAsync(Shared, ConflictOption.KeepMine);
        var resolution = outcome.ShouldBeOfType<Result<ConflictResolution>.Ok>().Value;

        resolution.CommitSha.ShouldNotBeNull();
        File.ReadAllText(Path.Combine(_sandbox.CloneB, Shared)).ShouldBe(ContentB);
        _sandbox.ConflictCopiesOf(_sandbox.CloneB, Shared).ShouldBeEmpty();
        File.Exists(Path.Combine(_sandbox.CloneB, "shared (mine).txt")).ShouldBeFalse();
        MergeHeadPresent().ShouldBeFalse();

        var loopB = _sandbox.LoopFor(_sandbox.CloneB);
        (await loopB.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        var loopA = _sandbox.LoopFor(_sandbox.CloneA);
        (await loopA.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        File.ReadAllText(Path.Combine(_sandbox.CloneA, Shared)).ShouldBe(ContentB);
    }

    [Fact]
    public async Task KeepCopy_TextConflict_BothVersionsRemainOnDisk()
    {
        await PrepareTextConflict();

        var outcome = await ResolveAsync(Shared, ConflictOption.KeepCopy);
        var resolution = outcome.ShouldBeOfType<Result<ConflictResolution>.Ok>().Value;

        resolution.CommitSha.ShouldNotBeNull();
        File.ReadAllText(Path.Combine(_sandbox.CloneB, Shared)).ShouldBe(ContentB);
        var copies = _sandbox.ConflictCopiesOf(_sandbox.CloneB, Shared);
        copies.ShouldHaveSingleItem();
        File.ReadAllText(Path.Combine(_sandbox.CloneB, copies[0]!)).ShouldBe(ContentA);
        File.Exists(Path.Combine(_sandbox.CloneB, "shared (mine).txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task MultiPathConflict_CommitsOnlyAfterLastPathIsHandled()
    {
        var loopA = _sandbox.LoopFor(_sandbox.CloneA);
        await _sandbox.EditAndSync(_sandbox.CloneA, loopA, "alpha.txt", "alpha-A\n");
        await _sandbox.EditAndSync(_sandbox.CloneA, loopA, "beta.txt", "beta-A\n");
        File.WriteAllText(Path.Combine(_sandbox.CloneB, "alpha.txt"), "alpha-B\n");
        File.WriteAllText(Path.Combine(_sandbox.CloneB, "beta.txt"), "beta-B\n");
        var loopB = _sandbox.LoopFor(_sandbox.CloneB);
        loopB.AcceptBatch(["alpha.txt", "beta.txt"]);
        (await loopB.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Fail>();
        await _sandbox.DetectorFor(_sandbox.CloneB)
            .HandleConflicted(["alpha.txt", "beta.txt"], CancellationToken.None);

        var executor = _sandbox.ExecutorFor(_sandbox.CloneB);
        (await executor.Resolve("alpha.txt", ConflictOption.KeepMine, CancellationToken.None))
            .ShouldBeOfType<Result<ConflictResolution>.Ok>().Value.CommitSha.ShouldBeNull();

        MergeHeadPresent().ShouldBeTrue();
        File.ReadAllText(Path.Combine(_sandbox.CloneB, "alpha.txt")).ShouldBe("alpha-B\n");

        var final = (await executor.Resolve("beta.txt", ConflictOption.KeepMine, CancellationToken.None))
            .ShouldBeOfType<Result<ConflictResolution>.Ok>().Value;
        final.CommitSha.ShouldNotBeNull();
        MergeHeadPresent().ShouldBeFalse();
        File.ReadAllText(Path.Combine(_sandbox.CloneB, "beta.txt")).ShouldBe("beta-B\n");
    }

    [Fact]
    public async Task LocalDelete_KeepMine_DeletionStandsAndTheirsCopySurvives()
    {
        await _sandbox.DeleteLocallyIntoConflict(Shared, ContentA);
        var detector = _sandbox.DetectorFor(_sandbox.CloneB);
        var items = (await detector.HandleConflicted([Shared], CancellationToken.None))
            .ShouldBeOfType<Result<IReadOnlyList<ConflictItem>>.Ok>().Value;
        items.Single().Kind.ShouldBe(ConflictKind.DeletedLocallyModifiedRemotely);

        var outcome = await ResolveAsync(Shared, ConflictOption.KeepMine);
        var resolution = outcome.ShouldBeOfType<Result<ConflictResolution>.Ok>().Value;

        resolution.CommitSha.ShouldNotBeNull();
        File.Exists(Path.Combine(_sandbox.CloneB, Shared)).ShouldBeFalse();
        File.ReadAllText(Path.Combine(_sandbox.CloneB, items.Single().CopyPath!)).ShouldBe(ContentA);
        MergeHeadPresent().ShouldBeFalse();
    }

    [Fact]
    public async Task LocalDelete_TakeTheirs_FileComesBackAsTheirsContent()
    {
        await _sandbox.DeleteLocallyIntoConflict(Shared, ContentA);
        var detector = _sandbox.DetectorFor(_sandbox.CloneB);
        var items = (await detector.HandleConflicted([Shared], CancellationToken.None))
            .ShouldBeOfType<Result<IReadOnlyList<ConflictItem>>.Ok>().Value;

        var outcome = await ResolveAsync(Shared, ConflictOption.TakeTheirs);
        var resolution = outcome.ShouldBeOfType<Result<ConflictResolution>.Ok>().Value;

        resolution.CommitSha.ShouldNotBeNull();
        File.ReadAllText(Path.Combine(_sandbox.CloneB, Shared)).ShouldBe(ContentA);
        File.Exists(Path.Combine(_sandbox.CloneB, items.Single().CopyPath!)).ShouldBeFalse();
    }

    [Fact]
    public async Task RemoteDelete_TakeTheirs_DeletionWinsAndOursKeptAsMineCopy()
    {
        await _sandbox.RemoteDeleteIntoConflict(Shared, ContentB);
        var detector = _sandbox.DetectorFor(_sandbox.CloneB);
        var items = (await detector.HandleConflicted([Shared], CancellationToken.None))
            .ShouldBeOfType<Result<IReadOnlyList<ConflictItem>>.Ok>().Value;
        items.Single().Kind.ShouldBe(ConflictKind.ModifiedLocallyDeletedRemotely);
        items.Single().CopyPath.ShouldBeNull();

        var outcome = await ResolveAsync(Shared, ConflictOption.TakeTheirs);
        var resolution = outcome.ShouldBeOfType<Result<ConflictResolution>.Ok>().Value;

        resolution.CommitSha.ShouldNotBeNull();
        File.Exists(Path.Combine(_sandbox.CloneB, Shared)).ShouldBeFalse();
        File.ReadAllText(Path.Combine(_sandbox.CloneB, "shared (mine).txt")).ShouldBe(ContentB);
    }

    [Fact]
    public async Task BinaryDivergence_TakeTheirs_CanonicalBytesAreExact()
    {
        byte[] theirs = [0x01, 0x00, 0xFE, 0x02];
        byte[] ours = [0x03, 0x00, 0xFF, 0x04];
        await _sandbox.EditIntoConflict("asset.bin", theirs, ours);
        var detector = _sandbox.DetectorFor(_sandbox.CloneB);
        var items = (await detector.HandleConflicted(["asset.bin"], CancellationToken.None))
            .ShouldBeOfType<Result<IReadOnlyList<ConflictItem>>.Ok>().Value;
        items.Single().Kind.ShouldBe(ConflictKind.BinaryConflict);

        var outcome = await ResolveAsync("asset.bin", ConflictOption.TakeTheirs);
        outcome.ShouldBeOfType<Result<ConflictResolution>.Ok>().Value.CommitSha.ShouldNotBeNull();

        File.ReadAllBytes(Path.Combine(_sandbox.CloneB, "asset.bin")).ShouldBe(theirs);
        File.ReadAllBytes(Path.Combine(_sandbox.CloneB, "asset (mine).bin")).ShouldBe(ours);
    }

    [Fact]
    public async Task ConflictCopies_NeverReachABatchCommit()
    {
        await PrepareTextConflict();
        var copyPath = _sandbox.ConflictCopiesOf(_sandbox.CloneB, Shared).Single();
        var batches = new List<IReadOnlyList<string>>();
        var time = new FakeTimeProvider();
        using var batcher = new ChangeBatcher(
            _sandbox.CloneB,
            TimeSpan.FromSeconds(20),
            SyncIgnoreRules.ForFolder(_sandbox.CloneB),
            time);
        batcher.BatchReady += batches.Add;

        batcher.Accept(new FolderChange(copyPath, WatchChangeKind.Created));
        File.WriteAllText(Path.Combine(_sandbox.CloneB, "fresh.txt"), "tracked change\n");
        batcher.Accept(new FolderChange("fresh.txt", WatchChangeKind.Created));
        time.Advance(TimeSpan.FromSeconds(21));

        batches.Count.ShouldBe(1);
        batches[0].ShouldBe(["fresh.txt"]);
    }

    [Fact]
    public async Task UnknownPath_Resolve_FailsWithNotFound()
    {
        await PrepareTextConflict();

        var outcome = await ResolveAsync("missing.txt", ConflictOption.KeepMine);

        outcome.ShouldBeOfType<Result<ConflictResolution>.Fail>().Error.Code.ShouldBe("not_found");
    }

    public void Dispose() => _sandbox.Dispose();

    private async Task<ConflictDetector> PrepareTextConflict()
    {
        await _sandbox.EditIntoConflict(Shared, ContentA, ContentB);
        var detector = _sandbox.DetectorFor(_sandbox.CloneB);
        detector.HandleConflicted([Shared], CancellationToken.None)
            .GetAwaiter().GetResult()
            .ShouldBeOfType<Result<IReadOnlyList<ConflictItem>>.Ok>().Value.Count.ShouldBe(1);
        return detector;
    }

    private Task<Result<ConflictResolution>> ResolveAsync(string path, ConflictOption option) =>
        _sandbox.ExecutorFor(_sandbox.CloneB).Resolve(path, option, CancellationToken.None);

    private bool MergeHeadPresent()
    {
        var (exitCode, _) = GitCli.Run(_sandbox.CloneB, "rev-parse", "-q", "--verify", "MERGE_HEAD");
        return exitCode == 0;
    }
}
