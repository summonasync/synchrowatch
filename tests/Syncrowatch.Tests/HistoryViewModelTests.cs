using NSubstitute;
using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.App.History;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.History;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class HistoryViewModelTests : IDisposable
{
    private readonly string _directory = TempDir.Create();

    [Fact]
    public async Task RefreshAsync_RendersStoreEventsNewestFirstWithLabels()
    {
        var store = new JsonSyncEventStore(_directory);
        await store.Append(new SyncEventDraft(SyncEventKind.CommitIn, "main", "aaa..bbb", []), CancellationToken.None);
        await store.Append(
            new SyncEventDraft(SyncEventKind.AutoMerged, "main", "merge commit ccc", ["notes.txt"]), CancellationToken.None);
        var viewModel = CreateViewModel(store);

        await viewModel.RefreshAsync();

        viewModel.HasError.ShouldBeFalse();
        viewModel.Entries.Count.ShouldBe(2);
        viewModel.Entries[0].Label.ShouldBe("Auto-merged");
        viewModel.Entries[0].Detail.ShouldBe("merge commit ccc");
        viewModel.Entries[0].Files.ShouldBe("notes.txt");
        viewModel.Entries[0].TimeText.ShouldBe("just now");
        viewModel.Entries[1].Label.ShouldBe("Pulled");
        viewModel.Entries[1].Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task RefreshAsync_PopulatesPendingConflictSection()
    {
        var conflict = new PendingConflict(
            "notes.txt", "notes (mine).txt", "notes (theirs).txt", DateTimeOffset.UtcNow);
        var viewModel = CreateViewModel(
            new JsonSyncEventStore(_directory), new FixedConflictReader([conflict]));

        await viewModel.RefreshAsync();

        viewModel.HasPendingConflicts.ShouldBeTrue();
        var pending = viewModel.PendingConflicts.ShouldHaveSingleItem();
        pending.Path.ShouldBe("notes.txt");
        pending.Summary.ShouldBe("notes (mine).txt vs notes (theirs).txt");
    }

    [Fact]
    public async Task MergeFilter_QueriesOnlyMergeEvents()
    {
        var store = new JsonSyncEventStore(_directory);
        await store.Append(new SyncEventDraft(SyncEventKind.CommitIn, "main", "aaa..bbb", []), CancellationToken.None);
        await store.Append(new SyncEventDraft(SyncEventKind.AutoMerged, "main", "merge commit", ["notes.txt"]), CancellationToken.None);
        await store.Append(new SyncEventDraft(SyncEventKind.SyncFailed, "main", "push refused", []), CancellationToken.None);
        var viewModel = CreateViewModel(store);

        viewModel.ShowMerges = true;
        await viewModel.RefreshAsync();

        viewModel.Entries.Select(entry => entry.Label).ShouldBe(["Auto-merged"]);

        viewModel.ShowFailures = true;
        await viewModel.RefreshAsync();
        viewModel.Entries.Select(entry => entry.Label).ShouldBe(["Failed"]);

        viewModel.ShowAll = true;
        await viewModel.RefreshAsync();
        viewModel.Entries.Count.ShouldBe(3);
    }

    [Fact]
    public async Task RefreshAsync_StoreRefusal_ShowsError()
    {
        var store = Substitute.For<ISyncEventStore>();
        store.Query(Arg.Any<HistoryQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<SyncEvent>>.Fail(new SyncError("unavailable", "history file unreadable")));
        var viewModel = CreateViewModel(store);

        await viewModel.RefreshAsync();

        viewModel.HasError.ShouldBeTrue();
        viewModel.ErrorMessage.ShouldNotBeNull().ShouldContain("history file unreadable");
        viewModel.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void EntryLabel_ForUnmappedStoreKind_ShowsUnknownInsteadOfFailed()
    {
        var entry = new HistoryEntryViewModel(
            new SyncEvent(1, DateTimeOffset.UtcNow, (SyncEventKind)99, "main", "detail", []),
            DateTimeOffset.UtcNow);

        entry.Label.ShouldBe("Unknown");
    }

    [Fact]
    public async Task RefreshCommand_LoadsEntriesThroughTheBus()
    {
        var store = new JsonSyncEventStore(_directory);
        await store.Append(new SyncEventDraft(SyncEventKind.CommitOut, "main", "pushed bbb", []), CancellationToken.None);
        var viewModel = CreateViewModel(store);

        viewModel.RefreshCommand.Execute(null);
        await viewModel.RefreshAsync();

        viewModel.Entries.ShouldHaveSingleItem().Label.ShouldBe("Pushed");
    }

    private HistoryViewModel CreateViewModel(ISyncEventStore store, IPendingConflictReader? conflicts = null) =>
        new(store, conflicts ?? new EmptyPendingConflictReader());

    public void Dispose() => Directory.Delete(_directory, true);

    private sealed class FixedConflictReader(IReadOnlyList<PendingConflict> conflicts) : IPendingConflictReader
    {
        public Task<Result<IReadOnlyList<PendingConflict>>> Read(CancellationToken cancellationToken) =>
            Task.FromResult<Result<IReadOnlyList<PendingConflict>>>(new Result<IReadOnlyList<PendingConflict>>.Ok(conflicts));
    }
}
