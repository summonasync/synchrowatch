using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Transport;

namespace Syncrowatch.Core.Engine.Conflicts;

public sealed class ResolutionExecutor(
    SyncConfig config,
    ILocalGit local,
    IConflictStore store,
    TimeProvider? timeProvider = null) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly KeepBothStager _stager = new(config.WatchedFolder, timeProvider);

    public async Task<Result<ConflictResolution>> Resolve(
        string relativePath, ConflictOption option, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pending = await store.Pending().ConfigureAwait(false);
            if (pending is Result<IReadOnlyList<ConflictRecord>>.Fail pendingFailure)
            {
                return new Result<ConflictResolution>.Fail(pendingFailure.Error);
            }

            var record = ((Result<IReadOnlyList<ConflictRecord>>.Ok)pending).Value
                .FirstOrDefault(candidate => candidate.Path == relativePath);
            if (record is null)
            {
                return new Result<ConflictResolution>.Fail(SyncError.NotFound(
                    $"no pending conflict tracked for '{relativePath}'"));
            }

            var mergeState = await local.HasMergeInProgress(config.WatchedFolder, cancellationToken).ConfigureAwait(false);
            if (mergeState is Result<bool>.Fail mergeFailure)
            {
                return new Result<ConflictResolution>.Fail(mergeFailure.Error);
            }

            if (((Result<bool>.Ok)mergeState).Value is false)
            {
                var resumed = await ResumeMergeAsync(record, cancellationToken).ConfigureAwait(false);
                if (resumed is not null)
                {
                    return resumed;
                }
            }

            var applied = await ApplyOptionAsync(record, option, cancellationToken).ConfigureAwait(false);
            if (applied is Result<Unit>.Fail applyFailure)
            {
                return new Result<ConflictResolution>.Fail(applyFailure.Error);
            }

            var stagedRecord = record with { Status = ConflictRecordStatus.Staged, Option = option };
            var saved = await store.Save(stagedRecord).ConfigureAwait(false);
            if (saved is Result<Unit>.Fail saveFailure)
            {
                return new Result<ConflictResolution>.Fail(saveFailure.Error);
            }

            return await FinalizeAsync(stagedRecord, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<Result<ConflictResolution>?> ResumeMergeAsync(
        ConflictRecord record, CancellationToken cancellationToken)
    {
        var merged = await local.MergeNoCommit(config.WatchedFolder, "FETCH_HEAD", cancellationToken).ConfigureAwait(false);
        if (merged is Result<MergeOutcome>.Fail mergeFailure)
        {
            return new Result<ConflictResolution>.Fail(mergeFailure.Error);
        }

        if (((Result<MergeOutcome>.Ok)merged).Value is not MergeOutcome.Merged)
        {
            return null;
        }

        var mergeState = await local.HasMergeInProgress(config.WatchedFolder, cancellationToken).ConfigureAwait(false);
        if (mergeState is Result<bool>.Ok pendingMerge && pendingMerge.Value)
        {
            var committed = await local.CommitMerge(config.WatchedFolder, config.Identity, AutoConvergedMessage(record), cancellationToken)
                .ConfigureAwait(false);
            if (committed is Result<string>.Fail commitFailure)
            {
                return new Result<ConflictResolution>.Fail(commitFailure.Error);
            }

            return await CloseConflictAsync([record], ((Result<string>.Ok)committed).Value, true, cancellationToken)
                .ConfigureAwait(false);
        }

        return await CloseConflictAsync([record], null, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<Unit>> ApplyOptionAsync(
        ConflictRecord record, ConflictOption option, CancellationToken cancellationToken)
    {
        var folder = config.WatchedFolder;
        var listed = await local.ReadConflictedIndex(folder, cancellationToken).ConfigureAwait(false);
        if (listed is Result<IReadOnlyList<ConflictIndexEntry>>.Fail listingFailure)
        {
            return new Result<Unit>.Fail(listingFailure.Error);
        }

        var stages = ((Result<IReadOnlyList<ConflictIndexEntry>>.Ok)listed).Value
            .Where(entry => entry.Path == record.Path)
            .ToList();
        if (stages.Count == 0)
        {
            if (option is not ConflictOption.KeepCopy)
            {
                DiscardStaleCopy(record);
            }

            return new Result<Unit>.Ok(Unit.Value);
        }

        var ours = stages.FirstOrDefault(entry => entry.Stage == 2);
        var theirs = stages.FirstOrDefault(entry => entry.Stage == 3);
        var oursContent = ours is null
            ? null
            : await ReadBlobAsync(folder, ours.BlobSha, cancellationToken).ConfigureAwait(false);
        if (oursContent is Result<byte[]>.Fail oursFailure)
        {
            return new Result<Unit>.Fail(oursFailure.Error);
        }

        byte[]? OursBytes() => ours is null ? null : ((Result<byte[]>.Ok)oursContent!).Value;

        if (option is ConflictOption.KeepCopy or ConflictOption.KeepMine)
        {
            if (ours is null)
            {
                return await RemoveAsync(folder, record.Path, cancellationToken).ConfigureAwait(false);
            }

            var written = WriteCanonicalFile(folder, record.Path, OursBytes()!);
            if (written is Result<Unit>.Fail writeFailure)
            {
                return writeFailure;
            }

            if (option == ConflictOption.KeepMine)
            {
                DiscardStaleCopy(record);
            }

            return await StageAsync(folder, record.Path, cancellationToken).ConfigureAwait(false);
        }

        if (theirs is null)
        {
            if (OursBytes() is { } preserved)
            {
                _stager.MaterializeMineCopy(record.Path, preserved);
            }

            DiscardStaleCopy(record);
            return await RemoveAsync(folder, record.Path, cancellationToken).ConfigureAwait(false);
        }

        if (OursBytes() is { } mine)
        {
            _stager.MaterializeMineCopy(record.Path, mine);
        }

        var theirsContent = await ReadBlobAsync(folder, theirs.BlobSha, cancellationToken).ConfigureAwait(false);
        if (theirsContent is not Result<byte[]>.Ok theirsRead)
        {
            return new Result<Unit>.Fail(((Result<byte[]>.Fail)theirsContent).Error);
        }

        var stagedTheirs = WriteCanonicalFile(folder, record.Path, theirsRead.Value);
        if (stagedTheirs is Result<Unit>.Fail theirsWriteFailure)
        {
            return theirsWriteFailure;
        }

        DiscardStaleCopy(record);
        return await StageAsync(folder, record.Path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<ConflictResolution>> FinalizeAsync(
        ConflictRecord record, CancellationToken cancellationToken)
    {
        var folder = config.WatchedFolder;
        var listed = await local.ReadConflictedIndex(folder, cancellationToken).ConfigureAwait(false);
        if (listed is Result<IReadOnlyList<ConflictIndexEntry>>.Fail listingFailure)
        {
            return new Result<ConflictResolution>.Fail(listingFailure.Error);
        }

        var entries = ((Result<IReadOnlyList<ConflictIndexEntry>>.Ok)listed).Value;
        if (entries.Count > 0)
        {
            var remaining = entries.Select(entry => entry.Path).Distinct().ToList();
            return new Result<ConflictResolution>.Ok(ConflictResolution.Staged(record.Path, remaining));
        }

        var committed = await local.CommitMerge(folder, config.Identity, ResolutionMessage(record), cancellationToken)
            .ConfigureAwait(false);
        if (committed is Result<string>.Fail commitFailure)
        {
            return new Result<ConflictResolution>.Fail(commitFailure.Error);
        }

        var staged = await store.Pending().ConfigureAwait(false);
        if (staged is Result<IReadOnlyList<ConflictRecord>>.Fail stagedFailure)
        {
            return new Result<ConflictResolution>.Fail(stagedFailure.Error);
        }

        var toClose = ((Result<IReadOnlyList<ConflictRecord>>.Ok)staged).Value
            .Where(candidate => candidate.Status == ConflictRecordStatus.Staged)
            .ToList();
        return await CloseConflictAsync(toClose, ((Result<string>.Ok)committed).Value, false, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<ConflictResolution>> CloseConflictAsync(
        IReadOnlyList<ConflictRecord> records, string? commitSha, bool autoConverged, CancellationToken cancellationToken)
    {
        var stamp = _time.GetUtcNow();
        foreach (var record in records)
        {
            var closed = record with
            {
                Status = ConflictRecordStatus.Resolved,
                AutoConverged = autoConverged,
                ResolvedUtc = commitSha is null ? record.ResolvedUtc ?? stamp : stamp,
                CommitSha = commitSha ?? record.CommitSha
            };
            var saved = await store.Save(closed).ConfigureAwait(false);
            if (saved is Result<Unit>.Fail saveFailure)
            {
                return new Result<ConflictResolution>.Fail(saveFailure.Error);
            }
        }

        return new Result<ConflictResolution>.Ok(new ConflictResolution(
            records.Select(record => record.Path).ToList(), commitSha, [])
        {
            AutoConverged = autoConverged
        });
    }

    private async Task<Result<byte[]>> ReadBlobAsync(string folder, string blobSha, CancellationToken cancellationToken)
    {
        var blob = await local.ReadBlobBySha(folder, blobSha, cancellationToken).ConfigureAwait(false);
        return blob switch
        {
            Result<BlobContent>.Ok found when found.Value.Found => new Result<byte[]>.Ok(found.Value.Bytes),
            Result<BlobContent>.Ok => new Result<byte[]>.Fail(SyncError.NotFound($"blob {blobSha} is not readable")),
            Result<BlobContent>.Fail failure => new Result<byte[]>.Fail(failure.Error),
            _ => throw new InvalidOperationException("blob outcome is neither ok nor fail")
        };
    }

    private async Task<Result<Unit>> StageAsync(string folder, string path, CancellationToken cancellationToken)
    {
        var staged = await local.StagePaths(folder, [path], cancellationToken).ConfigureAwait(false);
        return staged switch
        {
            Result<StageOutcome>.Ok => new Result<Unit>.Ok(Unit.Value),
            Result<StageOutcome>.Fail failure => new Result<Unit>.Fail(failure.Error),
            _ => throw new InvalidOperationException("stage outcome is neither ok nor fail")
        };
    }

    private async Task<Result<Unit>> RemoveAsync(string folder, string path, CancellationToken cancellationToken)
    {
        var removed = await local.RemovePaths(folder, [path], cancellationToken).ConfigureAwait(false);
        return removed switch
        {
            Result<StageOutcome>.Ok => new Result<Unit>.Ok(Unit.Value),
            Result<StageOutcome>.Fail failure => new Result<Unit>.Fail(failure.Error),
            _ => throw new InvalidOperationException("remove outcome is neither ok nor fail")
        };
    }

    private static string WorktreePath(string folder, string relativePath) =>
        Path.Combine(folder, relativePath.Replace('\\', '/'));

    private static Result<Unit> WriteCanonicalFile(string folder, string relativePath, byte[] content)
    {
        try
        {
            var fullPath = WorktreePath(folder, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, content);
            return new Result<Unit>.Ok(Unit.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<Unit>.Fail(new SyncError("unavailable", $"resolved file '{relativePath}' could not be written"));
        }
    }

    private static string AutoConvergedMessage(ConflictRecord record) =>
        $"auto-converged: incoming already contained the change in {record.Path}";

    private void DiscardStaleCopy(ConflictRecord record)
    {
        if (record.CopyPath is { } copy)
        {
            _stager.DiscardCopy(copy);
        }
    }

    private static string ResolutionMessage(ConflictRecord record)
    {
        var label = record.Option switch
        {
            ConflictOption.KeepCopy => "keep copy",
            ConflictOption.KeepMine => "keep mine",
            ConflictOption.TakeTheirs => "take theirs",
            _ => "stale"
        };
        return $"sync: resolved conflict in {record.Path} ({label})";
    }
}
