using Shouldly;
using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class WatchBatchIntegrationTests : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(20);

    private readonly string _root = TempDir.Create();

    [Fact]
    public async Task TenRapidEdits_InsideOneDebounceWindow_ProduceExactlyOneCommit()
    {
        BootstrapRepository();
        var (watcher, batcher, committer, time, batches) = StartPipeline();
        try
        {
            watcher.Start();
            const string file = "notes.txt";
            for (var edit = 0; edit < 10; edit++)
            {
                File.WriteAllText(Path.Combine(_root, file), $"version {edit}");
                await Task.Delay(20);
            }

            await WaitForDeliveryToSettleAsync(batcher);
            RunWindows(time, batches, maxWindows: 4);

            batches.Count.ShouldBe(1);
            batches[0].ShouldBe([file]);

            await committer.CommitBatch(batches[0], CancellationToken.None);
            CommitCount().ShouldBe(2);
            HeadSubject().ShouldBe("sync: 1 file changed (notes.txt)");
        }
        finally
        {
            batcher.Dispose();
            watcher.Dispose();
        }
    }

    [Fact]
    public async Task IgnoredAndSyncIgnorePaths_NeverLandInACommit()
    {
        BootstrapRepository(withSyncIgnore: true);
        var (watcher, batcher, committer, time, batches) = StartPipeline();
        try
        {
            watcher.Start();
            File.WriteAllText(Path.Combine(_root, "token.key"), "secret");
            File.WriteAllText(Path.Combine(_root, "drafts", "scratch.txt"), "wip");
            File.WriteAllText(Path.Combine(_root, "keepme.txt"), "real content");
            File.WriteAllText(Path.Combine(_root, "leftover.tmp"), "junk");
            await WaitForDeliveryToSettleAsync(batcher);
            RunWindows(time, batches, maxWindows: 4);

            batches.Count.ShouldBe(1);
            batches[0].ShouldBe(["keepme.txt"]);

            await committer.CommitBatch(batches[0], CancellationToken.None);
            CommitCount().ShouldBe(2);
            HeadChangedFiles().ShouldBe(["keepme.txt"]);
        }
        finally
        {
            batcher.Dispose();
            watcher.Dispose();
        }
    }

    [Fact]
    public async Task BracketFilename_CommitsExactlyTheNamedFile()
    {
        BootstrapRepository();
        File.WriteAllText(Path.Combine(_root, "data[1].txt"), "bracketed");
        File.WriteAllText(Path.Combine(_root, "data1.txt"), "plain");
        var committer = new BatchCommitter(BuildConfig(), new GitLocal(new GitProcess()));

        var outcome = await committer.CommitBatch(["data[1].txt"], CancellationToken.None);

        outcome.ShouldBeOfType<Result<CommitOutcome>.Ok>().Value.CommitSha.ShouldNotBeNull();
        CommitCount().ShouldBe(2);
        HeadChangedFiles().ShouldBe(["data[1].txt"]);
    }

    [Fact]
    public async Task CancellationBeforeWindowClose_CommitsNoPartialBatch()
    {
        BootstrapRepository();
        var canceller = new CancellationTokenSource();
        var time = new FakeTimeProvider();
        var watcher = new FolderWatcher(_root);
        var batcher = new ChangeBatcher(_root, Debounce, SyncIgnoreRules.ForFolder(_root), time);
        var committer = new BatchCommitter(BuildConfig(), new GitLocal(new GitProcess()));
        var batches = new List<IReadOnlyList<string>>();
        batcher.BatchReady += batches.Add;
        watcher.ChangeDetected += batcher.Accept;
        watcher.Start(canceller.Token);
        try
        {
            File.WriteAllText(Path.Combine(_root, "half-done.txt"), "in progress");
            await WaitForPendingAsync(batcher);

            canceller.Cancel();
            batcher.Dispose();
            time.Advance(Debounce);

            batches.ShouldBeEmpty();
            CommitCount().ShouldBe(1);
        }
        finally
        {
            batcher.Dispose();
            watcher.Dispose();
        }
    }

    private (FolderWatcher Watcher, ChangeBatcher Batcher, BatchCommitter Committer, FakeTimeProvider Time, List<IReadOnlyList<string>> Batches)
        StartPipeline()
    {
        var time = new FakeTimeProvider();
        var watcher = new FolderWatcher(_root);
        var batcher = new ChangeBatcher(_root, Debounce, SyncIgnoreRules.ForFolder(_root), time);
        var committer = new BatchCommitter(BuildConfig(), new GitLocal(new GitProcess()));
        var batches = new List<IReadOnlyList<string>>();
        batcher.BatchReady += batches.Add;
        watcher.ChangeDetected += batcher.Accept;
        return (watcher, batcher, committer, time, batches);
    }

    private SyncConfig BuildConfig() => new()
    {
        WatchedFolder = _root,
        RemoteUrl = "https://github.com/acme/private.git",
        Branch = "main",
        Mode = SyncMode.Git
    };

    private void BootstrapRepository(bool withSyncIgnore = false)
    {
        if (withSyncIgnore)
        {
            File.WriteAllText(Path.Combine(_root, ".syncignore"), "drafts/\n*.key\n");
        }

        Directory.CreateDirectory(Path.Combine(_root, "drafts"));
        File.WriteAllText(Path.Combine(_root, "readme.md"), "bootstrap");
        GitCli.Ensure(_root, "init", "-b", "main");
        GitCli.ConfigureIdentity(_root);
        GitCli.CommitAll(_root, "bootstrap");
    }

    private static void RunWindows(FakeTimeProvider time, List<IReadOnlyList<string>> batches, int maxWindows)
    {
        for (var window = 0; window < maxWindows && batches.Count == 0; window++)
        {
            time.Advance(Debounce);
        }
    }

    private static async Task WaitForPendingAsync(ChangeBatcher batcher)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (batcher.PendingCount == 0 && DateTime.UtcNow < limit)
        {
            await Task.Delay(25);
        }

        batcher.PendingCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    private static async Task WaitForDeliveryToSettleAsync(ChangeBatcher batcher)
    {
        await WaitForPendingAsync(batcher);
        var quietSince = DateTime.UtcNow;
        var lastCount = batcher.PendingCount;
        while (DateTime.UtcNow - quietSince < TimeSpan.FromMilliseconds(500))
        {
            await Task.Delay(50);
            if (batcher.PendingCount != lastCount)
            {
                lastCount = batcher.PendingCount;
                quietSince = DateTime.UtcNow;
            }
        }
    }

    private int CommitCount() =>
        int.Parse(GitCli.Run(_root, "rev-list", "--count", "HEAD").Output.Trim().Split('\n')[0]);

    private string HeadSubject() =>
        GitCli.Run(_root, "log", "-1", "--pretty=%s").Output.Trim();

    private IReadOnlyList<string> HeadChangedFiles() =>
        GitCli.Run(_root, "show", "--name-only", "--format=", "HEAD").Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToList();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }
}
