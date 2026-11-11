using Shouldly;
using Syncrowatch.Core.Engine;
using Xunit;

namespace Syncrowatch.Tests;

public class FolderWatcherTests : IDisposable
{
    private readonly string _root = TempDir.Create();

    [Fact]
    public async Task Start_FileCreated_EmitsTypedChange()
    {
        using var watcher = new FolderWatcher(_root);
        var change = await ExpectChange(watcher, () => File.WriteAllText(Path.Combine(_root, "created.txt"), "x"));

        change.Kind.ShouldBe(WatchChangeKind.Created);
        change.RelativePath.ShouldBe("created.txt");
    }

    [Fact]
    public async Task Start_FileUpdated_EmitsChanged()
    {
        var file = Path.Combine(_root, "existing.txt");
        File.WriteAllText(file, "v1");
        using var watcher = new FolderWatcher(_root);
        var change = await ExpectChange(watcher, () => File.WriteAllText(file, "v2"));

        change.Kind.ShouldBe(WatchChangeKind.Changed);
        change.RelativePath.ShouldBe("existing.txt");
    }

    [Fact]
    public async Task Start_FileDeleted_EmitsDeleted()
    {
        var file = Path.Combine(_root, "gone.txt");
        File.WriteAllText(file, "v1");
        using var watcher = new FolderWatcher(_root);
        var change = await ExpectChange(watcher, () => File.Delete(file));

        change.Kind.ShouldBe(WatchChangeKind.Deleted);
        change.RelativePath.ShouldBe("gone.txt");
    }

    [Fact]
    public async Task Start_FileRenamed_EmitsRemovalAndAppearance()
    {
        var before = Path.Combine(_root, "before.txt");
        File.WriteAllText(before, "content");
        using var watcher = new FolderWatcher(_root);
        var signals = new List<FolderChange>();
        var received = new TaskCompletionSource();
        watcher.ChangeDetected += change =>
        {
            signals.Add(change);
            if (signals.Count == 2)
            {
                received.TrySetResult();
            }
        };
        watcher.Start();

        File.Move(before, Path.Combine(_root, "after.txt"));
        await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

        signals[0].Kind.ShouldBe(WatchChangeKind.Deleted);
        signals[0].RelativePath.ShouldBe("before.txt");
        signals[1].Kind.ShouldBe(WatchChangeKind.Created);
        signals[1].RelativePath.ShouldBe("after.txt");
    }

    [Fact]
    public async Task Start_NestedFile_ReportsRelativeToRoot()
    {
        Directory.CreateDirectory(Path.Combine(_root, "deep", "inner"));
        using var watcher = new FolderWatcher(_root);
        var change = await ExpectChange(
            watcher, () => File.WriteAllText(Path.Combine(_root, "deep", "inner", "leaf.txt"), "x"));

        change.RelativePath.ShouldBe("deep/inner/leaf.txt");
    }

    [Fact]
    public async Task CancelToken_DisposesWatcher_StopsEmitting()
    {
        var file = Path.Combine(_root, "quiet.txt");
        File.WriteAllText(file, "v1");
        using var watcher = new FolderWatcher(_root);
        var canceller = new CancellationTokenSource();
        var seen = new List<FolderChange>();
        watcher.ChangeDetected += seen.Add;
        watcher.Start(canceller.Token);
        canceller.Cancel();

        await Task.Delay(150);
        File.WriteAllText(file, "v2");
        await Task.Delay(300);

        seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_PreCancelledToken_DoesNotThrowAndStaysQuiet()
    {
        var file = Path.Combine(_root, "never.txt");
        File.WriteAllText(file, "v1");
        using var watcher = new FolderWatcher(_root);
        var seen = new List<FolderChange>();
        watcher.ChangeDetected += seen.Add;
        watcher.Start(new CancellationToken(true));
        watcher.Dispose();

        await Task.Delay(150);
        File.WriteAllText(file, "v2");
        await Task.Delay(300);

        seen.ShouldBeEmpty();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var watcher = new FolderWatcher(_root);
        watcher.Start();
        watcher.Dispose();
        watcher.Dispose();
    }

    private static async Task<FolderChange> ExpectChange(FolderWatcher watcher, Action trigger)
    {
        var received = new TaskCompletionSource<FolderChange>();
        watcher.ChangeDetected += change => received.TrySetResult(change);
        watcher.Start();

        trigger();
        return await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

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
