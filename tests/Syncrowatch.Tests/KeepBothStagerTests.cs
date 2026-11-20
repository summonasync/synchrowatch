using Shouldly;
using Syncrowatch.Core.Engine.Conflicts;
using Xunit;

namespace Syncrowatch.Tests;

public class KeepBothStagerTests : IDisposable
{
    private readonly string _root = TempDir.Create();

    [Fact]
    public void TextFile_KeepsExtensionAndTimestampLabel()
    {
        var stager = new KeepBothStager(_root, new FixedClock(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero)));

        var copy = stager.MaterializeTheirsCopy("notes.md", [0x61]);

        copy.ShouldBe("notes (theirs 2026-11-11 120000).md");
        File.ReadAllBytes(Path.Combine(_root, copy)).ShouldBe([0x61]);
    }

    [Fact]
    public void ExistingCopy_CollidesIntoNumberedName()
    {
        var stager = new KeepBothStager(_root, new FixedClock(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero)));
        File.WriteAllText(Path.Combine(_root, "doc (theirs 2026-11-11 120000).txt"), "stale");

        var copy = stager.MaterializeTheirsCopy("doc.txt", [0x62]);

        copy.ShouldBe("doc (theirs 2026-11-11 120000) 2.txt");
        File.ReadAllText(Path.Combine(_root, copy)).ShouldBe("b");
    }

    [Fact]
    public void ExtensionlessFile_AppendsLabelAtEnd()
    {
        var stager = new KeepBothStager(_root, new FixedClock(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero)));

        var copy = stager.MaterializeTheirsCopy("Makefile", [0x63]);

        copy.ShouldBe("Makefile (theirs 2026-03-04 050607)");
    }

    [Fact]
    public void NestedFile_KeepsItsDirectory()
    {
        var stager = new KeepBothStager(_root);

        var copy = stager.MaterializeMineCopy("src/app/main.cs", [0x64]);

        copy.ShouldBe($"src/app/main (mine).cs");
        File.ReadAllBytes(Path.Combine(_root, "src", "app", "main (mine).cs")).ShouldBe([0x64]);
    }

    [Fact]
    public void DiscardCopy_RemovesOnlyInsideRoot()
    {
        var stager = new KeepBothStager(_root);
        var copy = stager.MaterializeMineCopy("a.txt", [0x65]);
        File.Exists(Path.Combine(_root, copy)).ShouldBeTrue();

        stager.DiscardCopy(copy);
        File.Exists(Path.Combine(_root, copy)).ShouldBeFalse();

        stager.DiscardCopy("../outside.txt");
        File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "outside.txt")).ShouldBeFalse();
    }

    [Fact]
    public void DiscardCopy_NeverTouchesSiblingSharingPathPrefix()
    {
        var parent = TempDir.Create();
        var root = Path.Combine(parent, "repo");
        var sibling = Path.Combine(parent, "repo-sibling");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "secret.txt"), "keep");
        var stager = new KeepBothStager(root);

        stager.DiscardCopy("../repo-sibling/secret.txt");

        File.Exists(Path.Combine(sibling, "secret.txt")).ShouldBeTrue();
        Directory.Delete(parent, true);
    }

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
