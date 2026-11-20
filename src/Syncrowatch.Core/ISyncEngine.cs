using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;

namespace Syncrowatch.Core;

public interface ISyncEngine : IDisposable
{
    void Start(CancellationToken cancellationToken = default);

    void SetMode(SyncRunMode mode);

    void Pause();

    void Resume();

    void AcceptBatch(IReadOnlyList<string> relativePaths);

    Task<Result<SyncCycleReport>> SyncOnce(CancellationToken cancellationToken);

    Task<Result<EngineStatus>> GetStatus(CancellationToken cancellationToken);
}
