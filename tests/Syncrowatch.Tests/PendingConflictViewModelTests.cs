using Shouldly;
using Syncrowatch.App.History;
using Syncrowatch.Core.Engine.Conflicts;
using Syncrowatch.Core.History;
using HistoryConflict = Syncrowatch.Core.History.PendingConflict;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class PendingConflictViewModelTests
{
    [Fact]
    public void Versions_ExposeLocalTheirsBaseFileNames()
    {
        var conflict = new HistoryConflict(
            "src/app.config", "app (mine).config", "app (theirs).config", DateTimeOffset.UtcNow);
        var viewModel = new PendingConflictViewModel(conflict, DateTimeOffset.UtcNow);

        viewModel.LocalVersion.ShouldBe("app (mine).config");
        viewModel.TheirsVersion.ShouldBe("app (theirs).config");
        viewModel.BaseVersion.ShouldBe("app.config");
    }

    [Fact]
    public async Task DropCommand_InvokesResolverWithKeepMine()
    {
        var recorded = new List<(string Path, ConflictOption Option)>();
        var viewModel = CreateViewModel(Resolve(recorded));

        await viewModel.DropCommand.ExecuteAsync(null);

        recorded.ShouldHaveSingleItem().ShouldBe(("src/app.config", ConflictOption.KeepMine));
    }

    [Fact]
    public async Task ReplaceCommand_InvokesResolverWithTakeTheirs()
    {
        var recorded = new List<(string Path, ConflictOption Option)>();
        var viewModel = CreateViewModel(Resolve(recorded));

        await viewModel.ReplaceCommand.ExecuteAsync(null);

        recorded.ShouldHaveSingleItem().ShouldBe(("src/app.config", ConflictOption.TakeTheirs));
    }

    [Fact]
    public async Task MergeCommand_InvokesResolverWithKeepCopy()
    {
        var recorded = new List<(string Path, ConflictOption Option)>();
        var viewModel = CreateViewModel(Resolve(recorded));

        await viewModel.MergeCommand.ExecuteAsync(null);

        recorded.ShouldHaveSingleItem().ShouldBe(("src/app.config", ConflictOption.KeepCopy));
    }

    [Fact]
    public void WithoutResolver_CommandsAreDisabled()
    {
        var conflict = new HistoryConflict("notes.txt", "notes.txt", "notes.txt", DateTimeOffset.UtcNow);
        var viewModel = new PendingConflictViewModel(conflict, DateTimeOffset.UtcNow);

        viewModel.CanResolve.ShouldBeFalse();
        viewModel.DropCommand.CanExecute(null).ShouldBeFalse();
        viewModel.ReplaceCommand.CanExecute(null).ShouldBeFalse();
        viewModel.MergeCommand.CanExecute(null).ShouldBeFalse();
    }

    private static PendingConflictViewModel CreateViewModel(ConflictAction resolve) =>
        new(new HistoryConflict("src/app.config", "app (mine).config", "app (theirs).config", DateTimeOffset.UtcNow),
            DateTimeOffset.UtcNow,
            resolve);

    private static ConflictAction Resolve(List<(string Path, ConflictOption Option)> recorded) =>
        (path, option) =>
        {
            recorded.Add((path, option));
            return Task.CompletedTask;
        };
}
