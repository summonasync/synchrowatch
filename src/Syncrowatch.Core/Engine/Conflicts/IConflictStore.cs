using Syncrowatch.Core;

namespace Syncrowatch.Core.Engine.Conflicts;

public interface IConflictStore
{
    Task<Result<IReadOnlyList<ConflictRecord>>> All();

    Task<Result<IReadOnlyList<ConflictRecord>>> Pending();

    Task<Result<Unit>> Save(ConflictRecord record);
}
