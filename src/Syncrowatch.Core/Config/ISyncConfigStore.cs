using Syncrowatch.Core;

namespace Syncrowatch.Core.Config;

public interface ISyncConfigStore
{
    Task<Result<SyncConfig>> Load(CancellationToken cancellationToken);

    Task<Result<SyncConfig>> Save(SyncConfig config, CancellationToken cancellationToken);
}
