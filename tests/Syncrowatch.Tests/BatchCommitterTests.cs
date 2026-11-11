using Shouldly;
using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public class BatchCommitterTests
{
    private static readonly string Root = TempDir.Create();

    private static SyncConfig Config() => new()
    {
        WatchedFolder = Root,
        RemoteUrl = "https://github.com/acme/private.git",
        Branch = "main",
        Mode = SyncMode.Git,
        Identity = new CommitIdentity { Name = "Syncrowatch", Email = "sync@local" }
    };

    private static FakeGitRunner ShaRunner() => new(invocation => invocation.Arguments.Contains("rev-parse")
        ? new GitCommandResult(0, $"dead{Interlocked.Increment(ref _shaSequence):x}\n", string.Empty)
        : new GitCommandResult(0, string.Empty, string.Empty));

    private static int _shaSequence;

    [Fact]
    public async Task CommitBatch_EmptyBatch_FailsWithoutInvocations()
    {
        var runner = new FakeGitRunner();
        var committer = new BatchCommitter(Config(), new LocalRepoOps(runner));

        var outcome = await committer.CommitBatch([], CancellationToken.None);

        outcome.ShouldBeOfType<Result<CommitOutcome>.Fail>().Error.Code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task CommitBatch_StagesThenCommitsExactlyOnce()
    {
        var runner = ShaRunner();
        var committer = new BatchCommitter(Config(), new LocalRepoOps(runner));

        var outcome = await committer.CommitBatch(["a.txt", "b.txt"], CancellationToken.None);

        var committed = outcome.ShouldBeOfType<Result<CommitOutcome>.Ok>().Value;
        committed.CommitSha.ShouldNotBeNull();
        committed.SkippedPaths.ShouldBeEmpty();
        runner.Invocations.Count.ShouldBe(3);
        runner.Invocations[0].Arguments[0].ShouldBe("add");
        runner.Invocations[1].Arguments.ShouldContain("commit");
        runner.Invocations[2].Arguments.ShouldBe(["rev-parse", "HEAD"]);
        var commitArguments = runner.Invocations[1].Arguments.ToArray();
        var message = commitArguments[Array.IndexOf(commitArguments, "-m") + 1];
        message.ShouldBe("sync: 2 files changed");
    }

    [Fact]
    public async Task CommitBatch_MixedBatchWithRejectedName_CommitsTheRestAndSurfacesSkip()
    {
        var pathspecs = new List<string>();
        var runner = new FakeGitRunner(invocation =>
        {
            if (invocation.Arguments.Contains("rev-parse"))
            {
                return new GitCommandResult(0, $"beef{Interlocked.Increment(ref _shaSequence):x}\n", string.Empty);
            }

            var fromArg = invocation.Arguments.FirstOrDefault(argument => argument.StartsWith("--pathspec-from-file="));
            if (fromArg is not null)
            {
                pathspecs.Add(File.ReadAllText(fromArg["--pathspec-from-file=".Length..]));
            }

            return new GitCommandResult(0, string.Empty, string.Empty);
        });
        var committer = new BatchCommitter(Config(), new LocalRepoOps(runner));

        var outcome = await committer.CommitBatch(["good.txt", "-poison.txt"], CancellationToken.None);

        var committed = outcome.ShouldBeOfType<Result<CommitOutcome>.Ok>().Value;
        committed.CommitSha.ShouldNotBeNull();
        committed.SkippedPaths.ShouldBe(["-poison.txt"]);
        var commitArguments = runner.Invocations[1].Arguments.ToArray();
        commitArguments[Array.IndexOf(commitArguments, "-m") + 1].ShouldBe("sync: 1 file changed (good.txt)");
        pathspecs.Count.ShouldBe(2);
        pathspecs.ShouldAllBe(pathspec => pathspec == ":(literal)good.txt");
    }

    [Fact]
    public async Task CommitBatch_PoisonNameDoesNotPoisonLaterBatches()
    {
        var pathspecs = new List<string>();
        var runner = new FakeGitRunner(invocation =>
        {
            if (invocation.Arguments.Contains("rev-parse"))
            {
                return new GitCommandResult(0, $"face{Interlocked.Increment(ref _shaSequence):x}\n", string.Empty);
            }

            var fromArg = invocation.Arguments.FirstOrDefault(argument => argument.StartsWith("--pathspec-from-file="));
            if (fromArg is not null)
            {
                pathspecs.Add(File.ReadAllText(fromArg["--pathspec-from-file=".Length..]));
            }

            return new GitCommandResult(0, string.Empty, string.Empty);
        });
        var committer = new BatchCommitter(Config(), new LocalRepoOps(runner));

        var first = await committer.CommitBatch(["notes.txt", "-poison.txt"], CancellationToken.None);
        var second = await committer.CommitBatch(["journal.txt", "-poison.txt"], CancellationToken.None);

        first.ShouldBeOfType<Result<CommitOutcome>.Ok>().Value.CommitSha.ShouldNotBeNull();
        var secondOutcome = second.ShouldBeOfType<Result<CommitOutcome>.Ok>().Value;
        secondOutcome.CommitSha.ShouldNotBeNull();
        secondOutcome.SkippedPaths.ShouldBe(["-poison.txt"]);
        pathspecs.Count.ShouldBe(4);
        pathspecs.ShouldContain(":(literal)notes.txt");
        pathspecs.ShouldContain(":(literal)journal.txt");
        pathspecs.ShouldNotContain(pathspec => pathspec.Contains("poison"));
    }

    [Fact]
    public async Task CommitBatch_SinglePath_NamesTheFileInMessage()
    {
        var runner = ShaRunner();
        var committer = new BatchCommitter(Config(), new LocalRepoOps(runner));

        var outcome = await committer.CommitBatch(["notes/ideas.md"], CancellationToken.None);

        outcome.ShouldBeOfType<Result<CommitOutcome>.Ok>().Value.CommitSha.ShouldNotBeNull();
        var commitArguments = runner.Invocations[1].Arguments.ToArray();
        commitArguments[Array.IndexOf(commitArguments, "-m") + 1].ShouldBe("sync: 1 file changed (notes/ideas.md)");
    }

    [Fact]
    public async Task CommitBatch_StageFailure_SkipsCommit()
    {
        var runner = new FakeGitRunner(invocation => invocation.Arguments[0] == "add"
            ? new GitCommandResult(128, string.Empty, "fatal: index file corrupt")
            : new GitCommandResult(0, string.Empty, string.Empty));
        var committer = new BatchCommitter(Config(), new LocalRepoOps(runner));

        var outcome = await committer.CommitBatch(["a.txt"], CancellationToken.None);

        outcome.ShouldBeOfType<Result<CommitOutcome>.Fail>().Error.Code.ShouldBe("git_error");
        runner.Invocations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task CommitBatch_UsesIdentityFromConfig()
    {
        var runner = ShaRunner();
        var committer = new BatchCommitter(Config(), new LocalRepoOps(runner));

        await committer.CommitBatch(["a.txt"], CancellationToken.None);

        var commitArguments = runner.Invocations[1].Arguments;
        commitArguments[1].ShouldBe("user.name=Syncrowatch");
        commitArguments[3].ShouldBe("user.email=sync@local");
    }
}
