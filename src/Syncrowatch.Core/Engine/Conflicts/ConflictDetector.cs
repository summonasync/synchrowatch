using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Transport;

namespace Syncrowatch.Core.Engine.Conflicts;

public sealed class ConflictDetector(
    SyncConfig config,
    ILocalGit local,
    IConflictStore store,
    TimeProvider? timeProvider = null)
{
    private static readonly IReadOnlyList<string> CopyExclusions = ["*(theirs *)*", "*(mine)*"];

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<Result<IReadOnlyList<ConflictItem>>> HandleConflicted(
        IReadOnlyList<string> conflictedPaths, CancellationToken cancellationToken)
    {
        var folder = config.WatchedFolder;
        var excluded = await local.ApplyExcludes(folder, CopyExclusions, cancellationToken).ConfigureAwait(false);
        if (excluded is Result<Unit>.Fail excludeFailure)
        {
            return new Result<IReadOnlyList<ConflictItem>>.Fail(excludeFailure.Error);
        }

        var known = await store.Pending().ConfigureAwait(false);
        if (known is Result<IReadOnlyList<ConflictRecord>>.Fail knownFailure)
        {
            return new Result<IReadOnlyList<ConflictItem>>.Fail(knownFailure.Error);
        }

        var tracked = ((Result<IReadOnlyList<ConflictRecord>>.Ok)known).Value
            .Select(record => record.Path)
            .ToHashSet(StringComparer.Ordinal);
        var stager = new KeepBothStager(folder, _time);
        var items = new List<ConflictItem>();
        foreach (var relativePath in conflictedPaths)
        {
            if (tracked.Contains(relativePath))
            {
                continue;
            }

            var item = await ClassifyAsync(folder, relativePath, stager, cancellationToken).ConfigureAwait(false);
            if (item is not Result<ConflictItem>.Ok classified)
            {
                return new Result<IReadOnlyList<ConflictItem>>.Fail(((Result<ConflictItem>.Fail)item).Error);
            }

            if (!DeletionGuard.Survives(classified.Value, folder))
            {
                return new Result<IReadOnlyList<ConflictItem>>.Fail(SyncError.Conflict(
                    $"delete would silently win over '{relativePath}'"));
            }

            var saved = await store.Save(ToRecord(classified.Value)).ConfigureAwait(false);
            if (saved is Result<Unit>.Fail saveFailure)
            {
                return new Result<IReadOnlyList<ConflictItem>>.Fail(saveFailure.Error);
            }

            items.Add(classified.Value);
        }

        return new Result<IReadOnlyList<ConflictItem>>.Ok(items);
    }

    private async Task<Result<ConflictItem>> ClassifyAsync(
        string folder, string relativePath, KeepBothStager stager, CancellationToken cancellationToken)
    {
        var ours = await local.ReadBlob(folder, "HEAD", relativePath, cancellationToken).ConfigureAwait(false);
        if (ours is not Result<BlobContent>.Ok oursRead)
        {
            return new Result<ConflictItem>.Fail(((Result<BlobContent>.Fail)ours).Error);
        }

        var theirs = await local.ReadBlob(folder, "FETCH_HEAD", relativePath, cancellationToken).ConfigureAwait(false);
        if (theirs is not Result<BlobContent>.Ok theirsRead)
        {
            return new Result<ConflictItem>.Fail(((Result<BlobContent>.Fail)theirs).Error);
        }

        var oursBlob = ((Result<BlobContent>.Ok)ours).Value;
        var theirsBlob = ((Result<BlobContent>.Ok)theirs).Value;
        var copyPath = theirsBlob.Found ? stager.MaterializeTheirsCopy(relativePath, theirsBlob.Bytes) : null;
        var kind = (oursBlob.Found, theirsBlob.Found) switch
        {
            (true, false) => ConflictKind.ModifiedLocallyDeletedRemotely,
            (false, true) => ConflictKind.DeletedLocallyModifiedRemotely,
            _ when BinaryContentSniffer.IsBinary(oursBlob.Bytes) || BinaryContentSniffer.IsBinary(theirsBlob.Bytes) =>
                ConflictKind.BinaryConflict,
            _ => ConflictKind.TextConflict
        };
        return new Result<ConflictItem>.Ok(new ConflictItem(relativePath, kind, copyPath));
    }

    private ConflictRecord ToRecord(ConflictItem item)
    {
        return new ConflictRecord
        {
            Id = Guid.NewGuid(),
            Path = item.Path,
            Kind = item.Kind,
            CopyPath = item.CopyPath,
            DetectedUtc = _time.GetUtcNow(),
            Status = ConflictRecordStatus.Pending
        };
    }
}
