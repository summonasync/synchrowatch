using Synchro.Core;
using Syncrowatch.Core.History;
using ConflictStore = Syncrowatch.Core.Engine.Conflicts.IConflictStore;
using TrackedConflict = Syncrowatch.Core.Engine.Conflicts.ConflictRecord;

namespace Syncrowatch.App.History;

public sealed class ConflictPendingReader(ConflictStore store) : IPendingConflictReader
{
    private readonly ConflictStore _store = store;

    public async Task<Result<IReadOnlyList<PendingConflict>>> Read(CancellationToken cancellationToken)
    {
        var pending = await _store.Pending().ConfigureAwait(true);
        return pending switch
        {
            Result<IReadOnlyList<TrackedConflict>>.Ok records =>
                new Result<IReadOnlyList<PendingConflict>>.Ok(records.Value
                    .Select(record => new PendingConflict(
                        record.Path,
                        record.Path,
                        record.CopyPath ?? record.Path,
                        record.DetectedUtc))
                    .ToList()),
            Result<IReadOnlyList<TrackedConflict>>.Fail failure =>
                new Result<IReadOnlyList<PendingConflict>>.Fail(failure.Error),
            _ => throw new InvalidOperationException("conflict store outcome is neither ok nor fail")
        };
    }
}
