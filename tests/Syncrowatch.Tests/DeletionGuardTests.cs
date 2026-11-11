using Shouldly;
using Syncrowatch.Core.Engine.Conflicts;
using Xunit;

namespace Syncrowatch.Tests;

public class DeletionGuardTests : IDisposable
{
    private readonly string _root = TempDir.Create();

    [Fact]
    public void RemotelyDeletedItem_SurvivesWhenCanonicalFileIsPresent()
    {
        File.WriteAllText(Path.Combine(_root, "kept.txt"), "ours");
        var item = new ConflictItem("kept.txt", ConflictKind.ModifiedLocallyDeletedRemotely, null);

        DeletionGuard.Survives(item, _root).ShouldBeTrue();
    }

    [Fact]
    public void RemotelyDeletedItem_FailsWhenCanonicalFileIsGone()
    {
        var item = new ConflictItem("gone.txt", ConflictKind.ModifiedLocallyDeletedRemotely, null);

        DeletionGuard.Survives(item, _root).ShouldBeFalse();
    }

    [Fact]
    public void LocallyDeletedItem_SurvivesWhenConflictCopyIsMaterialized()
    {
        var item = new ConflictItem("restored.txt", ConflictKind.DeletedLocallyModifiedRemotely, "restored (theirs 2026-11-11 101010).txt");
        File.WriteAllText(Path.Combine(_root, "restored (theirs 2026-11-11 101010).txt"), "theirs");

        DeletionGuard.Survives(item, _root).ShouldBeTrue();
    }

    [Fact]
    public void LocallyDeletedItem_FailsWithoutConflictCopy()
    {
        var item = new ConflictItem("restored.txt", ConflictKind.DeletedLocallyModifiedRemotely, "restored (theirs 2026-11-11 101010).txt");

        DeletionGuard.Survives(item, _root).ShouldBeFalse();
    }

    [Fact]
    public void ContentConflicts_AlwaysSurvive()
    {
        DeletionGuard.Survives(new ConflictItem("a.txt", ConflictKind.TextConflict, null), _root).ShouldBeTrue();
        DeletionGuard.Survives(new ConflictItem("b.bin", ConflictKind.BinaryConflict, null), _root).ShouldBeTrue();
    }

    public void Dispose() => Directory.Delete(_root, true);
}
