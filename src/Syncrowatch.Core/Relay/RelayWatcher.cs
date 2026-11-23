using Syncrowatch.Core;
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
    private int? _lastSkippedCount;

    public async Task<Result<IReadOnlyList<RelayPackage>>> Poll(CancellationToken cancellationToken)
    {
        var branches = await _remote.ListBranch(cancellationToken).ConfigureAwait(false);
        if (branches is Result<IReadOnlyList<RemoteRef>>.Fail listingFailure)
        {
            return new Result<IReadOnlyList<RelayPackage>>.Fail(Scrubbed(listingFailure.Error));
        }

        if (((Result<IReadOnlyList<RemoteRef>>.Ok)branches).Value.All(branch => branch.Name != IRelayWatcher.RelayBranchName))
        {
            return new Result<IReadOnlyList<RelayPackage>>.Ok([]);
        }

        var fetched = await _remote.Fetch(cancellationToken).ConfigureAwait(false);
        if (fetched is Result<IReadOnlyList<RemoteRef>>.Fail fetchFailure)
        {
            return new Result<IReadOnlyList<RelayPackage>>.Fail(Scrubbed(fetchFailure.Error));
        }

        var tree = await ListTreeAsync(cancellationToken).ConfigureAwait(false);
        if (tree is Result<TreeScan>.Fail treeFailure)
        {
            return new Result<IReadOnlyList<RelayPackage>>.Fail(treeFailure.Error);
        }

        var scan = ((Result<TreeScan>.Ok)tree).Value;
        if (scan.SkippedCount > 0 && scan.SkippedCount != _lastSkippedCount)
        {
            _bus.Publish(new SyncNotification.RelayEntriesSkipped(_time.GetUtcNow(), scan.SkippedCount));
        }

        _lastSkippedCount = scan.SkippedCount;

        var snapshot = _seen.Load();
        if (snapshot.RecoveredFromCorruption)
        {
            _bus.Publish(new SyncNotification.RelayStateCorrupted(_time.GetUtcNow()));
        }

        var known = snapshot.Packages.ToHashSet();
        var fresh = scan.Packages
            .Where(package => !known.Contains(new RelaySeenPackage(package.RelayPath, package.BlobSha)))
            .ToList();
        foreach (var package in fresh)
        {
            _bus.Publish(new SyncNotification.RelayPackageDetected(
                _time.GetUtcNow(), package.Project, package.PackageFile, package.CliCommand));
        }

        if (fresh.Count > 0)
        {
            var merged = snapshot.Packages
                .Concat(fresh.Select(package => new RelaySeenPackage(package.RelayPath, package.BlobSha)))
                .ToList();
            var saved = await _seen.Save(merged, cancellationToken).ConfigureAwait(false);
            if (saved is Result<Unit>.Fail saveFailure)
            {
                return new Result<IReadOnlyList<RelayPackage>>.Fail(Scrubbed(saveFailure.Error));
            }
        }

        return new Result<IReadOnlyList<RelayPackage>>.Ok(fresh);
    }

    private async Task<Result<TreeScan>> ListTreeAsync(CancellationToken cancellationToken)
    {
        var listing = await _runner.RunAsync(new GitInvocation
        {
            Arguments = ["ls-tree", "-r", "-z", TrackingPrefix + IRelayWatcher.RelayBranchName],
            WorkingDirectory = _config.WatchedFolder,
            Environment = GitEnv.PromptDisabled
        }, cancellationToken).ConfigureAwait(false);

        if (!listing.Succeeded)
        {
            return new Result<TreeScan>.Fail(new SyncError(
                "relay_tree",
                RemoteUrlRedactor.Scrub($"relay branch tree could not be read: {listing.StandardError.Trim()}")));
        }

        List<RelayPackage> packages = [];
        var skipped = 0;
        foreach (var entry in listing.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var parsed = TryParse(entry, _config.Identity.Name);
            switch (parsed.Verdict)
            {
                case EntryVerdict.Package:
                    packages.Add(parsed.Package!);
                    break;
                case EntryVerdict.Malformed:
                    skipped++;
                    break;
            }
        }

        packages.Sort((left, right) => string.CompareOrdinal(left.RelayPath, right.RelayPath));
        return new Result<TreeScan>.Ok(new TreeScan(packages, skipped));
    }

    private static SyncError Scrubbed(SyncError error) =>
        error with { Message = RemoteUrlRedactor.Scrub(error.Message) };

    private static bool IsSafeSegment(string segment) =>
        segment.Length > 0
        && segment.All(candidate => candidate
            is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '.' or '_' or '-');

    private static (EntryVerdict Verdict, RelayPackage? Package) TryParse(string entry, string peerName)
    {
        var tab = entry.IndexOf('\t');
        if (tab <= 0)
        {
            return (EntryVerdict.NotPackage, null);
        }

        var meta = entry[..tab].Split(' ');
        var path = entry[(tab + 1)..];
        var segments = path.Split('/');
        var looksLikePackage = segments.Length == 2 && segments[1].EndsWith(PackageSuffix, StringComparison.Ordinal);
        if (!looksLikePackage)
        {
            return (EntryVerdict.NotPackage, null);
        }

        if (meta.Length != 3
            || meta[1] != "blob"
            || meta[2].Length == 0
            || !meta[2].All(char.IsAsciiHexDigit)
            || !IsSafeSegment(segments[0])
            || !IsSafeSegment(segments[1]))
        {
            return (EntryVerdict.Malformed, null);
        }

        var packageFile = segments[1];
        return (EntryVerdict.Package, new RelayPackage(
            segments[0],
            packageFile,
            meta[2],
            path,
            $"synchro apply \"{path}\" --peer \"{peerName}\""));
    }

    private enum EntryVerdict
    {
        NotPackage,
        Malformed,
        Package
    }

    private sealed record TreeScan(IReadOnlyList<RelayPackage> Packages, int SkippedCount);
}
