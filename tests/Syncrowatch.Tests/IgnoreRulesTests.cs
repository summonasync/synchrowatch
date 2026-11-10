using Shouldly;
using Syncrowatch.Core.Bootstrap;
using Xunit;

namespace Syncrowatch.Tests;

public class IgnoreRulesTests
{
    [Fact]
    public void Default_CoversBuiltInGroups()
    {
        var patterns = DefaultIgnoreRules.Patterns;

        patterns.ShouldContain(".git/");
        patterns.ShouldContain(".hg/");
        patterns.ShouldContain(".svn/");
        patterns.ShouldContain("bin/");
        patterns.ShouldContain("obj/");
        patterns.ShouldContain(".idea/");
        patterns.ShouldContain(".vs/");
        patterns.ShouldContain(".synchro/");
        patterns.ShouldContain(".DS_Store");
        patterns.ShouldContain("Thumbs.db");
        patterns.ShouldContain("*.tmp");
        patterns.ShouldContain("~$*");
        patterns.ShouldContain("*.part");
        patterns.ShouldContain("*.crdownload");
    }

    [Fact]
    public void Load_WithoutSyncignoreFile_KeepsDefaultsOnly()
    {
        var folder = TempDir.Create();
        try
        {
            var rules = IgnoreRules.Load(folder);

            rules.Patterns.ShouldBe(DefaultIgnoreRules.Patterns);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Load_WithSyncignoreFile_AppendsPatternsAfterDefaults()
    {
        var folder = TempDir.Create();
        try
        {
            File.WriteAllLines(Path.Combine(folder, ".syncignore"), ["# build scratch", "", "  logs/  ", "*.secret", "   "]);

            var rules = IgnoreRules.Load(folder);

            rules.Patterns.ShouldBe([.. DefaultIgnoreRules.Patterns, "logs/", "*.secret"]);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Load_RepeatedReads_NeverMutateTheDefaultSet()
    {
        var folder = TempDir.Create();
        try
        {
            File.WriteAllText(Path.Combine(folder, ".syncignore"), "scratch/\n");

            IgnoreRules.Load(folder);
            IgnoreRules.Load(folder);

            IgnoreRules.Default.Patterns.ShouldBe(DefaultIgnoreRules.Patterns);
            DefaultIgnoreRules.Patterns.ShouldNotContain("scratch/");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
