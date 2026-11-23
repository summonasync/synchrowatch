using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.Core.Engine;
using Xunit;

namespace Syncrowatch.Tests.E2E;

[Collection("git-config-sandbox")]
public sealed class BatchCommitE2ETests : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(20);

    private readonly SyncE2EStand _stand = new();
    private readonly CancellationTokenSource _lifetime = new();

    [Fact]
    public async Task FiveRapidEditsInsideOneDebounceWindow_LandAsExactlyOneCommitOnTheRemote()
    {
        await _stand.BindAsync(_stand.CloneA);
        var time = new FakeTimeProvider();
        using var loop = await _stand.LoopForAsync(_stand.CloneA);
        var watcher = new FolderWatcher(_stand.CloneA);
        var batcher = new ChangeBatcher(_stand.CloneA, Debounce, SyncIgnoreRules.ForFolder(_stand.CloneA), time);
        var delivered = new List<IReadOnlyList<string>>();
        batcher.BatchReady += deliveredBatch =>
        {
            delivered.Add(deliveredBatch);
            loop.AcceptBatch(deliveredBatch);
        };
        watcher.ChangeDetected += batcher.Accept;
        watcher.Start(_lifetime.Token);
        try
        {
            for (var edit = 0; edit < 5; edit++)
            {
                File.WriteAllText(Path.Combine(_stand.CloneA, $"note-{edit}.txt"), $"batch line {edit}");
            }

            await SyncE2EStand.SettleAsync(batcher, minimum: 5);
            time.Advance(Debounce);

            loop.PendingBatchCount.ShouldBe(5);
            delivered.ShouldHaveSingleItem();

            var outcome = await loop.SyncOnce(CancellationToken.None);

            outcome.ShouldBeOfType<Result<SyncCycleReport>.Ok>().Value.Pushed.ShouldNotBeNull();
            RemoteCommitCount().ShouldBe(2);
            RemoteHeadSubject().ShouldBe("sync: 5 files changed");
            RemoteTree().ShouldBe([
                "gone.txt", "note-0.txt", "note-1.txt", "note-2.txt", "note-3.txt", "note-4.txt", "shared.txt"
            ]);
        }
        finally
        {
            batcher.Dispose();
            watcher.Dispose();
        }
    }

    [Fact]
    public async Task PartialWriteThatMutatesBetweenStabilityChecks_IsDeferredAndLandsInALaterBatch()
    {
        await _stand.BindAsync(_stand.CloneA);
        var time = new FakeTimeProvider();
        using var loop = await _stand.LoopForAsync(_stand.CloneA);
        var batcher = new ChangeBatcher(_stand.CloneA, Debounce, SyncIgnoreRules.ForFolder(_stand.CloneA), time);
        batcher.BatchReady += loop.AcceptBatch;

        File.WriteAllText(Path.Combine(_stand.CloneA, "wip.txt"), "first snapshot");
        batcher.Accept(new FolderChange("wip.txt", WatchChangeKind.Created));
        File.WriteAllText(Path.Combine(_stand.CloneA, "wip.txt"), "second snapshot, still growing");

        time.Advance(Debounce);

        loop.PendingBatchCount.ShouldBe(0);
        RemoteCommitCount().ShouldBe(1);

        time.Advance(Debounce);

        loop.PendingBatchCount.ShouldBe(1);
        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        RemoteCommitCount().ShouldBe(2);
        RemoteBlobText("wip.txt").ShouldBe("second snapshot, still growing");
        batcher.Dispose();
    }

    private int RemoteCommitCount() =>
        int.Parse(GitCli.Run(_stand.Bare, "rev-list", "--count", "main").Output.Trim());

    private string RemoteHeadSubject() =>
        GitCli.Run(_stand.Bare, "log", "-1", "--pretty=%s", "main").Output.Trim();

    private string RemoteBlobText(string relativePath) =>
        GitCli.Run(_stand.Bare, "show", $"main:{relativePath}").Output;

    private IReadOnlyList<string> RemoteTree() =>
        GitCli.Run(_stand.Bare, "ls-tree", "--name-only", "-r", "main").Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    public void Dispose()
    {
        _lifetime.Dispose();
        _stand.Dispose();
    }
}
