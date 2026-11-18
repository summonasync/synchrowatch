using Shouldly;
using Syncrowatch.App.History;
using Syncrowatch.Core.History;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class HistoryEntryViewModelGlyphTests
{
    [Theory]
    [InlineData(SyncEventKind.CommitIn, "=>")]
    [InlineData(SyncEventKind.CommitOut, "()")]
    [InlineData(SyncEventKind.AutoMerged, "{}")]
    [InlineData(SyncEventKind.MergeConflicted, "!=")]
    [InlineData(SyncEventKind.SyncFailed, ";")]
    public void Glyph_MapsEventKindToCodeToken(SyncEventKind kind, string expected)
    {
        var entry = new HistoryEntryViewModel(
            new SyncEvent(1, DateTimeOffset.UtcNow, kind, "main", "detail", []),
            DateTimeOffset.UtcNow);

        entry.Glyph.ShouldBe(expected);
    }

    [Theory]
    [InlineData(SyncEventKind.MergeConflicted, true)]
    [InlineData(SyncEventKind.SyncFailed, true)]
    [InlineData(SyncEventKind.CommitIn, false)]
    [InlineData(SyncEventKind.CommitOut, false)]
    [InlineData(SyncEventKind.AutoMerged, false)]
    public void IsAttention_FlagsConflictAndFailureKinds(SyncEventKind kind, bool expected)
    {
        var entry = new HistoryEntryViewModel(
            new SyncEvent(1, DateTimeOffset.UtcNow, kind, "main", "detail", []),
            DateTimeOffset.UtcNow);

        entry.IsAttention.ShouldBe(expected);
    }

    [Fact]
    public void IsLatest_DefaultsToFalseWithoutFlag()
    {
        var entry = new HistoryEntryViewModel(
            new SyncEvent(1, DateTimeOffset.UtcNow, SyncEventKind.CommitIn, "main", "detail", []),
            DateTimeOffset.UtcNow);

        entry.IsLatest.ShouldBeFalse();
    }

    [Fact]
    public void HasFiles_ReflectsEventPaths()
    {
        var withFiles = new HistoryEntryViewModel(
            new SyncEvent(1, DateTimeOffset.UtcNow, SyncEventKind.CommitIn, "main", "detail", ["a.txt"]),
            DateTimeOffset.UtcNow);
        var withoutFiles = new HistoryEntryViewModel(
            new SyncEvent(2, DateTimeOffset.UtcNow, SyncEventKind.CommitIn, "main", "detail", []),
            DateTimeOffset.UtcNow);

        withFiles.HasFiles.ShouldBeTrue();
        withoutFiles.HasFiles.ShouldBeFalse();
    }
}
