using NSubstitute;
using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.App.History;
using Syncrowatch.Core.Engine.Conflicts;
using Syncrowatch.Core.History;
using HistoryConflict = Syncrowatch.Core.History.PendingConflict;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class HistoryConflictResolveTests : IDisposable
{
    private readonly string _directory = TempDir.Create();

    [Fact]
    public async Task MergeCommand_ResolvedSuccessfully_ReloadsPendingConflicts()
    {
        var pending = new MutablePendingReader([new HistoryConflict("notes.txt", "notes.txt", "notes.txt", DateTimeOffset.UtcNow)]);
        var resolver = new StubResolver(pending);
        var viewModel = CreateViewModel(pending, resolver);

        await viewModel.RefreshAsync();
        viewModel.PendingConflicts.ShouldHaveSingleItem();

        await viewModel.PendingConflicts[0].MergeCommand.ExecuteAsync(null);

        resolver.Calls.ShouldHaveSingleItem().ShouldBe(("notes.txt", ConflictOption.KeepCopy));
        viewModel.PendingConflicts.ShouldBeEmpty();
        viewModel.HasError.ShouldBeFalse();
    }

    [Fact]
    public async Task DropCommand_ResolverRefusal_SurfacesErrorAndKeepsConflict()
    {
        var pending = new MutablePendingReader([new HistoryConflict("notes.txt", "notes.txt", "notes.txt", DateTimeOffset.UtcNow)]);
        var viewModel = CreateViewModel(pending, new StubResolver(pending, new SyncError("unavailable", "merge head is gone")));

        await viewModel.RefreshAsync();

        await viewModel.PendingConflicts[0].DropCommand.ExecuteAsync(null);

        viewModel.HasError.ShouldBeTrue();
        viewModel.ErrorMessage.ShouldNotBeNull().ShouldContain("merge head is gone");
        viewModel.PendingConflicts.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task EmptyTimelineWithoutErrors_ShowsEmptyState()
    {
        var viewModel = CreateViewModel(new MutablePendingReader([]), new StubResolver(new MutablePendingReader([])));

        viewModel.IsEmpty.ShouldBeTrue();
        viewModel.HasEntries.ShouldBeFalse();

        await viewModel.RefreshAsync();

        viewModel.HasEntries.ShouldBeFalse();
        viewModel.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task LoadedTimeline_FlagsNewestEntryAsLatest()
    {
        var store = new JsonSyncEventStore(_directory);
        await store.Append(new SyncEventDraft(SyncEventKind.CommitIn, "main", "aaa..bbb", []), CancellationToken.None);
        await store.Append(new SyncEventDraft(SyncEventKind.CommitOut, "main", "pushed bbb", []), CancellationToken.None);
        var viewModel = CreateViewModel(new MutablePendingReader([]), new StubResolver(new MutablePendingReader([])), store);

        await viewModel.RefreshAsync();

        viewModel.HasEntries.ShouldBeTrue();
        viewModel.IsEmpty.ShouldBeFalse();
        viewModel.Entries[0].IsLatest.ShouldBeTrue();
        viewModel.Entries[1].IsLatest.ShouldBeFalse();
    }

    [Fact]
    public async Task StoreFailure_EmptyStateIsSuppressed()
    {
        var store = Substitute.For<ISyncEventStore>();
        store.Query(Arg.Any<HistoryQuery>(), Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<SyncEvent>>.Fail(new SyncError("unavailable", "history file unreadable")));
        var viewModel = CreateViewModel(new MutablePendingReader([]), new StubResolver(new MutablePendingReader([])), store);

        await viewModel.RefreshAsync();

        viewModel.HasError.ShouldBeTrue();
        viewModel.IsEmpty.ShouldBeFalse();
    }

    private HistoryViewModel CreateViewModel(
        MutablePendingReader pending,
        StubResolver resolver,
        ISyncEventStore? store = null) =>
        new(store ?? new JsonSyncEventStore(_directory), pending, resolver);

    public void Dispose() => Directory.Delete(_directory, true);

    private sealed class MutablePendingReader(IReadOnlyList<HistoryConflict> initial) : IPendingConflictReader
    {
        private IReadOnlyList<HistoryConflict> _current = initial;

        public void Clear() => _current = [];

        public Task<Result<IReadOnlyList<HistoryConflict>>> Read(CancellationToken cancellationToken) =>
            Task.FromResult<Result<IReadOnlyList<HistoryConflict>>>(new Result<IReadOnlyList<HistoryConflict>>.Ok(_current));
    }

    private sealed class StubResolver(MutablePendingReader pending, SyncError? failure = null) : IConflictResolver
    {
        public List<(string Path, ConflictOption Option)> Calls { get; } = [];

        public Task<Result<ConflictResolution>> Resolve(
            string relativePath, ConflictOption option, CancellationToken cancellationToken)
        {
            Calls.Add((relativePath, option));
            if (failure is { } refusal)
            {
                return Task.FromResult<Result<ConflictResolution>>(new Result<ConflictResolution>.Fail(refusal));
            }

            pending.Clear();
            return Task.FromResult<Result<ConflictResolution>>(
                new Result<ConflictResolution>.Ok(new ConflictResolution([relativePath], null, [])));
        }
    }
}
