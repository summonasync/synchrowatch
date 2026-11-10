using Synchro.Core;
using Syncrowatch.Core.Config;

namespace Syncrowatch.Core.Transport;

public sealed class GitHubRepository : IRemoteRepository
{
    private static readonly IReadOnlyList<string> CredentialIsolation = ["-c", "credential.helper="];

    private readonly SyncConfig _config;
    private readonly ICredentialStore _credentials;
    private readonly IGitRunner _runner;

    public GitHubRepository(SyncConfig config, ICredentialStore credentials, IGitRunner runner)
    {
        _config = config;
        _credentials = credentials;
        _runner = runner;
    }

    public Task<Result<RemoteValidation>> ValidateRepository(CancellationToken cancellationToken) =>
        TransportResult.Capture<RemoteValidation>(async () =>
        {
            GitArgumentGuards.UsableRemoteUrl(_config.RemoteUrl);
            var probe = await RunProbeAsync(cancellationToken).ConfigureAwait(false);
            if (probe.Succeeded)
            {
                throw new PublicRepositoryException(_config.RemoteUrl);
            }

            var probeFailure = GitFailure.Classify(probe, "probe");
            if (probeFailure is RemoteUnreachableException)
            {
                throw probeFailure;
            }

            var answer = await RunRemoteAsync(
                [.. CredentialIsolation, "ls-remote", "--symref", _config.RemoteUrl, "HEAD"], null, cancellationToken)
                .ConfigureAwait(false);
            GitFailure.EnsureSuccess(answer, "validate");

            return new Result<RemoteValidation>.Ok(
                new RemoteValidation(true, RemoteRefParser.ParseDefaultBranch(answer.StandardOutput)));
        });

    public Task<Result<IReadOnlyList<RemoteRef>>> Fetch(CancellationToken cancellationToken) =>
        TransportResult.Capture<IReadOnlyList<RemoteRef>>(async () =>
        {
            GitArgumentGuards.UsableRemoteUrl(_config.RemoteUrl);
            var fetched = await RunRemoteAsync([.. CredentialIsolation, .. IdentityArguments(), "fetch", "--prune", "origin"],
                _config.WatchedFolder, cancellationToken).ConfigureAwait(false);
            GitFailure.EnsureSuccess(fetched, "fetch");
            var tracked = await RunLocalAsync(
                ["for-each-ref", "--format=%(objectname)%09%(refname)", "refs/remotes/origin/"], cancellationToken)
                .ConfigureAwait(false);
            GitFailure.EnsureSuccess(tracked, "list tracking refs");

            return new Result<IReadOnlyList<RemoteRef>>.Ok(RemoteRefParser.ParseTracking(tracked.StandardOutput));
        });

    public Task<Result<SyncOutcome>> Pull(string branch, CancellationToken cancellationToken) =>
        TransportResult.Capture<SyncOutcome>(async () =>
        {
            GitArgumentGuards.UsableRemoteUrl(_config.RemoteUrl);
            var name = GitArgumentGuards.RefName(branch);
            var before = await ReadHeadAsync(cancellationToken).ConfigureAwait(false);
            var fetched = await RunRemoteAsync([.. CredentialIsolation, .. IdentityArguments(), "fetch", "origin", name],
                _config.WatchedFolder, cancellationToken).ConfigureAwait(false);
            GitFailure.EnsureSuccess(fetched, "fetch");
            var merged = await RunLocalAsync([.. IdentityArguments(), "merge", "--ff-only", "FETCH_HEAD"], cancellationToken)
                .ConfigureAwait(false);
            GitFailure.EnsureSuccess(merged, "merge");
            var after = await ReadHeadAsync(cancellationToken).ConfigureAwait(false);

            return new Result<SyncOutcome>.Ok(new SyncOutcome(name, before, after));
        });

    public Task<Result<SyncOutcome>> Push(string branch, CancellationToken cancellationToken) =>
        TransportResult.Capture<SyncOutcome>(async () =>
        {
            GitArgumentGuards.UsableRemoteUrl(_config.RemoteUrl);
            var name = GitArgumentGuards.RefName(branch);
            var remoteBefore = await RemoteTipAsync(name, cancellationToken).ConfigureAwait(false);
            var pushed = await RunRemoteAsync(
                [.. CredentialIsolation, .. IdentityArguments(), "push", "origin", name],
                _config.WatchedFolder, cancellationToken).ConfigureAwait(false);
            GitFailure.EnsureSuccess(pushed, "push");

            return new Result<SyncOutcome>.Ok(
                new SyncOutcome(name, remoteBefore, await ReadHeadAsync(cancellationToken).ConfigureAwait(false)));
        });

    public Task<Result<SyncOutcome>> PushForceWithLease(string branch, string expectedRemoteSha, CancellationToken cancellationToken) =>
        TransportResult.Capture<SyncOutcome>(async () =>
        {
            GitArgumentGuards.UsableRemoteUrl(_config.RemoteUrl);
            var name = GitArgumentGuards.RefName(branch);
            GitArgumentGuards.Sha(expectedRemoteSha);
            var pushed = await RunRemoteAsync(
                [.. CredentialIsolation,
                 "push",
                 $"--force-with-lease=refs/heads/{name}:{expectedRemoteSha}",
                 "origin",
                 name],
                _config.WatchedFolder, cancellationToken).ConfigureAwait(false);
            GitFailure.EnsureSuccess(pushed, "force push with lease");

            return new Result<SyncOutcome>.Ok(
                new SyncOutcome(name, expectedRemoteSha, await ReadHeadAsync(cancellationToken).ConfigureAwait(false)));
        });

    public Task<Result<IReadOnlyList<RemoteRef>>> ListBranch(CancellationToken cancellationToken) =>
        TransportResult.Capture<IReadOnlyList<RemoteRef>>(async () =>
        {
            GitArgumentGuards.UsableRemoteUrl(_config.RemoteUrl);
            var listing = await RunRemoteAsync(
                [.. CredentialIsolation, "ls-remote", "--heads", _config.RemoteUrl], null, cancellationToken)
                .ConfigureAwait(false);
            GitFailure.EnsureSuccess(listing, "list branches");

            return new Result<IReadOnlyList<RemoteRef>>.Ok(RemoteRefParser.ParseHeads(listing.StandardOutput));
        });

    private Task<GitCommandResult> RunProbeAsync(CancellationToken cancellationToken)
    {
        GitInvocation invocation = new()
        {
            Arguments = [.. CredentialIsolation, "ls-remote", "--symref", _config.RemoteUrl, "HEAD"],
            Environment = GitEnv.PromptDisabled
        };
        return _runner.RunAsync(invocation, cancellationToken);
    }

    private async Task<GitCommandResult> RunRemoteAsync(
        IReadOnlyList<string> command, string? workingDirectory, CancellationToken cancellationToken)
    {
        if (!UsesPassword(_config.RemoteUrl))
        {
            return await _runner.RunAsync(new GitInvocation
            {
                Arguments = command,
                WorkingDirectory = workingDirectory,
                Environment = GitEnv.PromptDisabled
            }, cancellationToken).ConfigureAwait(false);
        }

        using var injector = CredentialInjector.Create(await RequireToken(cancellationToken).ConfigureAwait(false));
        return await _runner.RunAsync(new GitInvocation
        {
            Arguments = command,
            WorkingDirectory = workingDirectory,
            Environment = Merge(GitEnv.PromptDisabled, injector.Environment)
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task<GitCommandResult> RunLocalAsync(IReadOnlyList<string> command, CancellationToken cancellationToken)
    {
        GitInvocation invocation = new()
        {
            Arguments = command,
            WorkingDirectory = _config.WatchedFolder
        };
        return _runner.RunAsync(invocation, cancellationToken);
    }

    private async Task<PersonalAccessToken> RequireToken(CancellationToken cancellationToken)
    {
        var outcome = await _credentials.Read(cancellationToken).ConfigureAwait(false);
        return outcome switch
        {
            Result<PersonalAccessToken>.Ok token => token.Value,
            Result<PersonalAccessToken>.Fail failure => throw new GitCredentialUnavailableException(failure.Error),
            _ => throw new InvalidOperationException("credential outcome is neither ok nor fail")
        };
    }

    private async Task<string> ReadHeadAsync(CancellationToken cancellationToken)
    {
        var head = await RunLocalAsync(["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
        GitFailure.EnsureSuccess(head, "read HEAD");
        return head.StandardOutput.Trim();
    }

    private async Task<string> RemoteTipAsync(string branch, CancellationToken cancellationToken)
    {
        var listing = await RunRemoteAsync(
            [.. CredentialIsolation, "ls-remote", _config.RemoteUrl, $"refs/heads/{branch}"], null, cancellationToken)
            .ConfigureAwait(false);
        GitFailure.EnsureSuccess(listing, "list remote tip");
        var line = listing.StandardOutput
            .Split('\n')
            .FirstOrDefault(candidate => candidate.Trim().Length > 0);

        return line is null ? string.Empty : line.Split('\t')[0].Trim();
    }

    private IReadOnlyList<string> IdentityArguments() =>
        ["-c", $"user.name={_config.Identity.Name}", "-c", $"user.email={_config.Identity.Email}"];

    private static bool UsesPassword(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> first, IReadOnlyDictionary<string, string> second)
    {
        var merged = new Dictionary<string, string>(first);
        foreach (var (key, value) in second)
        {
            merged[key] = value;
        }

        return merged;
    }
}
