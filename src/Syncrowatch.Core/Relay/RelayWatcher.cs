using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Transport;

namespace Syncrowatch.Core.Relay;

public sealed class RelayWatcher(
    SyncConfig config,
    IRemoteRepository remote,
    IGitRunner runner,
    IRelaySeenState seen,
    ISyncEventBus bus,
    TimeProvider? timeProvider = null) : IRelayWatcher
{
    private const string PackageSuffix = ".synchr";
    private const string TrackingPrefix = "refs/remotes/origin/";

    private readonly SyncConfig _config = config;
    private readonly IRemoteRepository _remote = remote;
    private readonly IGitRunner _runner = runner;
    private readonly IRelaySeenState _seen = seen;
    private readonly ISyncEventBus _bus = bus;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<Result<IReadOnlyList<RelayPackage>>> Poll(CancellationToken cancellationToken)
    {
        var branches = await _remote.ListBranch(cancellationToken).ConfigureAwait(false);
        if (branches is Result<IReadOnlyList<RemoteRef>>.Fail listingFailure)
        {
            return new Result<IReadOnlyList<RelayPackage>>.Fail(listingFailure.Error);
        }

        if (((Result<IReadOnlyList<RemoteRef>>.Ok)branches).Value.All(branch => branch.Name != IRelayWatcher.RelayBranchName))
        {
            return new Result<IReadOnlyList<RelayPackage>>.Ok([]);
        }

        var fetched = await _remote.Fetch(cancellationToken).ConfigureAwait(false);
        if (fetched is Result<IReadOnlyList<RemoteRef>>.Fail fetchFailure)
        {
            return new Result<IReadOnlyList<RelayPackage>>.Fail(fetchFailure.Error);
        }

        var tree = await ListTreeAsync(cancellationToken).ConfigureAwait(false);
        if (tree is Result<List<RelayPackage>>.Fail treeFailure)
        {
            return new Result<IReadOnlyList<RelayPackage>>.Fail(treeFailure.Error);
        }

        var seenPackages = _seen.Load();
        var known = seenPackages.ToHashSet();
        var fresh = ((Result<List<RelayPackage>>.Ok)tree).Value
            .Where(package => !known.Contains(new RelaySeenPackage(package.RelayPath, package.BlobSha)))
            .ToList();
        foreach (var package in fresh)
        {
            _bus.Publish(new SyncNotification.RelayPackageDetected(
                _time.GetUtcNow(), package.Project, package.PackageFile, package.CliCommand));
        }

        if (fresh.Count > 0)
        {
            var merged = seenPackages
                .Concat(fresh.Select(package => new RelaySeenPackage(package.RelayPath, package.BlobSha)))
                .ToList();
            var saved = await _seen.Save(merged, cancellationToken).ConfigureAwait(false);
            if (saved is Result<Unit>.Fail saveFailure)
            {
                return new Result<IReadOnlyList<RelayPackage>>.Fail(saveFailure.Error);
            }
        }

        return new Result<IReadOnlyList<RelayPackage>>.Ok(fresh);
    }

    private async Task<Result<List<RelayPackage>>> ListTreeAsync(CancellationToken cancellationToken)
    {
        var listing = await _runner.RunAsync(new GitInvocation
        {
            Arguments = ["ls-tree", "-r", "-z", TrackingPrefix + IRelayWatcher.RelayBranchName],
            WorkingDirectory = _config.WatchedFolder,
            Environment = GitEnv.PromptDisabled
        }, cancellationToken).ConfigureAwait(false);

        if (!listing.Succeeded)
        {
            return new Result<List<RelayPackage>>.Fail(
                new SyncError("relay_tree", $"relay branch tree could not be read: {listing.StandardError.Trim()}"));
        }

        var packages = listing.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => TryParse(entry, _config.Identity.Name))
            .OfType<RelayPackage>()
            .OrderBy(package => package.RelayPath, StringComparer.Ordinal)
            .ToList();
        return new Result<List<RelayPackage>>.Ok(packages);
    }

    private static RelayPackage? TryParse(string entry, string peerName)
    {
        var tab = entry.IndexOf('\t');
        if (tab <= 0)
        {
            return null;
        }

        var meta = entry[..tab].Split(' ');
        var path = entry[(tab + 1)..];
        var segments = path.Split('/');
        if (meta.Length != 3
            || meta[1] != "blob"
            || meta[2].Length == 0
            || !meta[2].All(char.IsAsciiHexDigit)
            || segments.Length != 2
            || !segments[1].EndsWith(PackageSuffix, StringComparison.Ordinal))
        {
            return null;
        }

        var packageFile = segments[1];
        return new RelayPackage(
            segments[0],
            packageFile,
            meta[2],
            path,
            $"synchro apply \"{path}\" --peer \"{peerName}\"");
    }
}
