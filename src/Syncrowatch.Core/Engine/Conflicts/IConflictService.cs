using Syncrowatch.Core;

namespace Syncrowatch.Core.Engine.Conflicts;

public interface IConflictService
{
    Task<Result<IReadOnlyList<PendingConflict>>> Pending(CancellationToken cancellationToken);

    Task<Result<ConflictResolution>> Resolve(string relativePath, ConflictOption option, CancellationToken cancellationToken);
}
