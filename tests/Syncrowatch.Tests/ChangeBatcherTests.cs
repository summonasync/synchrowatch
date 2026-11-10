using Shouldly;
using Syncrowatch.Core.Engine;
using Xunit;

namespace Syncrowatch.Tests;

public class ChangeBatcherTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(20);

    [Fact]
    public void Accept_RapidChangesToSamePath_EmitsSingleCoalescedBatch()
    {
        var root = TempDir.Create();
        var file = Path.Combine(root, "notes.txt");
        File.WriteAllText(file, "draft");
        var time = new FakeTimeProvider();
        using var batcher = BuildBatcher(root, time);
        var batches = Capture(batcher);

        for (var i = 0; i < 10; i++)
        {
            File.WriteAllText(file, $"draft {i}");
            batcher.Accept(new FolderChange("notes.txt", WatchChangeKind.Changed));
        }

        time.Advance(Debounce);
        batches.Count.ShouldBe(1);
        batches[0].ShouldBe(["notes.txt"]);
    }

    [Fact]
    public void Accept_DistinctPaths_EmitsAllOfThemInOneBatch()
    {
        var root = TempDir.Create();
        string[] created = ["a.txt", "b.txt", "sub/c.txt"];
        foreach (var name in created)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, name))!);
            File.WriteAllText(Path.Combine(root, name), "x");
        }

        var time = new FakeTimeProvider();
        using var batcher = BuildBatcher(root, time);
        var batches = Capture(batcher);

        batcher.Accept(new FolderChange("a.txt", WatchChangeKind.Created));
        batcher.Accept(new FolderChange("b.txt", WatchChangeKind.Changed));
        batcher.Accept(new FolderChange("sub/c.txt", WatchChangeKind.Changed));
        time.Advance(Debounce);

        batches.Count.ShouldBe(1);
        batches[0].Order().ShouldBe(["a.txt", "b.txt", "sub/c.txt"]);
    }

    [Fact]
    public void Accept_IgnoredPaths_NeverEnterTheWindow()
    {
        var root = TempDir.Create();
        var time = new FakeTimeProvider();
        using var batcher = BuildBatcher(root, time);
        var batches = Capture(batcher);

        batcher.Accept(new FolderChange("bin/Debug/app.dll", WatchChangeKind.Created));
        batcher.Accept(new FolderChange("report.tmp", WatchChangeKind.Created));
        batcher.Accept(new FolderChange("~$secret.docx", WatchChangeKind.Changed));
        batcher.Accept(new FolderChange(".synchro/state.json", WatchChangeKind.Changed));
        batcher.Accept(new FolderChange(".git/HEAD", WatchChangeKind.Changed));
        time.Advance(Debounce);

        batches.ShouldBeEmpty();
        batcher.PendingCount.ShouldBe(0);
    }

    [Fact]
    public void Accept_SyncIgnorePattern_NeverEnterTheWindow()
    {
        var root = TempDir.Create();
        File.WriteAllText(Path.Combine(root, ".syncignore"), "secrets/\nscratch.txt\n");
        var rules = SyncIgnoreRules.ForFolder(root);
        var time = new FakeTimeProvider();
        using var batcher = new ChangeBatcher(root, Debounce, rules, time);
        var batches = Capture(batcher);

        batcher.Accept(new FolderChange("secrets/key.pem", WatchChangeKind.Created));
        batcher.Accept(new FolderChange("scratch.txt", WatchChangeKind.Created));
        time.Advance(Debounce);

        batches.ShouldBeEmpty();
    }

    [Fact]
    public void Accept_DirectoryEvent_IsDropped()
    {
        var root = TempDir.Create();
        Directory.CreateDirectory(Path.Combine(root, "newdir"));
        var time = new FakeTimeProvider();
        using var batcher = BuildBatcher(root, time);
        var batches = Capture(batcher);

        batcher.Accept(new FolderChange("newdir", WatchChangeKind.Created));
        time.Advance(Debounce);

        batcher.PendingCount.ShouldBe(0);
        batches.ShouldBeEmpty();
    }

    [Fact]
    public void Flush_QuietFile_ClosesAtDebounceNotEarlier()
    {
        var root = TempDir.Create();
        File.WriteAllText(Path.Combine(root, "notes.txt"), "stable");
        var time = new FakeTimeProvider();
        using var batcher = BuildBatcher(root, time);
        var batches = Capture(batcher);

        batcher.Accept(new FolderChange("notes.txt", WatchChangeKind.Changed));
        time.Advance(Debounce - TimeSpan.FromMilliseconds(1));

        batches.ShouldBeEmpty();
        time.RequestedDueTimes.ShouldContain(Debounce);

        time.Advance(TimeSpan.FromMilliseconds(10));
        batches.Count.ShouldBe(1);
    }

    [Fact]
    public void Accept_LateEvent_SlidesTheDeadlineForward()
    {
        var root = TempDir.Create();
        File.WriteAllText(Path.Combine(root, "a.txt"), "a");
        File.WriteAllText(Path.Combine(root, "b.txt"), "b");
        var time = new FakeTimeProvider();
        using var batcher = BuildBatcher(root, time);
        var batches = Capture(batcher);

        batcher.Accept(new FolderChange("a.txt", WatchChangeKind.Created));
        time.Advance(Debounce - TimeSpan.FromSeconds(5));
        batcher.Accept(new FolderChange("b.txt", WatchChangeKind.Created));
        time.Advance(TimeSpan.FromSeconds(6));

        batches.ShouldBeEmpty();
        time.Advance(Debounce - TimeSpan.FromSeconds(1));
        batches.Count.ShouldBe(1);
    }

    [Fact]
    public void Flush_FileChangedSinceAccept_DefersToNextWindow()
    {
        var root = TempDir.Create();
        var file = Path.Combine(root, "growing.csv");
        File.WriteAllText(file, "row1");
        var time = new FakeTimeProvider();
        using var batcher = BuildBatcher(root, time);
        var batches = Capture(batcher);

        batcher.Accept(new FolderChange("growing.csv", WatchChangeKind.Changed));
        File.WriteAllText(file, "row1\nrow2\n");
        time.Advance(Debounce);

        batches.ShouldBeEmpty();
        batcher.PendingCount.ShouldBe(1);

        time.Advance(Debounce);
        batches.Count.ShouldBe(1);
        batches[0].ShouldBe(["growing.csv"]);
    }

    [Fact]
    public void Flush_DeletedPath_StillAbsent_EmitsBatch()
    {
        var root = TempDir.Create();
        File.WriteAllText(Path.Combine(root, "obsolete.txt"), "old");
        var time = new FakeTimeProvider();
        using var batcher = BuildBatcher(root, time);
        var batches = Capture(batcher);

        File.Delete(Path.Combine(root, "obsolete.txt"));
        batcher.Accept(new FolderChange("obsolete.txt", WatchChangeKind.Deleted));
        time.Advance(Debounce);

        batches.Count.ShouldBe(1);
        batches[0].ShouldBe(["obsolete.txt"]);
    }

    [Fact]
    public void Flush_DeletedPath_RecreatedAfterEvent_DefersOnce()
    {
        var root = TempDir.Create();
        File.WriteAllText(Path.Combine(root, "phoenix.txt"), "old");
        File.Delete(Path.Combine(root, "phoenix.txt"));
        var time = new FakeTimeProvider();
        using var batcher = BuildBatcher(root, time);
        var batches = Capture(batcher);

        batcher.Accept(new FolderChange("phoenix.txt", WatchChangeKind.Deleted));
        File.WriteAllText(Path.Combine(root, "phoenix.txt"), "reborn");
        time.Advance(Debounce);

        batches.ShouldBeEmpty();
        time.Advance(Debounce);
        batches.Count.ShouldBe(1);
    }

    [Fact]
    public void Dispose_DropsPendingWindow_EmitsNothing()
    {
        var root = TempDir.Create();
        File.WriteAllText(Path.Combine(root, "pending.txt"), "x");
        var time = new FakeTimeProvider();
        var batcher = BuildBatcher(root, time);
        var batches = Capture(batcher);

        batcher.Accept(new FolderChange("pending.txt", WatchChangeKind.Created));
        batcher.Dispose();
        time.Advance(Debounce);

        batches.ShouldBeEmpty();
        batcher.PendingCount.ShouldBe(0);
    }

    [Fact]
    public void Constructor_NonPositiveDebounce_IsRejected()
    {
        var root = TempDir.Create();
        Should.Throw<ArgumentOutOfRangeException>(
            () => new ChangeBatcher(root, TimeSpan.Zero, SyncIgnoreRules.FromLines(["*.tmp"])));
    }

    private static ChangeBatcher BuildBatcher(string root, FakeTimeProvider time) =>
        new(root, Debounce, SyncIgnoreRules.ForFolder(root), time);

    private static List<IReadOnlyList<string>> Capture(ChangeBatcher batcher)
    {
        var batches = new List<IReadOnlyList<string>>();
        batcher.BatchReady += batches.Add;
        return batches;
    }
}
