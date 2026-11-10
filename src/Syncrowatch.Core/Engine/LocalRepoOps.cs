using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Transport;

namespace Syncrowatch.Core.Engine;

public sealed class LocalRepoOps(IGitRunner runner)
{
    public async Task<Result<Unit>> Stage(string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken)
    {
        try
        {
            return await StageCore(repositoryRoot, relativePaths, cancellationToken).ConfigureAwait(false);
        }
        catch (GitTransportException ex)
        {
            return new Result<Unit>.Fail(ex.Error);
        }
    }

    public async Task<Result<string>> Commit(
        string repositoryRoot, CommitIdentity identity, string message, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken)
    {
        try
        {
            return await CommitCore(repositoryRoot, identity, message, relativePaths, cancellationToken).ConfigureAwait(false);
        }
        catch (GitTransportException ex)
        {
            return new Result<string>.Fail(ex.Error);
        }
    }

    private async Task<Result<Unit>> StageCore(
        string repositoryRoot, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken)
    {
        GuardRoot(repositoryRoot);
        if (relativePaths.Count == 0)
        {
            return new Result<Unit>.Ok(Unit.Value);
        }

        using var pathspec = PathspecFile.Create([.. relativePaths.Select(GitArgumentGuards.RelativePath)]);
        var outcome = await runner.RunAsync(new GitInvocation
        {
            Arguments = ["add", "-A", $"--pathspec-from-file={pathspec.FilePath}", "--pathspec-file-nul"],
            WorkingDirectory = repositoryRoot
        }, cancellationToken).ConfigureAwait(false);
        GitFailure.EnsureSuccess(outcome, "stage");
        return new Result<Unit>.Ok(Unit.Value);
    }

    private async Task<Result<string>> CommitCore(
        string repositoryRoot, CommitIdentity identity, string message, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken)
    {
        GuardRoot(repositoryRoot);
        if (relativePaths.Count == 0)
        {
            return new Result<string>.Fail(SyncError.Invalid("commit batch is empty"));
        }

        using var pathspec = PathspecFile.Create([.. relativePaths.Select(GitArgumentGuards.RelativePath)]);
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
        GitFailure.EnsureSuccess(committed, "commit");

        var head = await runner.RunAsync(new GitInvocation
        {
            Arguments = ["rev-parse", "HEAD"],
            WorkingDirectory = repositoryRoot
        }, cancellationToken).ConfigureAwait(false);
        GitFailure.EnsureSuccess(head, "read HEAD");
        return new Result<string>.Ok(head.StandardOutput.Trim());
    }

    private static void GuardRoot(string repositoryRoot)
    {
        if (!Directory.Exists(repositoryRoot))
        {
            throw new GitOperationException(SyncError.NotFound($"repository folder '{repositoryRoot}' does not exist"));
        }
    }
}
