using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public class RemoteRefParserTests
{
    [Fact]
    public void ParseHeads_StripsRefsHeadsPrefix()
    {
        var refs = RemoteRefParser.ParseHeads("aaa111\trefs/heads/main\nbbb222\trefs/heads/feature/x\n");

        refs.ShouldBe(
        [
            new RemoteRef("main", "aaa111"),
            new RemoteRef("feature/x", "bbb222")
        ]);
    }

    [Fact]
    public void ParseHeads_SkipsNonBranchLines()
    {
        var refs = RemoteRefParser.ParseHeads("ccc333\trefs/tags/v1\n\naaa111\trefs/heads/main\n");

        refs.Single().ShouldBe(new RemoteRef("main", "aaa111"));
    }

    [Fact]
    public void ParseTracking_StripsOriginPrefixAndSkipsSymref()
    {
        var refs = RemoteRefParser.ParseTracking(
            "ddd444\trefs/remotes/origin/main\neee555\trefs/remotes/origin/HEAD\n");

        refs.Single().ShouldBe(new RemoteRef("main", "ddd444"));
    }

    [Fact]
    public void ParseDefaultBranch_ReadsSymrefLine()
    {
        var branch = RemoteRefParser.ParseDefaultBranch("ref: refs/heads/trunk\tHEAD\nfff666\tHEAD\n");

        branch.ShouldBe("trunk");
    }

    [Fact]
    public void ParseDefaultBranch_WithoutSymref_ReturnsNull()
    {
        RemoteRefParser.ParseDefaultBranch("fff666\tHEAD\n").ShouldBeNull();
    }
}
