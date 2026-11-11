using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Transport;

namespace Syncrowatch.Core.Engine;

public sealed class LocalRepoOps(IGitRunner runner)
{
    public async Task<Result<StageOutcome>> Stage(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken)
    {
        try
        {
            return await StageCore(repositoryRoot, relativePaths, cancellationToken).ConfigureAwait(false);
        }
        catch (GitTransportException ex)
        {
            return new Result<StageOutcome>.Fail(ex.Error);
        }
    }

    public async Task<Result<CommitOutcome>> Commit(
        string repositoryRoot, CommitIdentity identity, string message, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken)
    {
        try
        {
            return await CommitCore(repositoryRoot, identity, message, relativePaths, cancellationToken).ConfigureAwait(false);
        }
        catch (GitTransportException ex)
        {
            return new Result<CommitOutcome>.Fail(ex.Error);
        }
    }

    private async Task<Result<StageOutcome>> StageCore(
        string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken)
    {
        GuardRoot(repositoryRoot);
        var (accepted, skipped) = SplitGuarded(relativePaths);
        if (accepted.Count == 0)
        {
            return new Result<StageOutcome>.Ok(new StageOutcome(skipped));
        }

        using var pathspec = PathspecFile.Create(accepted);
        var outcome = await runner.RunAsync(new GitInvocation
        {
            Arguments = ["add", "-A", $"--pathspec-from-file={pathspec.FilePath}", "--pathspec-file-nul"],
            WorkingDirectory = repositoryRoot
        }, cancellationToken).ConfigureAwait(false);
        GitFailure.EnsureSuccess(outcome, "stage");
        return new Result<StageOutcome>.Ok(new StageOutcome(skipped));
    }

    private async Task<Result<CommitOutcome>> CommitCore(
        string repositoryRoot, CommitIdentity identity, string message, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken)
    {
        GuardRoot(repositoryRoot);
        var (accepted, skipped) = SplitGuarded(relativePaths);
        if (accepted.Count == 0)
        {
            return new Result<CommitOutcome>.Ok(new CommitOutcome(skipped, null));
        }

        using var pathspec = PathspecFile.Create(accepted);
        var committed = await runner.RunAsync(new GitInvocation
        {
            Arguments =
            [
                "-c", $"user.name={identity.Name}",
                "-c", $"user.email={identity.Email}",
                "commit", "-m", message,
                $"--pathspec-from-file={pathspec.FilePath}",
                "--pathspec-file-nul"
            ],
            WorkingDirectory = repositoryRoot
        }, cancellationToken).ConfigureAwait(false);
        if (!committed.Succeeded)
        {
            if (IsNothingToCommit(committed))
            {
                return new Result<CommitOutcome>.Ok(new CommitOutcome(skipped, null));
            }

            await UnstageAll(repositoryRoot, cancellationToken).ConfigureAwait(false);
            GitFailure.EnsureSuccess(committed, "commit");
        }

        var head = await runner.RunAsync(new GitInvocation
        {
            Arguments = ["rev-parse", "HEAD"],
            WorkingDirectory = repositoryRoot
        }, cancellationToken).ConfigureAwait(false);
        GitFailure.EnsureSuccess(head, "read HEAD");
        return new Result<CommitOutcome>.Ok(new CommitOutcome(skipped, head.StandardOutput.Trim()));
    }

    private async Task UnstageAll(string repositoryRoot, CancellationToken cancellationToken)
    {
        try
        {
            await runner.RunAsync(new GitInvocation
            {
                Arguments = ["reset"],
                WorkingDirectory = repositoryRoot
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (GitTransportException)
        {
        }
    }

    private (List<string> Accepted, List<string> Skipped) SplitGuarded(IReadOnlyList<string> relativePaths)
    {
        var accepted = new List<string>();
        var skipped = new List<string>();
        foreach (var relativePath in relativePaths)
        {
            try
            {
                accepted.Add(GitArgumentGuards.RelativePath(relativePath));
            }
            catch (GitOperationException)
            {
                skipped.Add(relativePath);
            }
        }

        return (accepted, skipped);
    }

    private static bool IsNothingToCommit(GitCommandResult result)
    {
        var output = result.StandardOutput + result.StandardError;
        return output.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase)
            || output.Contains("no changes added to commit", StringComparison.OrdinalIgnoreCase);
    }

    private static void GuardRoot(string repositoryRoot)
    {
        if (!Directory.Exists(repositoryRoot))
        {
            throw new GitOperationException(SyncError.NotFound($"repository folder '{repositoryRoot}' does not exist"));
        }
    }
}
