using Shouldly;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public class GitArgumentGuardsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("-dash.txt")]
    [InlineData("a:colon.txt")]
    [InlineData("dir/sub:colon")]
    [InlineData("/absolute.txt")]
    [InlineData(@"\\srv\share\doc.txt")]
    [InlineData("../escape.txt")]
    [InlineData("folder/../../escape.txt")]
    public void RelativePath_RejectsUnusableValues(string relativePath)
    {
        Should.Throw<GitOperationException>(() => GitArgumentGuards.RelativePath(relativePath));
    }

    [Fact]
    public void RelativePath_AcceptsDashNamedFolderInsidePath()
    {
        GitArgumentGuards.RelativePath("folder/-leading/file.txt").ShouldBe("folder/-leading/file.txt");
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("dir/sub/file with spaces.txt")]
    [InlineData("src/Engine/App.cs")]
    [InlineData("weird[name].txt")]
    public void RelativePath_AcceptsRegularRepositoryPaths(string relativePath)
    {
        GitArgumentGuards.RelativePath(relativePath).ShouldBe(relativePath.Replace('\\', '/'));
    }

    [Fact]
    public void RelativePath_NormalizesWindowsSeparators()
    {
        GitArgumentGuards.RelativePath("dir\\sub\\file.txt").ShouldBe("dir/sub/file.txt");
    }

    [Theory]
    [InlineData("")]
    [InlineData("-main")]
    [InlineData("feature:next")]
    [InlineData("  ")]
    public void Branch_RejectsUnusableRefNames(string branch)
    {
        Should.Throw<GitOperationException>(() => GitArgumentGuards.Branch(branch));
    }

    [Fact]
    public void Branch_AcceptsPlainName()
    {
        GitArgumentGuards.Branch("feature/next").ShouldBe("feature/next");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not hex")]
    [InlineData("zzzz1234")]
    public void Sha_RejectsNonHexadecimalValues(string sha)
    {
        Should.Throw<GitOperationException>(() => GitArgumentGuards.Sha(sha));
    }

    [Fact]
    public void Sha_AcceptsHexadecimalObjectId()
    {
        GitArgumentGuards.Sha("a1b2c3d4e5f6789012345678901234567890abcd").ShouldBe("a1b2c3d4e5f6789012345678901234567890abcd");
    }
}
