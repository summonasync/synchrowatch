using Synchro.Core;

namespace Syncrowatch.Core.Relay;

public interface IRelaySeenState
{
    IReadOnlyList<RelaySeenPackage> Load();

    Task<Result<Unit>> Save(IReadOnlyList<RelaySeenPackage> packages, CancellationToken cancellationToken);
}
