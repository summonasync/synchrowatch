using Shouldly;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public class GitLocalDirtyPathsTests
{
    [Theory]
    [InlineData("?? untracked.txt", "untracked.txt")]
    [InlineData(" M docs/readme.md", "docs/readme.md")]
    [InlineData("M  staged.txt", "staged.txt")]
    [InlineData("MM both.txt", "both.txt")]
    [InlineData(" D \"ol'd dir/a b.txt\"", "ol'd dir/a b.txt")]
    [InlineData("?? \"new\\\"dir/\"", "new\"dir/")]
    [InlineData("R  old.txt -> new/n.txt", "old.txt", "new/n.txt")]
    [InlineData("R  plain.txt -> \"re n\\303\\242m\\303\\251d.txt\"", "plain.txt", "re nâméd.txt")]
    [InlineData("R  \"ol'd dir/a b.txt\" -> \"new\\\"dir/x.txt\"", "ol'd dir/a b.txt", "new\"dir/x.txt")]
    public void ParseDirty_ReadsUntrackedModifiedStagedQuotedAndRenameLines(
        string output, params string[] expected)
    {
        GitLocal.ParseDirty(output).ShouldBe(expected);
    }

    [Fact]
    public void ParseDirty_SplitsRecordsAndKeepsBothRenameSides()
    {
        var output = string.Join('\n',
            " M plain.txt",
            "?? notes.txt",
            "A  \"quoted dir/new file.txt\"",
            "R  old spot.txt -> \"new spot/x.txt\"",
            " D \"gone\\\"quote.txt\"");

        GitLocal.ParseDirty(output).ShouldBe(
        [
            "plain.txt",
            "notes.txt",
            "quoted dir/new file.txt",
            "old spot.txt",
            "new spot/x.txt",
            "gone\"quote.txt"
        ]);
    }

    [Fact]
    public void ParseDirty_CleanStatusAndShortRecords_YieldNoPaths()
    {
        GitLocal.ParseDirty(string.Empty).ShouldBeEmpty();
        GitLocal.ParseDirty("M  a\nxy\n").ShouldBe(["a"]);
    }
}
