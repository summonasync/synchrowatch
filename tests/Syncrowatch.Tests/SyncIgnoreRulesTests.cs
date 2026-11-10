using Shouldly;
using Syncrowatch.Core.Engine;
using Xunit;

namespace Syncrowatch.Tests;

public class SyncIgnoreRulesTests
{
    [Fact]
    public void ForFolder_WithoutSyncIgnoreFile_KeepsVersionControlDirectoriesOut()
    {
        var rules = SyncIgnoreRules.ForFolder(TempDir.Create());

        rules.IsIgnored(".git/HEAD").ShouldBeTrue();
        rules.IsIgnored(".git/objects/ab/cdef").ShouldBeTrue();
        rules.IsIgnored(".svn/entries").ShouldBeTrue();
        rules.IsIgnored("src/.hg/store").ShouldBeTrue();
    }

    [Fact]
    public void ForFolder_WithoutSyncIgnoreFile_KeepsBuildDirectoriesOut()
    {
        var rules = SyncIgnoreRules.ForFolder(TempDir.Create());

        rules.IsIgnored("src/bin/Debug/app.dll").ShouldBeTrue();
        rules.IsIgnored("obj/project.assets.json").ShouldBeTrue();
        rules.IsIgnored(".idea/workspace.xml").ShouldBeTrue();
        rules.IsIgnored(".vs/solution.state").ShouldBeTrue();
        rules.IsIgnored(".synchro/state.json").ShouldBeTrue();
    }

    [Fact]
    public void ForFolder_WithoutSyncIgnoreFile_KeepsTempAndOsJunkOut()
    {
        var rules = SyncIgnoreRules.ForFolder(TempDir.Create());

        rules.IsIgnored("report.tmp").ShouldBeTrue();
        rules.IsIgnored("docs/draft.tmp").ShouldBeTrue();
        rules.IsIgnored("~$quarter.docx").ShouldBeTrue();
        rules.IsIgnored("setup.part").ShouldBeTrue();
        rules.IsIgnored("movie.crdownload").ShouldBeTrue();
        rules.IsIgnored(".DS_Store").ShouldBeTrue();
        rules.IsIgnored("deep/Thumbs.db").ShouldBeTrue();
        rules.IsIgnored("desktop.ini").ShouldBeTrue();
    }

    [Fact]
    public void ForFolder_WithoutSyncIgnoreFile_KeepsRegularContent()
    {
        var rules = SyncIgnoreRules.ForFolder(TempDir.Create());

        rules.IsIgnored("readme.md").ShouldBeFalse();
        rules.IsIgnored("src/Engine/App.cs").ShouldBeFalse();
        rules.IsIgnored("notes/project.txt").ShouldBeFalse();
    }

    [Fact]
    public void SyncIgnoreFile_CommentsAndBlankLines_AreSkipped()
    {
        var rules = SyncIgnoreRules.FromLines(["# section", "", "   ", "*.log"]);

        rules.IsIgnored("run.log").ShouldBeTrue();
        rules.IsIgnored("run.txt").ShouldBeFalse();
    }

    [Fact]
    public void SyncIgnoreFile_UnanchoredPattern_MatchesAtAnyDepth()
    {
        var rules = SyncIgnoreRules.FromLines(["*.log"]);

        rules.IsIgnored("debug.log").ShouldBeTrue();
        rules.IsIgnored("logs/nested/debug.log").ShouldBeTrue();
    }

    [Fact]
    public void SyncIgnoreFile_AnchoredPattern_MatchesRootOnly()
    {
        var rules = SyncIgnoreRules.FromLines(["/temp.txt"]);

        rules.IsIgnored("temp.txt").ShouldBeTrue();
        rules.IsIgnored("sub/temp.txt").ShouldBeFalse();
    }

    [Fact]
    public void SyncIgnoreFile_DirectoryPattern_CoversDescendants()
    {
        var rules = SyncIgnoreRules.FromLines(["out/"]);

        rules.IsIgnored("out/a.txt").ShouldBeTrue();
        rules.IsIgnored("out/nested/b.txt").ShouldBeTrue();
        rules.IsIgnored("stdout.txt").ShouldBeFalse();
    }

    [Fact]
    public void SyncIgnoreFile_Negation_LastMatchWins()
    {
        var rules = SyncIgnoreRules.FromLines(["*.log", "!keep.log"]);

        rules.IsIgnored("noise.log").ShouldBeTrue();
        rules.IsIgnored("keep.log").ShouldBeFalse();
        rules.IsIgnored("sub/keep.log").ShouldBeFalse();
    }

    [Fact]
    public void SyncIgnoreFile_CannotReincludeInsideIgnoredDirectory()
    {
        var rules = SyncIgnoreRules.FromLines(["out/", "!out/special.txt"]);

        rules.IsIgnored("out/special.txt").ShouldBeTrue();
    }

    [Fact]
    public void SyncIgnoreFile_DoubleStar_MatchesDeepTrees()
    {
        var rules = SyncIgnoreRules.FromLines(["**/vendor/**", "logs/**"]);

        rules.IsIgnored("vendor/x.dll").ShouldBeTrue();
        rules.IsIgnored("src/vendor/deep/x.dll").ShouldBeTrue();
        rules.IsIgnored("myvendor/x.dll").ShouldBeFalse();
        rules.IsIgnored("logs/today.txt").ShouldBeTrue();
        rules.IsIgnored("logs").ShouldBeFalse();
    }

    [Fact]
    public void SyncIgnoreFile_QuestionMark_MatchesSingleCharacter()
    {
        var rules = SyncIgnoreRules.FromLines(["file?.txt"]);

        rules.IsIgnored("fileA.txt").ShouldBeTrue();
        rules.IsIgnored("fileAB.txt").ShouldBeFalse();
    }

    [Fact]
    public void SyncIgnoreFile_CharacterClass_MatchesSet()
    {
        var rules = SyncIgnoreRules.FromLines(["cache[0-9].bin"]);

        rules.IsIgnored("cache3.bin").ShouldBeTrue();
        rules.IsIgnored("cacheX.bin").ShouldBeFalse();
    }
}
