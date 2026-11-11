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
                var merged = MergeExcludes(target, patterns);
                await File.WriteAllTextAsync(target, merged, cancellationToken).ConfigureAwait(false);
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

    public Task<Result<StageOutcome>> StagePaths(string folder, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken) =>
        TransportResult.Capture<StageOutcome>(async () =>
        {
            GuardFolder(folder);
            var (accepted, skipped) = SplitGuarded(relativePaths);
            if (accepted.Count == 0)
            {
                return new Result<StageOutcome>.Ok(new StageOutcome(skipped));
            }

            using var pathspec = PathspecFile.Create(accepted);
            GitFailure.EnsureSuccess(await RunAsync(folder,
                ["add", "-A", $"--pathspec-from-file={pathspec.FilePath}", "--pathspec-file-nul"], cancellationToken).ConfigureAwait(false), "stage");
            return new Result<StageOutcome>.Ok(new StageOutcome(skipped));
        });

    public Task<Result<CommitOutcome>> CommitPaths(
        string folder, CommitIdentity identity, string message, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken) =>
        TransportResult.Capture<CommitOutcome>(async () =>
        {
            GuardFolder(folder);
            GitArgumentGuards.ConfigValue(identity.Name, "identity name");
            GitArgumentGuards.ConfigValue(identity.Email, "identity email");
            var (accepted, skipped) = SplitGuarded(relativePaths);
            if (accepted.Count == 0)
            {
                return new Result<CommitOutcome>.Ok(new CommitOutcome(skipped, null));
            }

            using var pathspec = PathspecFile.Create(accepted);
            var committed = await RunAsync(folder,
            [
                "-c", $"user.name={identity.Name}",
                "-c", $"user.email={identity.Email}",
                "commit", "-m", message,
                $"--pathspec-from-file={pathspec.FilePath}",
                "--pathspec-file-nul"
            ], cancellationToken).ConfigureAwait(false);
            if (!committed.Succeeded)
            {
                if (IsNothingToCommit(committed))
                {
                    return new Result<CommitOutcome>.Ok(new CommitOutcome(skipped, null));
                }

                await RunAsync(folder, ["reset"], cancellationToken).ConfigureAwait(false);
                GitFailure.EnsureSuccess(committed, "commit");
            }

            var head = await RunAsync(folder, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
            GitFailure.EnsureSuccess(head, "read HEAD");
            return new Result<CommitOutcome>.Ok(new CommitOutcome(skipped, head.StandardOutput.Trim()));
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
            WorkingDirectory = folder,
            Environment = GitEnv.PromptDisabled
        }, cancellationToken);

    private static void GuardFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new GitOperationException(SyncError.NotFound($"repository folder '{folder}' does not exist"));
        }
    }

    private static (List<string> Accepted, List<string> Skipped) SplitGuarded(IReadOnlyList<string> relativePaths)
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

    private static string MergeExcludes(string target, IReadOnlyList<string> patterns)
    {
        var existing = File.Exists(target)
            ? File.ReadAllLines(target).Where(line => line.Trim().Length > 0)
            : [];
        var additions = patterns.Where(pattern => !existing.Contains(pattern, StringComparer.Ordinal));
        return string.Join('\n', existing.Concat(additions)) + "\n";
    }
}
