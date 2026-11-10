using Synchro.Core;

namespace Syncrowatch.Core;

public interface ISyncConfigStore
{
    Task<Result<SyncConfig>> Load(CancellationToken cancellationToken);

    Task<Result<SyncConfig>> Save(SyncConfig config, CancellationToken cancellationToken);
}
