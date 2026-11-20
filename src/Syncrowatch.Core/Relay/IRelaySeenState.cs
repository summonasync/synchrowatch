using Synchro.Core;

namespace Syncrowatch.Core.Relay;

public interface IRelaySeenState
{
    RelaySeenSnapshot Load();

    Task<Result<Unit>> Save(IReadOnlyList<RelaySeenPackage> packages, CancellationToken cancellationToken);
}
