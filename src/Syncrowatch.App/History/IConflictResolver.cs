using Syncrowatch.Core;
using Syncrowatch.Core.Engine.Conflicts;

namespace Syncrowatch.App.History;

public interface IConflictResolver
{
    Task<Result<ConflictResolution>> Resolve(string relativePath, ConflictOption option, CancellationToken cancellationToken);
}
