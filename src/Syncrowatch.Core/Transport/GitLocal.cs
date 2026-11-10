using Synchro.Core;
using Syncrowatch.Core.Config;

namespace Syncrowatch.Core.Transport;

public sealed class GitLocal(IGitRunner runner) : ILocalGit
{
    public Task<Result<Unit>> Init(string folder, string branch, CommitIdentity identity, CancellationToken cancellationToken) =>
        TransportResult.Capture<Unit>(async () =>
        {
            GitArgumentGuards.RefName(branch);
            GitArgumentGuards.ConfigValue(identity.Name, "identity name");
            GitArgumentGuards.ConfigValue(identity.Email, "identity email");
            GitFailure.EnsureSuccess(await RunAsync(folder, ["init", "-b", branch], cancellationToken).ConfigureAwait(false), "init");
            GitFailure.EnsureSuccess(await RunAsync(folder, ["config", "user.name", identity.Name], cancellationToken).ConfigureAwait(false), "store identity");
            GitFailure.EnsureSuccess(await RunAsync(folder, ["config", "user.email", identity.Email], cancellationToken).ConfigureAwait(false), "store identity");
            return new Result<Unit>.Ok(Unit.Value);
        });

    public Task<Result<Unit>> AddRemote(string folder, string remoteName, string url, CancellationToken cancellationToken) =>
        TransportResult.Capture<Unit>(async () =>
        {
            GitArgumentGuards.RefName(remoteName);
            GitArgumentGuards.UsableRemoteUrl(url);
            var current = await RunAsync(folder, ["remote", "get-url", remoteName], cancellationToken).ConfigureAwait(false);
            if (current.Succeeded)
            {
                if (current.StandardOutput.Trim() == url)
                {
                    return new Result<Unit>.Ok(Unit.Value);
                }

                GitFailure.EnsureSuccess(
                    await RunAsync(folder, ["remote", "set-url", remoteName, url], cancellationToken).ConfigureAwait(false),
                    "update remote url");
                return new Result<Unit>.Ok(Unit.Value);
            }

            GitFailure.EnsureSuccess(await RunAsync(folder, ["remote", "add", remoteName, url], cancellationToken).ConfigureAwait(false), "add remote");
            return new Result<Unit>.Ok(Unit.Value);
        });

    public Task<Result<Unit>> ApplyExcludes(string folder, IReadOnlyList<string> patterns, CancellationToken cancellationToken) =>
        TransportResult.Capture<Unit>(async () =>
        {
            var probe = await RunAsync(folder, ["rev-parse", "--git-path", "info/exclude"], cancellationToken).ConfigureAwait(false);
            GitFailure.EnsureSuccess(probe, "resolve exclude file");
            var located = probe.StandardOutput.Trim();
            var target = Path.IsPathRooted(located) ? located : Path.Combine(folder, located);
            try
            {
                await File.WriteAllTextAsync(target, string.Join('\n', patterns) + "\n", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new GitOperationException(new SyncError("unavailable", $"exclude file {target} could not be written"));
            }

            return new Result<Unit>.Ok(Unit.Value);
        });

    public Task<Result<Unit>> StageAll(string folder, CancellationToken cancellationToken) =>
        TransportResult.Capture<Unit>(async () =>
        {
            GitFailure.EnsureSuccess(await RunAsync(folder, ["add", "-A"], cancellationToken).ConfigureAwait(false), "stage working tree");
            return new Result<Unit>.Ok(Unit.Value);
        });

    public Task<Result<Unit>> Commit(string folder, string message, CancellationToken cancellationToken) =>
        TransportResult.Capture<Unit>(async () =>
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new GitOperationException(SyncError.Invalid("commit message is empty"));
            }

            GitFailure.EnsureSuccess(await RunAsync(folder, ["commit", "-m", message], cancellationToken).ConfigureAwait(false), "commit");
            return new Result<Unit>.Ok(Unit.Value);
        });

    public Task<Result<Unit>> Checkout(string folder, string branch, string startPoint, CancellationToken cancellationToken) =>
        TransportResult.Capture<Unit>(async () =>
        {
            GitArgumentGuards.RefName(branch);
            GitArgumentGuards.RefName(startPoint);
            GitFailure.EnsureSuccess(
                await RunAsync(folder, ["checkout", "-B", branch, startPoint], cancellationToken).ConfigureAwait(false),
                "checkout branch");
            return new Result<Unit>.Ok(Unit.Value);
        });

    public Task<Result<LocalHead>> HeadInfo(string folder, CancellationToken cancellationToken) =>
        TransportResult.Capture<LocalHead>(async () =>
        {
            var revision = await RunAsync(folder, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
            var symbolic = await RunAsync(folder, ["symbolic-ref", "--short", "HEAD"], cancellationToken).ConfigureAwait(false);
            if (!revision.Succeeded && !symbolic.Succeeded)
            {
                throw GitFailure.Classify(revision, "read HEAD");
            }

            var head = new LocalHead(
                symbolic.Succeeded ? symbolic.StandardOutput.Trim() : null,
                revision.Succeeded ? revision.StandardOutput.Trim() : string.Empty);
            return new Result<LocalHead>.Ok(head);
        });

    public Task<Result<bool>> IsWorkingTreeEmpty(string folder, CancellationToken cancellationToken) =>
        TransportResult.Capture<bool>(async () =>
        {
            var status = await RunAsync(folder, ["status", "--porcelain"], cancellationToken).ConfigureAwait(false);
            GitFailure.EnsureSuccess(status, "read working tree state");
            return new Result<bool>.Ok(status.StandardOutput.Trim().Length == 0);
        });

    private Task<GitCommandResult> RunAsync(string folder, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        runner.RunAsync(new GitInvocation
        {
            Arguments = arguments,
            WorkingDirectory = folder
        }, cancellationToken);
}
