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
    public async Task Stage_WritesLiteralPathspecFile()
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

        var stage = outcome.ShouldBeOfType<Result<StageOutcome>.Ok>();
        stage.Value.SkippedPaths.ShouldBeEmpty();
        capturedPathspec.ShouldBe(":(literal)a.txt\0:(literal)sub/b.txt");
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

        outcome.ShouldBeOfType<Result<StageOutcome>.Ok>().Value.SkippedPaths.ShouldBeEmpty();
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Stage_MixedBatchWithRejectedName_StagesOnlyTheValidFiles()
    {
        string? capturedPathspec = null;
        var runner = new FakeGitRunner(invocation =>
        {
            var fromArg = invocation.Arguments.Single(argument => argument.StartsWith("--pathspec-from-file="));
            capturedPathspec = File.ReadAllText(fromArg["--pathspec-from-file=".Length..]);
            return new GitCommandResult(0, string.Empty, string.Empty);
        });
        var ops = new LocalRepoOps(runner);

        var outcome = await ops.Stage(TempDir.Create(), ["good.txt", "-poison.txt"], CancellationToken.None);

        outcome.ShouldBeOfType<Result<StageOutcome>.Ok>().Value.SkippedPaths.ShouldBe(["-poison.txt"]);
        capturedPathspec.ShouldBe(":(literal)good.txt");
        runner.Invocations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Stage_AllPathsRejected_MakesNoGitCall()
    {
        var runner = new FakeGitRunner();
        var ops = new LocalRepoOps(runner);

        var outcome = await ops.Stage(TempDir.Create(), ["-lead.txt", "colon:name.txt", "../up.txt"], CancellationToken.None);

        outcome.ShouldBeOfType<Result<StageOutcome>.Ok>().Value.SkippedPaths.Count.ShouldBe(3);
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Stage_MissingRepositoryRoot_FailsWithoutInvocations()
    {
        var runner = new FakeGitRunner();
        var ops = new LocalRepoOps(runner);
        var missingRoot = Path.Combine(TempDir.Create(), "nope");

        var outcome = await ops.Stage(missingRoot, ["a.txt"], CancellationToken.None);

        outcome.ShouldBeOfType<Result<StageOutcome>.Fail>().Error.Code.ShouldBe("not_found");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Stage_GitFailure_MapsToResultFail()
    {
        var runner = new FakeGitRunner(_ => new GitCommandResult(128, string.Empty, "fatal: index.lock exists"));
        var ops = new LocalRepoOps(runner);

        var outcome = await ops.Stage(TempDir.Create(), ["a.txt"], CancellationToken.None);

        outcome.ShouldBeOfType<Result<StageOutcome>.Fail>().Error.Code.ShouldBe("git_error");
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

        outcome.ShouldBeOfType<Result<CommitOutcome>.Ok>().Value.CommitSha.ShouldBe("cafe1234");
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
    public async Task Commit_MixedBatchWithRejectedName_CommitsTheValidOnes()
    {
        var runner = new FakeGitRunner(invocation => invocation.Arguments.Contains("rev-parse")
            ? new GitCommandResult(0, "cafe5678\n", string.Empty)
            : new GitCommandResult(0, string.Empty, string.Empty));
        var ops = new LocalRepoOps(runner);
        var identity = new CommitIdentity { Name = "Syncrowatch", Email = "sync@local" };

        var outcome = await ops.Commit(
            TempDir.Create(), identity, "sync: 1 file changed", ["good.txt", "colon:bad.txt"], CancellationToken.None);

        var committed = outcome.ShouldBeOfType<Result<CommitOutcome>.Ok>().Value;
        committed.CommitSha.ShouldBe("cafe5678");
        committed.SkippedPaths.ShouldBe(["colon:bad.txt"]);
        runner.Invocations.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Commit_AllPathsRejected_MakesNoGitCallAndReportsNoOp()
    {
        var runner = new FakeGitRunner();
        var ops = new LocalRepoOps(runner);
        var identity = new CommitIdentity { Name = "Syncrowatch", Email = "sync@local" };

        var outcome = await ops.Commit(
            TempDir.Create(), identity, "sync: 0 files changed", ["-lead.txt", "colon:name.txt"], CancellationToken.None);

        var committed = outcome.ShouldBeOfType<Result<CommitOutcome>.Ok>().Value;
        committed.CommitSha.ShouldBeNull();
        committed.SkippedPaths.Count.ShouldBe(2);
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Commit_EmptyDiffBatch_IsTypedNoOp()
    {
        var runner = new FakeGitRunner(_ => new GitCommandResult(1, string.Empty, "nothing to commit, working tree clean"));
        var ops = new LocalRepoOps(runner);
        var identity = new CommitIdentity { Name = "Syncrowatch", Email = "sync@local" };

        var outcome = await ops.Commit(TempDir.Create(), identity, "sync: 1 file changed", ["a.txt"], CancellationToken.None);

        var committed = outcome.ShouldBeOfType<Result<CommitOutcome>.Ok>().Value;
        committed.CommitSha.ShouldBeNull();
        committed.SkippedPaths.ShouldBeEmpty();
        runner.Invocations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Commit_FailureAfterStage_RestoresCleanIndex()
    {
        var runner = new FakeGitRunner(_ => new GitCommandResult(1, string.Empty, "error: unable to write index"));
        var ops = new LocalRepoOps(runner);
        var identity = new CommitIdentity { Name = "Syncrowatch", Email = "sync@local" };

        var outcome = await ops.Commit(TempDir.Create(), identity, "sync: 1 file changed", ["a.txt"], CancellationToken.None);

        outcome.ShouldBeOfType<Result<CommitOutcome>.Fail>().Error.Code.ShouldBe("git_error");
        runner.Invocations.Count.ShouldBe(2);
        runner.Invocations[1].Arguments.ShouldBe(["reset"]);
    }

    [Fact]
    public async Task Commit_EmptyPathList_IsTypedNoOpWithoutInvocations()
    {
        var runner = new FakeGitRunner();
        var ops = new LocalRepoOps(runner);
        var identity = new CommitIdentity { Name = "Syncrowatch", Email = "sync@local" };

        var outcome = await ops.Commit(TempDir.Create(), identity, "sync: nothing", [], CancellationToken.None);

        var committed = outcome.ShouldBeOfType<Result<CommitOutcome>.Ok>().Value;
        committed.CommitSha.ShouldBeNull();
        committed.SkippedPaths.ShouldBeEmpty();
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Commit_NothingToCommitOnStdout_IsTypedNoOp()
    {
        var runner = new FakeGitRunner(_ => new GitCommandResult(1, "no changes added to commit\n", string.Empty));
        var ops = new LocalRepoOps(runner);
        var identity = new CommitIdentity { Name = "Syncrowatch", Email = "sync@local" };

        var outcome = await ops.Commit(TempDir.Create(), identity, "sync: 1 file changed", ["a.txt"], CancellationToken.None);

        var committed = outcome.ShouldBeOfType<Result<CommitOutcome>.Ok>().Value;
        committed.CommitSha.ShouldBeNull();
        runner.Invocations.Count.ShouldBe(1);
    }
}
