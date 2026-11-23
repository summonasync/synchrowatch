using Syncrowatch.Core;
using Syncrowatch.Core.History;

namespace Syncrowatch.App.History;

public sealed class EmptyPendingConflictReader : IPendingConflictReader
{
    public Task<Result<IReadOnlyList<PendingConflict>>> Read(CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<PendingConflict>>>(new Result<IReadOnlyList<PendingConflict>>.Ok([]));
}
