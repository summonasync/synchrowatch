using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;

namespace Syncrowatch.Tests;

public sealed class FakeSyncEngine : ISyncEngine
{
    public int StartCount { get; private set; }

    public int SyncCount { get; private set; }

    public int ConfirmCount { get; private set; }

    public int PauseCount { get; private set; }

    public int ResumeCount { get; private set; }

    public int DisposeCount { get; private set; }

    public SyncRunMode? AppliedMode { get; private set; }

    public List<IReadOnlyList<string>> AcceptedBatches { get; } = [];

    public Func<CancellationToken, Result<SyncCycleReport>>? SyncOnceBehavior { get; set; }

    public void Start(CancellationToken cancellationToken = default) => StartCount++;

    public void SetMode(SyncRunMode mode) => AppliedMode = mode;

    public void Pause() => PauseCount++;

    public void Resume() => ResumeCount++;

    public void AcceptBatch(IReadOnlyList<string> relativePaths) => AcceptedBatches.Add(relativePaths);

    public Task<Result<SyncCycleReport>> SyncOnce(CancellationToken cancellationToken)
    {
        SyncCount++;
        return Task.FromResult(
            SyncOnceBehavior?.Invoke(cancellationToken)
            ?? new Result<SyncCycleReport>.Ok(new SyncCycleReport(DateTimeOffset.UtcNow, null, null)));
    }

    public Task<Result<EngineStatus>> GetStatus(CancellationToken cancellationToken) =>
        Task.FromResult<Result<EngineStatus>>(new Result<EngineStatus>.Ok(new EngineStatus(StartCount > 0, null, null)));

    public async Task<Result<SyncOutcome>> ConfirmPendingPush(CancellationToken cancellationToken)
    {
        ConfirmCount++;
        await Task.Yield();
        return new Result<SyncOutcome>.Ok(new SyncOutcome("main", "staged-from", "staged-to"));
    }

    public void Dispose() => DisposeCount++;
}
