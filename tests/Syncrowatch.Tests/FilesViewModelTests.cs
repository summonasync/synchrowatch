using NSubstitute;
using Shouldly;
using Synchro.Core;
using Syncrowatch.App.Files;
using Syncrowatch.App.Notifications;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class FilesViewModelTests : IDisposable
{
    private const string HeadSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SyncedSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly string _directory = TempDir.Create();
    private readonly SyncEventBus _bus = new();
    private readonly List<string> _copied = [];

    [Fact]
    public async Task RefreshAsync_ListsTrackedFilesAsClean()
    {
        var harness = CreateViewModel(git: GitWithFiles("docs/read me.md", "notes.txt", "src/app.cs"));

        await harness.ViewModel.RefreshAsync();

        harness.ViewModel.HasError.ShouldBeFalse();
        harness.ViewModel.VisibleFiles.Select(entry => entry.Path)
            .ShouldBe(["docs/read me.md", "notes.txt", "src/app.cs"]);
        harness.ViewModel.VisibleFiles.Select(entry => entry.StatusText).ShouldBe(["clean", "clean", "clean"]);
    }

    [Fact]
    public async Task RefreshAsync_RendersPendingAndUnpushedGlances()
    {
        var pending = Substitute.For<IPendingPathsSource>();
        pending.PendingPaths.Returns(["notes.txt"]);
        var git = Substitute.For<ILocalGit>();
        git.ListTrackedFiles(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<string>>.Ok(["docs/read me.md", "notes.txt", "src/app.cs"]));
        git.HeadInfo(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<LocalHead>.Ok(new LocalHead("main", HeadSha)));
        git.ChangedPaths(Arg.Any<string>(), SyncedSha, HeadSha, Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<string>>.Ok(["src/app.cs"]));
        var store = new JsonSyncEventStore(_directory);
        await store.Append(new SyncEventDraft(SyncEventKind.CommitOut, "main", $"pushed {SyncedSha}", []),
            CancellationToken.None);
        var harness = CreateViewModel(git: git, pending: pending, events: store);

        await harness.ViewModel.RefreshAsync();

        harness.ViewModel.VisibleFiles.First(entry => entry.Path == "notes.txt").StatusText.ShouldBe("pending batch");
        harness.ViewModel.VisibleFiles.First(entry => entry.Path == "src/app.cs").StatusText.ShouldBe("unpushed");
        harness.ViewModel.VisibleFiles.First(entry => entry.Path == "docs/read me.md").StatusText.ShouldBe("clean");
    }

    [Fact]
    public async Task FilterText_NarrowsTheListCaseInsensitively()
    {
        var harness = CreateViewModel(git: GitWithFiles("docs/read me.md", "notes.txt", "src/app.cs"));
        await harness.ViewModel.RefreshAsync();

        harness.ViewModel.FilterText = "APP";

        harness.ViewModel.VisibleFiles.Select(entry => entry.Path).ShouldBe(["src/app.cs"]);

        harness.ViewModel.FilterText = "nothing matches this";

        harness.ViewModel.VisibleFiles.ShouldBeEmpty();
    }

    [Fact]
    public async Task SelectingFile_LoadsTimelineWithAuthorTimeSubjectAndShortSha()
    {
        var git = GitWithFiles("notes.txt");
        git.LogPath(Arg.Any<string>(), "notes.txt", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<GitLogEntry>>.Ok(
            [
                new GitLogEntry(
                    "0123456789abcdef0123456789abcdef01234567",
                    "Masha Petrova",
                    "masha@example.com",
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    "fix: trim watcher noise")
            ]));
        var harness = CreateViewModel(git: git);
        await harness.ViewModel.RefreshAsync();

        harness.ViewModel.SelectedFile = harness.ViewModel.VisibleFiles[0];
        await WaitForAsync(() => harness.ViewModel.Timeline.Count == 1);

        var commit = harness.ViewModel.Timeline.ShouldHaveSingleItem();
        commit.Author.ShouldBe("Masha Petrova");
        commit.Subject.ShouldBe("fix: trim watcher noise");
        commit.ShortSha.ShouldBe("0123456");
        commit.TimeText.ShouldBe("5m ago");
    }

    [Fact]
    public async Task CopyLink_WritesShaPinnedUrlWithoutCredentialsAndConfirmsWithToast()
    {
        var config = DefaultConfig() with { RemoteUrl = "https://token123@github.com/acme/repo.git" };
        var store = new JsonSyncEventStore(_directory);
        await store.Append(new SyncEventDraft(SyncEventKind.CommitOut, "main", $"pushed {HeadSha}", []),
            CancellationToken.None);
        var harness = CreateViewModel(config: config, git: GitWithFiles("docs/read me.md", "notes.txt"), events: store);
        await harness.ViewModel.RefreshAsync();

        harness.ViewModel.PushPending.ShouldBeFalse();
        harness.ViewModel.SelectedFile = harness.ViewModel.VisibleFiles[0];
        harness.ViewModel.CopyLinkCommand.Execute(null);
        await WaitForAsync(() => _copied.Count == 1);

        _copied.ShouldHaveSingleItem().ShouldBe($"https://github.com/acme/repo/blob/{HeadSha}/docs/read%20me.md");
        var toast = harness.Toasts.Presented.ShouldHaveSingleItem();
        toast.Kind.ShouldBe(ToastKind.LinkCopied);
        toast.Title.ShouldBe("Link copied");
    }

    [Fact]
    public async Task CopyLink_WhilePushPending_WarnsAboutTheMissingPush()
    {
        var store = new JsonSyncEventStore(_directory);
        await store.Append(new SyncEventDraft(SyncEventKind.CommitOut, "main", $"pushed {SyncedSha}", []),
            CancellationToken.None);
        var git = Substitute.For<ILocalGit>();
        git.ListTrackedFiles(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<string>>.Ok(["notes.txt"]));
        git.HeadInfo(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<LocalHead>.Ok(new LocalHead("main", HeadSha)));
        git.ChangedPaths(Arg.Any<string>(), SyncedSha, HeadSha, Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<string>>.Ok(["notes.txt"]));
        var harness = CreateViewModel(git: git, events: store);
        await harness.ViewModel.RefreshAsync();

        harness.ViewModel.PushPending.ShouldBeTrue();
        harness.ViewModel.SelectedFile = harness.ViewModel.VisibleFiles[0];
        harness.ViewModel.CopyLinkCommand.Execute(null);
        await WaitForAsync(() => harness.Toasts.Presented.Count == 1);

        var toast = harness.Toasts.Presented.ShouldHaveSingleItem();
        toast.Kind.ShouldBe(ToastKind.LinkPending);
        toast.Title.ShouldContain("not pushed");
        _copied.ShouldHaveSingleItem().ShouldBe($"https://github.com/acme/repo/blob/{HeadSha}/notes.txt");
    }

    [Fact]
    public async Task CopyLink_NonGithubRemote_DisablesTheActionWithHint()
    {
        var config = DefaultConfig() with { RemoteUrl = "git@github.com:acme/repo.git" };
        var harness = CreateViewModel(config: config, git: GitWithFiles("notes.txt"));
        await harness.ViewModel.RefreshAsync();

        harness.ViewModel.CanCopyLink.ShouldBeFalse();
        harness.ViewModel.HasLinkHint.ShouldBeTrue();
        harness.ViewModel.LinkHint.ShouldNotBeNull().ShouldContain("github.com https remote");
        harness.ViewModel.CopyLinkCommand.CanExecute(null).ShouldBeFalse();

        harness.ViewModel.CopyLinkCommand.Execute(null);

        _copied.ShouldBeEmpty();
        harness.Toasts.Presented.ShouldBeEmpty();
    }

    [Fact]
    public async Task BusEvents_RefreshGlancesForAffectedPathsOnly()
    {
        var head = new Result<LocalHead>.Ok(new LocalHead("main", HeadSha));
        var advancedHead = new Result<LocalHead>.Ok(new LocalHead("main", "cccccccccccccccccccccccccccccccccccccccc"));
        var git = Substitute.For<ILocalGit>();
        git.ListTrackedFiles(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<string>>.Ok(["notes.txt", "src/app.cs"]));
        git.HeadInfo(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(head, advancedHead);
        git.ChangedPaths(Arg.Any<string>(), HeadSha, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<string>>.Ok(["src/app.cs"]));
        var store = new JsonSyncEventStore(_directory);
        await store.Append(new SyncEventDraft(SyncEventKind.CommitOut, "main", $"pushed {HeadSha}", []),
            CancellationToken.None);
        var harness = CreateViewModel(git: git, events: store);
        await harness.ViewModel.RefreshAsync();

        harness.ViewModel.VisibleFiles.Select(entry => entry.StatusText).ShouldBe(["clean", "clean"]);

        _bus.Publish(new SyncNotification.AutoMerged(DateTimeOffset.UtcNow, "main", ["src/app.cs"]));
        await WaitForAsync(() =>
            harness.ViewModel.VisibleFiles.First(entry => entry.Path == "src/app.cs").StatusText == "unpushed");

        harness.ViewModel.VisibleFiles.First(entry => entry.Path == "notes.txt").StatusText.ShouldBe("clean");
    }

    [Fact]
    public async Task PushPendingNotification_MarksTheViewModelUntilSyncSettles()
    {
        var store = new JsonSyncEventStore(_directory);
        await store.Append(new SyncEventDraft(SyncEventKind.CommitOut, "main", $"pushed {SyncedSha}", []),
            CancellationToken.None);
        var harness = CreateViewModel(git: GitWithFiles("notes.txt"), events: store);
        await harness.ViewModel.RefreshAsync();
        harness.ViewModel.PushPending.ShouldBeTrue();

        _bus.Publish(new SyncNotification.PushPending(new PendingPush("main", HeadSha, DateTimeOffset.UtcNow)));

        harness.ViewModel.PushPending.ShouldBeTrue();

        await store.Append(new SyncEventDraft(SyncEventKind.CommitOut, "main", $"pushed {HeadSha}", []),
            CancellationToken.None);
        _bus.Publish(new SyncNotification.SyncCompleted(
            DateTimeOffset.UtcNow, new SyncCycleReport(DateTimeOffset.UtcNow, null, null)));
        await WaitForAsync(() => !harness.ViewModel.PushPending);
    }

    [Fact]
    public void Dispose_StopsListeningToTheBus()
    {
        var harness = CreateViewModel(git: GitWithFiles("notes.txt"));

        harness.ViewModel.Dispose();
        _bus.Publish(new SyncNotification.PushPending(new PendingPush("main", HeadSha, DateTimeOffset.UtcNow)));

        harness.ViewModel.PushPending.ShouldBeFalse();
    }

    private Harness CreateViewModel(
        SyncConfig? config = null,
        ILocalGit? git = null,
        IPendingPathsSource? pending = null,
        ISyncEventStore? events = null)
    {
        var store = Substitute.For<ISyncConfigStore>();
        store.Load(Arg.Any<CancellationToken>())
            .Returns(new Result<SyncConfig>.Ok(config ?? DefaultConfig()));
        var resolvedPending = pending ?? Substitute.For<IPendingPathsSource>();
        if (pending is null)
        {
            resolvedPending.PendingPaths.Returns([]);
        }

        var remote = Substitute.For<IRemoteRepository>();
        remote.TrackingTip(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<string>.Ok(string.Empty));
        var toasts = new RecordingToastFactory();
        var viewModel = new FilesViewModel(
            store,
            git ?? GitWithFiles(),
            events ?? new JsonSyncEventStore(_directory),
            _bus,
            resolvedPending,
            _ => remote,
            new ToastService(_bus, toasts),
            clipboardWriter: text =>
            {
                _copied.Add(text);
                return Task.CompletedTask;
            });
        return new Harness(viewModel, toasts);
    }

    private SyncConfig DefaultConfig() => new()
    {
        WatchedFolder = _directory,
        RemoteUrl = "https://github.com/acme/repo.git",
        Branch = "main"
    };

    private static ILocalGit GitWithFiles(params string[] paths)
    {
        var git = Substitute.For<ILocalGit>();
        git.ListTrackedFiles(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<string>>.Ok(paths));
        git.HeadInfo(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<LocalHead>.Ok(new LocalHead("main", HeadSha)));
        git.LogPath(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<GitLogEntry>>.Ok([]));
        git.ChangedPaths(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<string>>.Ok([]));
        return git;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var budget = TimeSpan.FromSeconds(5);
        while (!condition())
        {
            budget -= TimeSpan.FromMilliseconds(20);
            if (budget < TimeSpan.Zero)
            {
                throw new TimeoutException("files view model never reached the expected state");
            }

            await Task.Delay(20);
        }
    }

    public void Dispose() => Directory.Delete(_directory, true);

    private sealed record Harness(FilesViewModel ViewModel, RecordingToastFactory Toasts);

    private sealed class RecordingToastFactory : IToastWindowFactory
    {
        public List<ToastViewModel> Presented { get; } = [];

        public void Present(ToastViewModel toast, Action expired) => Presented.Add(toast);

        public void Dismiss(ToastViewModel toast)
        {
        }
    }
}
