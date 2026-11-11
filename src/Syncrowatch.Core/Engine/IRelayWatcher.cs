using Synchro.Core;

namespace Syncrowatch.Core.Engine;

public interface IRelayWatcher
{
    const string RelayBranchName = "relay/.synchr";

    Task<Result<RemoteRef?>> Poll(CancellationToken cancellationToken);
}
