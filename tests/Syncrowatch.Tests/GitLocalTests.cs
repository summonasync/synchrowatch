using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public class GitLocalTests
{
    private const string OriginUrl = "https://github.com/acme/private.git";

    [Fact]
    public async Task Init_SeedsBranchAndRepoLocalIdentity()
    {
        var runner = new FakeGitRunner();

        var outcome = await new GitLocal(runner).Init(
            Path.GetTempPath(), "main", new CommitIdentity { Name = "Sync", Email = "s@t.local" }, CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Ok>();
        runner.Invocations.Count.ShouldBe(3);
        runner.Invocations[0].Arguments.ShouldBe(["init", "-b", "main"]);
        runner.Invocations[0].WorkingDirectory.ShouldBe(Path.GetTempPath());
        runner.Invocations[1].Arguments.ShouldBe(["config", "user.name", "Sync"]);
        runner.Invocations[2].Arguments.ShouldBe(["config", "user.email", "s@t.local"]);
    }

    [Theory]
    [InlineData("-upstream")]
    [InlineData("main:secret")]
    [InlineData("")]
    public async Task Init_UnusableBranch_RejectedBeforeAnyGitCall(string branch)
    {
        var runner = new FakeGitRunner();

        var outcome = await new GitLocal(runner).Init(
            Path.GetTempPath(), branch, CommitIdentity.Default, CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Fail>().Error.Code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Init_IdentityWithControlCharacters_RejectedBeforeAnyGitCall()
    {
        var runner = new FakeGitRunner();
        var identity = new CommitIdentity { Name = "sync\n[alias]\nboom = !calc", Email = "s@t.local" };

        var outcome = await new GitLocal(runner).Init(Path.GetTempPath(), "main", identity, CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Fail>().Error.Code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task AddRemote_MissingRemote_AddsAfterProbing()
    {
        var runner = new FakeGitRunner(invocation => invocation.Arguments.Contains("get-url")
            ? new GitCommandResult(128, string.Empty, "error: No such remote 'origin'")
            : new GitCommandResult(0, string.Empty, string.Empty));

        var outcome = await new GitLocal(runner).AddRemote(Path.GetTempPath(), "origin", OriginUrl, CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Ok>();
        runner.Invocations.Single(invocation => invocation.Arguments.Contains("add"))
            .Arguments.ShouldBe(["remote", "add", "origin", OriginUrl]);
    }

    [Fact]
    public async Task AddRemote_SameUrlAlreadyPresent_IsANoOp()
    {
        var runner = new FakeGitRunner(invocation => invocation.Arguments.Contains("get-url")
            ? new GitCommandResult(0, OriginUrl + "\n", string.Empty)
            : new GitCommandResult(0, string.Empty, string.Empty));

        var outcome = await new GitLocal(runner).AddRemote(Path.GetTempPath(), "origin", OriginUrl, CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Ok>();
        runner.Invocations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AddRemote_DifferentUrl_RewritesInsteadOfAdding()
    {
        var runner = new FakeGitRunner(invocation => invocation.Arguments.Contains("get-url")
            ? new GitCommandResult(0, "https://github.com/acme/old.git\n", string.Empty)
            : new GitCommandResult(0, string.Empty, string.Empty));

        var outcome = await new GitLocal(runner).AddRemote(
            Path.GetTempPath(), "origin", "https://github.com/acme/new.git", CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Ok>();
        runner.Invocations.ShouldNotContain(invocation => invocation.Arguments.Contains("add"));
        runner.Invocations.Single(invocation => invocation.Arguments.Contains("set-url"))
            .Arguments.ShouldBe(["remote", "set-url", "origin", "https://github.com/acme/new.git"]);
    }

    [Fact]
    public async Task AddRemote_DashLeadingUrl_RejectedBeforeAnyGitCall()
    {
        var runner = new FakeGitRunner();

        var outcome = await new GitLocal(runner).AddRemote(Path.GetTempPath(), "origin", "-oProxyCommand=evil", CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Fail>().Error.Code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Checkout_BuildsBranchFromStartPoint()
    {
        var runner = new FakeGitRunner();

        var outcome = await new GitLocal(runner).Checkout(Path.GetTempPath(), "main", "origin/main", CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Ok>();
        runner.Invocations.Single().Arguments.ShouldBe(["checkout", "-B", "main", "origin/main"]);
    }

    [Fact]
    public async Task Checkout_DashLeadingStartPoint_RejectedBeforeAnyGitCall()
    {
        var runner = new FakeGitRunner();

        var outcome = await new GitLocal(runner).Checkout(Path.GetTempPath(), "main", "--orphan=trap", CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Fail>().Error.Code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task HeadInfo_UnbornHead_KeepsBranchWithoutSha()
    {
        var runner = new FakeGitRunner(invocation => invocation.Arguments.Contains("rev-parse")
            ? new GitCommandResult(128, string.Empty, "fatal: ambiguous argument 'HEAD': unknown revision")
            : new GitCommandResult(0, "main\n", string.Empty));

        var outcome = await new GitLocal(runner).HeadInfo(Path.GetTempPath(), CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<LocalHead>.Ok>();
        ok.Value.Branch.ShouldBe("main");
        ok.Value.Sha.ShouldBeEmpty();
        ok.Value.HasCommits.ShouldBeFalse();
    }

    [Fact]
    public async Task HeadInfo_DetachedHead_ReportsShaWithoutBranch()
    {
        var runner = new FakeGitRunner(invocation => invocation.Arguments.Contains("rev-parse")
            ? new GitCommandResult(0, "feedc0de\n", string.Empty)
            : new GitCommandResult(1, string.Empty, "fatal: ref HEAD is not a symbolic ref"));

        var outcome = await new GitLocal(runner).HeadInfo(Path.GetTempPath(), CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<LocalHead>.Ok>();
        ok.Value.Branch.ShouldBeNull();
        ok.Value.Sha.ShouldBe("feedc0de");
    }

    [Fact]
    public async Task HeadInfo_OutsideRepository_FailsAsGitError()
    {
        var runner = new FakeGitRunner(_ => new GitCommandResult(128, string.Empty, "fatal: not a git repository"));

        var outcome = await new GitLocal(runner).HeadInfo(Path.GetTempPath(), CancellationToken.None);

        outcome.ShouldBeOfType<Result<LocalHead>.Fail>().Error.Code.ShouldBe("git_error");
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("\n", true)]
    [InlineData("?? notes.txt\n", false)]
    [InlineData("A  notes.txt\n", false)]
    public async Task IsWorkingTreeEmpty_ReadsPorcelainOutput(string stdout, bool expected)
    {
        var runner = new FakeGitRunner(_ => new GitCommandResult(0, stdout, string.Empty));

        var outcome = await new GitLocal(runner).IsWorkingTreeEmpty(Path.GetTempPath(), CancellationToken.None);

        outcome.ShouldBeOfType<Result<bool>.Ok>().Value.ShouldBe(expected);
    }

    [Fact]
    public async Task ApplyExcludes_WritesMergedPatternsIntoExcludeFile()
    {
        var folder = TempDir.Create();
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, ".git", "info"));
            var runner = new FakeGitRunner(invocation => invocation.Arguments.Contains("rev-parse")
                ? new GitCommandResult(0, ".git/info/exclude\n", string.Empty)
                : new GitCommandResult(0, string.Empty, string.Empty));

            var outcome = await new GitLocal(runner).ApplyExcludes(folder, ["bin/", "*.tmp", "logs/"], CancellationToken.None);

            outcome.ShouldBeOfType<Result<Unit>.Ok>();
            File.ReadAllText(Path.Combine(folder, ".git", "info", "exclude")).ShouldBe("bin/\n*.tmp\nlogs/\n");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task ApplyExcludes_PreservesExistingUserExcludes()
    {
        var folder = TempDir.Create();
        try
        {
            var excludeFile = Path.Combine(folder, ".git", "info", "exclude");
            Directory.CreateDirectory(Path.GetDirectoryName(excludeFile)!);
            File.WriteAllText(excludeFile, "my-notes/\n");
            var runner = new FakeGitRunner(invocation => invocation.Arguments.Contains("rev-parse")
                ? new GitCommandResult(0, ".git/info/exclude\n", string.Empty)
                : new GitCommandResult(0, string.Empty, string.Empty));

            var outcome = await new GitLocal(runner).ApplyExcludes(folder, ["bin/", "my-notes/", "*.tmp"], CancellationToken.None);

            outcome.ShouldBeOfType<Result<Unit>.Ok>();
            File.ReadAllText(excludeFile).ShouldBe("my-notes/\nbin/\n*.tmp\n");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task Commit_BlankMessage_RejectedBeforeAnyGitCall()
    {
        var runner = new FakeGitRunner();

        var outcome = await new GitLocal(runner).Commit(Path.GetTempPath(), "   ", CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Fail>().Error.Code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task StageAll_RunsAddAll()
    {
        var runner = new FakeGitRunner();

        var outcome = await new GitLocal(runner).StageAll(Path.GetTempPath(), CancellationToken.None);

        outcome.ShouldBeOfType<Result<Unit>.Ok>();
        runner.Invocations.Single().Arguments.ShouldBe(["add", "-A"]);
    }
}
