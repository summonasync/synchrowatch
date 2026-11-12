using NSubstitute;
using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.Engine.Conflicts;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class ConflictSandbox : IDisposable
{
    public const string SharedBase = "shared.txt";
    public const string ThirdBase = "third.txt";

    public ConflictSandbox(string baseFile = "base.txt", string baseContent = "base\n")
    {
        Root = TempDir.Create();
        Bare = Path.Combine(Root, "origin.git");
        CloneA = Path.Combine(Root, "clone-a");
        CloneB = Path.Combine(Root, "clone-b");
        GitCli.Ensure(Root, "init", "--bare", "-b", "main", Bare);
        GitCli.Ensure(Root, "clone", Bare, CloneA);
        GitCli.ConfigureIdentity(CloneA);
        File.WriteAllText(Path.Combine(CloneA, baseFile), baseContent);
        File.WriteAllText(Path.Combine(CloneA, SharedBase), "base shared content\n");
        File.WriteAllText(Path.Combine(CloneA, ThirdBase), "base third content\n");
        GitCli.CommitAll(CloneA, "base");
        GitCli.Push(CloneA, Bare, "main");
        GitCli.Ensure(Root, "clone", Bare, CloneB);
        GitCli.ConfigureIdentity(CloneB);

        Bus.Received += notification => Notifications.Add(notification);
    }

    public string Root { get; }
    public string Bare { get; }
    public string CloneA { get; }
    public string CloneB { get; }
    public SyncEventBus Bus { get; } = new();
    public List<SyncNotification> Notifications { get; } = [];

    private readonly List<SyncLoop> _loops = [];

    public SyncConfig ConfigFor(string clone) => new()
    {
        WatchedFolder = clone,
        RemoteUrl = Bare,
        Branch = "main"
    };

    public ILocalGit LocalGit() => new GitLocal(new GitProcess());

    public SyncLoop LoopFor(string clone, ISyncEventStore? store = null)
    {
        var config = ConfigFor(clone);
        var remote = new GitHubRepository(config, Substitute.For<ICredentialStore>(), new GitProcess());
        var loop = new SyncLoop(
            config,
            remote,
            LocalGit(),
            store ?? new JsonSyncEventStore(Path.Combine(Root, "history-" + Path.GetFileName(clone))),
            Bus);
        _loops.Add(loop);
        return loop;
    }

    public IConflictStore StoreFor(string clone) =>
        new JsonConflictStore(Path.Combine(Root, "conflicts-" + Path.GetFileName(clone)));

    public ConflictDetector DetectorFor(string clone) =>
        new(ConfigFor(clone), LocalGit(), StoreFor(clone));

    public ResolutionExecutor ExecutorFor(string clone, IConflictStore? store = null) =>
        new(ConfigFor(clone), LocalGit(), store ?? StoreFor(clone));

    public async Task EditAndSync(string clone, SyncLoop loop, string file, string content)
    {
        File.WriteAllText(Path.Combine(clone, file), content);
        loop.AcceptBatch([file]);
        AssertSyncOk(await loop.SyncOnce(CancellationToken.None));
    }

    public async Task DeleteAndSync(string clone, SyncLoop loop, string file)
    {
        File.Delete(Path.Combine(clone, file));
        loop.AcceptBatch([file]);
        AssertSyncOk(await loop.SyncOnce(CancellationToken.None));
    }

    internal static void AssertSyncOk(Result<SyncCycleReport> outcome)
    {
        if (outcome is Result<SyncCycleReport>.Fail failure)
        {
            throw new InvalidOperationException($"sync cycle failed: {failure.Error.Code} ({failure.Error.Message})");
        }
    }

    public async Task EditIntoConflict(string file, string contentA, string contentB)
    {
        var loopA = LoopFor(CloneA);
        await EditAndSync(CloneA, loopA, file, contentA);
        File.WriteAllText(Path.Combine(CloneB, file), contentB);
        var loopB = LoopFor(CloneB);
        loopB.AcceptBatch([file]);
        (await loopB.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Fail>();
    }

    public async Task EditIntoConflict(string file, byte[] contentA, byte[] contentB)
    {
        var loopA = LoopFor(CloneA);
        File.WriteAllBytes(Path.Combine(CloneA, file), contentA);
        loopA.AcceptBatch([file]);
        (await loopA.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        File.WriteAllBytes(Path.Combine(CloneB, file), contentB);
        var loopB = LoopFor(CloneB);
        loopB.AcceptBatch([file]);
        (await loopB.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Fail>();
    }

    public async Task DeleteLocallyIntoConflict(string file, string contentA)
    {
        var loopA = LoopFor(CloneA);
        await EditAndSync(CloneA, loopA, file, contentA);
        File.Delete(Path.Combine(CloneB, file));
        var loopB = LoopFor(CloneB);
        loopB.AcceptBatch([file]);
        (await loopB.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Fail>();
    }

    public async Task RemoteDeleteIntoConflict(string file, string contentB)
    {
        var loopA = LoopFor(CloneA);
        await DeleteAndSync(CloneA, loopA, file);
        File.WriteAllText(Path.Combine(CloneB, file), contentB);
        var loopB = LoopFor(CloneB);
        loopB.AcceptBatch([file]);
        (await loopB.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Fail>();
    }

    public IReadOnlyList<string> ConflictCopiesOf(string clone, string file) =>
        Directory
            .GetFiles(clone, Path.GetFileNameWithoutExtension(file) + " (theirs *")
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();

    public async Task<IReadOnlyList<SyncEvent>> Events(ISyncEventStore store)
    {
        var outcome = await store.Query(new HistoryQuery { Limit = 100 }, CancellationToken.None);
        return outcome.ShouldBeOfType<Result<IReadOnlyList<SyncEvent>>.Ok>().Value;
    }

    public void Dispose()
    {
        foreach (var loop in _loops)
        {
            loop.Dispose();
        }

        Directory.Delete(Root, true);
    }
}
