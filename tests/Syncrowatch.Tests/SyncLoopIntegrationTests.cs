using NSubstitute;
using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class SyncLoopIntegrationTests
{
    private const string SharedFile = "shared.txt";

    [Fact]
    public async Task TwoClones_CleanTextDivergence_ConvergeAfterCycles()
    {
        using var pair = new ClonePair();
        var storeA = new JsonSyncEventStore(Path.Combine(pair.Root, "history-a"));
        var storeB = new JsonSyncEventStore(Path.Combine(pair.Root, "history-b"));
        using var loopA = pair.Loop(pair.CloneA, storeA);
        using var loopB = pair.Loop(pair.CloneB, storeB);

        File.WriteAllText(Path.Combine(pair.CloneA, SharedFile), "ALPHA from A\nmiddle\nomega\n");
        loopA.AcceptBatch([SharedFile]);
        (await loopA.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        File.WriteAllText(Path.Combine(pair.CloneB, SharedFile), "alpha\nmiddle\nOMEGA from B\n");
        loopB.AcceptBatch([SharedFile]);
        (await loopB.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        File.ReadAllText(Path.Combine(pair.CloneB, SharedFile))
            .ShouldBe("ALPHA from A\nmiddle\nOMEGA from B\n");

        (await loopA.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        File.ReadAllText(Path.Combine(pair.CloneA, SharedFile))
            .ShouldBe("ALPHA from A\nmiddle\nOMEGA from B\n");
        GitCli.Run(pair.CloneA, "status", "--porcelain").Output.Trim().ShouldBeEmpty();

        var mergedEvents = await Events(storeB);
        mergedEvents.Select(@event => @event.Kind).ShouldBe([SyncEventKind.CommitOut, SyncEventKind.AutoMerged]);
        mergedEvents[1].Paths.ShouldBe([SharedFile]);

        var pulledEvents = await Events(storeA);
        pulledEvents.Select(@event => @event.Kind).ShouldBe([SyncEventKind.CommitIn, SyncEventKind.CommitOut]);
    }

    [Fact]
    public async Task ConflictingEdits_MergeAbortsAndTreeStaysAtPreMergeHead()
    {
        using var pair = new ClonePair();
        var storeB = new JsonSyncEventStore(Path.Combine(pair.Root, "history-b"));
        using var loopA = pair.Loop(pair.CloneA, new JsonSyncEventStore(Path.Combine(pair.Root, "history-a")));
        using var loopB = pair.Loop(pair.CloneB, storeB);

        File.WriteAllText(Path.Combine(pair.CloneA, SharedFile), "ALPHA from A\nmiddle\nomega\n");
        loopA.AcceptBatch([SharedFile]);
        (await loopA.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        File.WriteAllText(Path.Combine(pair.CloneB, SharedFile), "ALPHA from B\nmiddle\nomega\n");
        loopB.AcceptBatch([SharedFile]);
        var outcome = await loopB.SyncOnce(CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncCycleReport>.Fail>().Error.Code.ShouldBe("conflict");
        GitCli.Run(pair.CloneB, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
        GitCli.Run(pair.CloneB, "log", "-1", "--pretty=%s").Output.Trim()
            .ShouldBe("sync: 1 file changed (shared.txt)");
        File.ReadAllText(Path.Combine(pair.CloneB, SharedFile)).ShouldBe("ALPHA from B\nmiddle\nomega\n");

        var events = await Events(storeB);
        events.Single(@event => @event.Kind == SyncEventKind.MergeConflicted).Paths.ShouldBe([SharedFile]);
        pair.Notifications.OfType<SyncNotification.MergeConflicted>().Single()
            .ConflictedPaths.ShouldBe([SharedFile]);
    }

    [Fact]
    public async Task SyncNow_InsideTheDebounceWindow_CommitsAndPushesThePendingChange()
    {
        using var pair = new ClonePair();
        var store = new JsonSyncEventStore(Path.Combine(pair.Root, "history-a"));
        var settings = Substitute.For<ISyncConfigStore>();
        settings.Load(Arg.Any<CancellationToken>())
            .Returns(new Result<SyncConfig>.Ok(new SyncConfig
            {
                WatchedFolder = pair.CloneA,
                RemoteUrl = pair.BarePath,
                Branch = "main"
            }));
        using var watching = new FakeWatching();
        using var engine = pair.Loop(pair.CloneA, store);
        using var controller = new ModeController(engine, settings, watching, timeProvider: new FakeTimeProvider());
        await controller.LaunchAsync();

        File.WriteAllText(Path.Combine(pair.CloneA, SharedFile), "edited inside the window\n");
        watching.Emit(SharedFile);

        var outcome = await controller.SyncNowAsync();

        outcome.ShouldBeOfType<Result<SyncCycleReport>.Ok>().Value.Pushed.ShouldNotBeNull();
        GitCli.Run(pair.CloneA, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
        GitCli.Run(pair.CloneA, "log", "-1", "--pretty=%s").Output.Trim()
            .ShouldBe("sync: 1 file changed (shared.txt)");
        GitCli.Run(pair.BarePath, "rev-parse", "main").Output.Trim()
            .ShouldBe(GitCli.Run(pair.CloneA, "rev-parse", "HEAD").Output.Trim());
    }

    [Fact]
    public async Task CancelDuringCommit_BatchDoesNotHalfLand()
    {
        using var pair = new ClonePair();
        using var canceller = new CancellationTokenSource();
        var runner = new CancellingRunner(new GitProcess(), canceller);
        var store = new JsonSyncEventStore(Path.Combine(pair.Root, "history-a"));
        using var loop = pair.Loop(pair.CloneA, store, runner);
        File.WriteAllText(Path.Combine(pair.CloneA, SharedFile), "alpha edited\nmiddle\nomega\n");
        loop.AcceptBatch([SharedFile]);

        await Should.ThrowAsync<OperationCanceledException>(() => loop.SyncOnce(CancellationToken.None));

        int.Parse(GitCli.Run(pair.CloneA, "rev-list", "--count", "HEAD").Output.Trim()).ShouldBe(1);
        runner.Invocations.ShouldNotContain(invocation => invocation.Arguments.Contains("fetch"));
        runner.Invocations.ShouldNotContain(invocation => invocation.Arguments.Contains("merge"));
    }

    private static async Task<IReadOnlyList<SyncEvent>> Events(ISyncEventStore store)
    {
        var outcome = await store.Query(new HistoryQuery { Limit = 100 }, CancellationToken.None);
        return outcome.ShouldBeOfType<Result<IReadOnlyList<SyncEvent>>.Ok>().Value;
    }

    private sealed class ClonePair : IDisposable
    {
        public ClonePair()
        {
            Root = TempDir.Create();
            BarePath = Path.Combine(Root, "origin.git");
            CloneA = Path.Combine(Root, "clone-a");
            CloneB = Path.Combine(Root, "clone-b");
            GitCli.Ensure(Root, "init", "--bare", "-b", "main", BarePath);
            GitCli.Ensure(Root, "clone", BarePath, CloneA);
            GitCli.ConfigureIdentity(CloneA);
            File.WriteAllText(Path.Combine(CloneA, SharedFile), "alpha\nmiddle\nomega\n");
            GitCli.CommitAll(CloneA, "base");
            GitCli.Push(CloneA, BarePath, "main");
            GitCli.Ensure(Root, "clone", BarePath, CloneB);
            GitCli.ConfigureIdentity(CloneB);

            Bus.Received += Notifications.Add;
        }

        public string Root { get; }
        public string BarePath { get; }
        public string CloneA { get; }
        public string CloneB { get; }
        public SyncEventBus Bus { get; } = new();
        public List<SyncNotification> Notifications { get; } = [];

        public SyncLoop Loop(string clone, ISyncEventStore store, IGitRunner? runner = null)
        {
            var runnerImplementation = runner ?? new GitProcess();
            var config = new SyncConfig
            {
                WatchedFolder = clone,
                RemoteUrl = BarePath,
                Branch = "main"
            };
            var remote = new GitHubRepository(config, Substitute.For<ICredentialStore>(), runnerImplementation);
            var local = new GitLocal(runnerImplementation);
            return new SyncLoop(config, remote, local, store, Bus);
        }

        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class CancellingRunner(IGitRunner inner, CancellationTokenSource canceller) : IGitRunner
    {
        public List<GitInvocation> Invocations { get; } = [];

        public Task<GitCommandResult> RunAsync(GitInvocation invocation, CancellationToken cancellationToken)
        {
            Invocations.Add(invocation);
            if (invocation.Arguments.Contains("commit"))
            {
                canceller.Cancel();
                throw new OperationCanceledException(canceller.Token);
            }

            return inner.RunAsync(invocation, cancellationToken);
        }
    }
}
