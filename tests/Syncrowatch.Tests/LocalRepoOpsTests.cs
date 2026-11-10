using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public class LocalRepoOpsTests
{
    [Fact]
    public async Task Stage_WritesPathspecFileWithNulSeparatedPaths()
    {
        string? capturedPathspec = null;
        var runner = new FakeGitRunner(invocation =>
        {
            var fromArg = invocation.Arguments.Single(argument => argument.StartsWith("--pathspec-from-file="));
            capturedPathspec = File.ReadAllText(fromArg["--pathspec-from-file=".Length..]);
            return new GitCommandResult(0, string.Empty, string.Empty);
        });
        var ops = new LocalRepoOps(runner);

        var outcome = await ops.Stage(TempDir.Create(), ["a.txt", "sub/b.txt"], CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Ok>();
        capturedPathspec.ShouldBe("a.txt\0sub/b.txt");
        var arguments = runner.Invocations.Single().Arguments;
        arguments[0].ShouldBe("add");
        arguments[1].ShouldBe("-A");
        arguments[^1].ShouldBe("--pathspec-file-nul");
    }

    [Fact]
    public async Task Stage_EmptyBatch_SkipsGit()
    {
        var runner = new FakeGitRunner();
        var ops = new LocalRepoOps(runner);

        var outcome = await ops.Stage(TempDir.Create(), [], CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Ok>();
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Stage_MissingRepositoryRoot_FailsWithoutInvocations()
    {
        var runner = new FakeGitRunner();
        var ops = new LocalRepoOps(runner);
        var missingRoot = Path.Combine(TempDir.Create(), "nope");

        var outcome = await ops.Stage(missingRoot, ["a.txt"], CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Fail>().Error.Code.ShouldBe("not_found");
        runner.Invocations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("-smuggled.txt")]
    [InlineData("a:b.txt")]
    [InlineData("../escape.txt")]
    public async Task Stage_GuardRejectedPath_FailsWithZeroInvocations(string relativePath)
    {
        var runner = new FakeGitRunner();
        var ops = new LocalRepoOps(runner);

        var outcome = await ops.Stage(TempDir.Create(), [relativePath], CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Fail>().Error.Code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Stage_GitFailure_MapsToResultFail()
    {
        var runner = new FakeGitRunner(_ => new GitCommandResult(128, string.Empty, "fatal: index.lock exists"));
        var ops = new LocalRepoOps(runner);

        var outcome = await ops.Stage(TempDir.Create(), ["a.txt"], CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Fail>().Error.Code.ShouldBe("git_error");
    }

    [Fact]
    public async Task Commit_SendsIdentityMessageAndPathspec()
    {
        var runner = new FakeGitRunner(invocation => invocation.Arguments.Contains("rev-parse")
            ? new GitCommandResult(0, "cafe1234\n", string.Empty)
            : new GitCommandResult(0, string.Empty, string.Empty));
        var ops = new LocalRepoOps(runner);
        var identity = new CommitIdentity { Name = "Syncrowatch", Email = "sync@local" };

        var outcome = await ops.Commit(TempDir.Create(), identity, "sync: 1 file changed", ["a.txt"], CancellationToken.None);

        outcome.ShouldBeOfType<Result<string>.Ok>().Value.ShouldBe("cafe1234");
        runner.Invocations.Count.ShouldBe(2);
        var commit = runner.Invocations[0].Arguments;
        commit[0].ShouldBe("-c");
        commit[1].ShouldBe("user.name=Syncrowatch");
        commit[2].ShouldBe("-c");
        commit[3].ShouldBe("user.email=sync@local");
        commit.ShouldContain("commit");
        commit.ShouldContain("-m");
        commit.ShouldContain("--pathspec-file-nul");
        runner.Invocations[1].Arguments.ShouldBe(["rev-parse", "HEAD"]);
    }

    [Fact]
    public async Task Commit_GuardRejectedPath_FailsWithZeroInvocations()
    {
        var runner = new FakeGitRunner();
        var ops = new LocalRepoOps(runner);
        var identity = new CommitIdentity { Name = "Syncrowatch", Email = "sync@local" };

        var outcome = await ops.Commit(
            TempDir.Create(), identity, "sync: 1 file changed", ["colon:path.txt"], CancellationToken.None);

        outcome.ShouldBeOfType<Result<string>.Fail>().Error.Code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Commit_EmptyBatch_FailsWithoutInvocations()
    {
        var runner = new FakeGitRunner();
        var ops = new LocalRepoOps(runner);
        var identity = new CommitIdentity { Name = "Syncrowatch", Email = "sync@local" };

        var outcome = await ops.Commit(TempDir.Create(), identity, "sync: nothing", [], CancellationToken.None);

        outcome.ShouldBeOfType<Result<string>.Fail>().Error.Code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Commit_GitFailure_MapsToResultFail()
    {
        var runner = new FakeGitRunner(_ => new GitCommandResult(1, string.Empty, "error: nothing to commit"));
        var ops = new LocalRepoOps(runner);
        var identity = new CommitIdentity { Name = "Syncrowatch", Email = "sync@local" };

        var outcome = await ops.Commit(TempDir.Create(), identity, "sync: 1 file changed", ["a.txt"], CancellationToken.None);

        outcome.ShouldBeOfType<Result<string>.Fail>().Error.Code.ShouldBe("git_error");
    }
}
