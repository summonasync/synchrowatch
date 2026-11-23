using Syncrowatch.Core;
using Syncrowatch.Core.Relay;

namespace Syncrowatch.Core.Engine;

public interface IRelayWatcher
{
    const string RelayBranchName = "relay/synchr";

    Task<Result<IReadOnlyList<RelayPackage>>> Poll(CancellationToken cancellationToken);
}
