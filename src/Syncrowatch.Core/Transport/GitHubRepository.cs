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

    public async Task<Result<RemoteValidation>> ValidateRepository(CancellationToken cancellationToken)
    {
        try
        {
            return await ValidatePrivateRepository(cancellationToken);
        }
        catch (GitTransportException ex)
        {
            return new Result<RemoteValidation>.Fail(ex.Error);
        }
    }

    public async Task<Result<IReadOnlyList<RemoteRef>>> Fetch(CancellationToken cancellationToken)
    {
        try
        {
            await RunRemoteAsync([.. CredentialIsolation, .. IdentityArguments(), "fetch", "--prune", "origin"],
                _config.WatchedFolder, cancellationToken);
            var tracked = await RunLocalAsync(
                ["for-each-ref", "--format=%(objectname)%09%(refname)", "refs/remotes/origin/"], cancellationToken);
            GitFailure.EnsureSuccess(tracked, "list tracking refs");

            return new Result<IReadOnlyList<RemoteRef>>.Ok(RemoteRefParser.ParseTracking(tracked.StandardOutput));
        }
        catch (GitTransportException ex)
        {
            return new Result<IReadOnlyList<RemoteRef>>.Fail(ex.Error);
        }
    }

    public async Task<Result<SyncOutcome>> Pull(string branch, CancellationToken cancellationToken)
    {
        try
        {
            var name = GuardBranch(branch);
            var before = await ReadHeadAsync(cancellationToken);
            await RunRemoteAsync([.. CredentialIsolation, .. IdentityArguments(), "fetch", "origin", name],
                _config.WatchedFolder, cancellationToken);
            await RunLocalAsync([.. IdentityArguments(), "merge", "--ff-only", "FETCH_HEAD"], cancellationToken);
            var after = await ReadHeadAsync(cancellationToken);

            return new Result<SyncOutcome>.Ok(new SyncOutcome(name, before, after));
        }
        catch (GitTransportException ex)
        {
            return new Result<SyncOutcome>.Fail(ex.Error);
        }
    }

    public async Task<Result<SyncOutcome>> Push(string branch, CancellationToken cancellationToken)
    {
        try
        {
            var name = GuardBranch(branch);
            var remoteBefore = await RemoteTipAsync(name, cancellationToken);
            var pushed = await RunRemoteAsync(
                [.. CredentialIsolation, .. IdentityArguments(), "push", "origin", name],
                _config.WatchedFolder, cancellationToken);
            GitFailure.EnsureSuccess(pushed, "push");

            return new Result<SyncOutcome>.Ok(new SyncOutcome(name, remoteBefore, await ReadHeadAsync(cancellationToken)));
        }
        catch (GitTransportException ex)
        {
            return new Result<SyncOutcome>.Fail(ex.Error);
        }
    }

    public async Task<Result<SyncOutcome>> PushForceWithLease(string branch, string expectedRemoteSha, CancellationToken cancellationToken)
    {
        try
        {
            var name = GuardBranch(branch);
            GuardSha(expectedRemoteSha);
            var pushed = await RunRemoteAsync(
                [.. CredentialIsolation,
                 "push",
                 $"--force-with-lease=refs/heads/{name}:{expectedRemoteSha}",
                 "origin",
                 name],
                _config.WatchedFolder, cancellationToken);
            GitFailure.EnsureSuccess(pushed, "force push with lease");

            return new Result<SyncOutcome>.Ok(new SyncOutcome(name, expectedRemoteSha, await ReadHeadAsync(cancellationToken)));
        }
        catch (GitTransportException ex)
        {
            return new Result<SyncOutcome>.Fail(ex.Error);
        }
    }

    public async Task<Result<IReadOnlyList<RemoteRef>>> ListBranch(CancellationToken cancellationToken)
    {
        try
        {
            var listing = await RunRemoteAsync(
                [.. CredentialIsolation, "ls-remote", "--heads", _config.RemoteUrl], null, cancellationToken);
            GitFailure.EnsureSuccess(listing, "list branches");

            return new Result<IReadOnlyList<RemoteRef>>.Ok(RemoteRefParser.ParseHeads(listing.StandardOutput));
        }
        catch (GitTransportException ex)
        {
            return new Result<IReadOnlyList<RemoteRef>>.Fail(ex.Error);
        }
    }

    private async Task<Result<RemoteValidation>> ValidatePrivateRepository(CancellationToken cancellationToken)
    {
        var probe = await RunProbeAsync(cancellationToken);
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
            [.. CredentialIsolation, "ls-remote", "--symref", _config.RemoteUrl, "HEAD"], null, cancellationToken);
        GitFailure.EnsureSuccess(answer, "validate");

        return new Result<RemoteValidation>.Ok(
            new RemoteValidation(true, RemoteRefParser.ParseDefaultBranch(answer.StandardOutput)));
    }

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
            }, cancellationToken);
        }

        using var injector = CredentialInjector.Create(await RequireToken(cancellationToken));
        return await _runner.RunAsync(new GitInvocation
        {
            Arguments = command,
            WorkingDirectory = workingDirectory,
            Environment = Merge(GitEnv.PromptDisabled, injector.Environment)
        }, cancellationToken);
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
        var outcome = await _credentials.Read(cancellationToken);
        return outcome switch
        {
            Result<PersonalAccessToken>.Ok token => token.Value,
            Result<PersonalAccessToken>.Fail failure => throw new GitCredentialUnavailableException(failure.Error),
            _ => throw new InvalidOperationException("credential outcome is neither ok nor fail")
        };
    }

    private async Task<string> ReadHeadAsync(CancellationToken cancellationToken)
    {
        var head = await RunLocalAsync(["rev-parse", "HEAD"], cancellationToken);
        GitFailure.EnsureSuccess(head, "read HEAD");
        return head.StandardOutput.Trim();
    }

    private async Task<string> RemoteTipAsync(string branch, CancellationToken cancellationToken)
    {
        var listing = await RunRemoteAsync(
            [.. CredentialIsolation, "ls-remote", _config.RemoteUrl, $"refs/heads/{branch}"], null, cancellationToken);
        GitFailure.EnsureSuccess(listing, "list remote tip");
        var line = listing.StandardOutput
            .Split('\n')
            .FirstOrDefault(candidate => candidate.Trim().Length > 0);

        return line is null ? string.Empty : line.Split('\t')[0].Trim();
    }

    private IReadOnlyList<string> IdentityArguments() =>
        ["-c", $"user.name={_config.Identity.Name}", "-c", $"user.email={_config.Identity.Email}"];

    private static string GuardBranch(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.StartsWith('-'))
        {
            throw new GitOperationException(SyncError.Invalid($"branch '{branch}' is not a usable ref name"));
        }

        return branch;
    }

    private static void GuardSha(string sha)
    {
        if (sha.Length == 0 || sha.Contains(' ') || !sha.All(char.IsAsciiHexDigit))
        {
            throw new GitOperationException(SyncError.Invalid("expected remote sha is not a hexadecimal object id"));
        }
    }

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
