using Synchro.Core;

namespace Syncrowatch.Core;

public interface ISyncEngine
{
    Task<Result<SyncCycleReport>> SyncOnce(CancellationToken cancellationToken);

    Task<Result<EngineStatus>> GetStatus(CancellationToken cancellationToken);
}
