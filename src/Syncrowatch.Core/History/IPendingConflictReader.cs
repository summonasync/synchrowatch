using Synchro.Core;

namespace Syncrowatch.Core.History;

public interface IPendingConflictReader
{
    Task<Result<IReadOnlyList<PendingConflict>>> Read(CancellationToken cancellationToken);
}
