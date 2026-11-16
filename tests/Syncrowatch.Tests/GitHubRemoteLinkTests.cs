using Shouldly;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public class GitHubRemoteLinkTests
{
    [Theory]
    [InlineData("https://github.com/acme/widget", "https://github.com/acme/widget")]
    [InlineData("https://github.com/acme/widget.git", "https://github.com/acme/widget")]
    [InlineData("https://github.com/acme/widget/", "https://github.com/acme/widget")]
    [InlineData("https://github.com/acme/widget.git/", "https://github.com/acme/widget")]
    [InlineData("https://token@github.com/acme/widget", "https://github.com/acme/widget")]
    [InlineData("https://x-access-token:ghp_secret@github.com/acme/widget", "https://github.com/acme/widget")]
    [InlineData("HTTPS://GITHUB.COM/acme/widget", "https://github.com/acme/widget")]
    [InlineData("https://github.com:443/acme/widget", "https://github.com/acme/widget")]
    [InlineData("https://github.com/AcmeCorp/Widget.Pro", "https://github.com/AcmeCorp/Widget.Pro")]
    public void TryParseBrowserBase_GithubHttpsRemote_YieldsCleanBase(string remote, string expected)
    {
        GitHubRemoteLink.TryParseBrowserBase(remote).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("git@github.com:acme/widget")]
    [InlineData("ssh://git@github.com/acme/widget")]
    [InlineData("http://github.com/acme/widget")]
    [InlineData("https://gitlab.com/acme/widget")]
    [InlineData("https://github.com.evil.test/acme/widget")]
    [InlineData("https://github.com:8443/acme/widget")]
    [InlineData("https://github.com/acme")]
    [InlineData("https://github.com/acme/widget/extra")]
    [InlineData("https://github.com/")]
    [InlineData("https://github.com/acme/.git")]
    public void TryParseBrowserBase_NonGithubHttpsRemote_ReturnsNull(string? remote)
    {
        GitHubRemoteLink.TryParseBrowserBase(remote).ShouldBeNull();
    }

    [Theory]
    [InlineData("https://ghp_supersecret@github.com/acme/widget")]
    [InlineData("https://x-access-token:ghp_supersecret@github.com/acme/widget")]
    public void TryParseBrowserBase_CredentialedRemote_NeverLeaksToken(string remote)
    {
        var browserBase = GitHubRemoteLink.TryParseBrowserBase(remote);

        browserBase.ShouldNotBeNull();
        browserBase.ShouldNotContain("ghp_supersecret");
        browserBase.ShouldNotContain("@");
    }

    [Fact]
    public void BuildLink_BuildsShaPinnedBlobUrl()
    {
        GitHubRemoteLink.BuildLink("https://github.com/acme/widget", "abc1234", "src/App.cs")
            .ShouldBe("https://github.com/acme/widget/blob/abc1234/src/App.cs");
    }

    [Theory]
    [InlineData(@"src\App.cs", "src/App.cs")]
    [InlineData("src/App.cs", "src/App.cs")]
    [InlineData("/src/App.cs", "src/App.cs")]
    [InlineData(@"\src\App.cs", "src/App.cs")]
    public void BuildLink_NormalizesRelativePathSeparators(string relativePath, string expectedSuffix)
    {
        var link = GitHubRemoteLink.BuildLink("https://github.com/acme/widget", "abc1234", relativePath);

        link.ShouldBe($"https://github.com/acme/widget/blob/abc1234/{expectedSuffix}");
    }
}
