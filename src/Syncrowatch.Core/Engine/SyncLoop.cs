using System.Collections.Concurrent;
using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Transport;

namespace Syncrowatch.Core.Engine;

public sealed class SyncLoop : ISyncEngine, IDisposable
{
    private const string ConflictCode = "conflict";

    private static readonly SyncError DirtyTree =
        new("dirty_tree", "watched folder carries uncommitted changes; sync cycle refused");

    private readonly SyncConfig _config;
    private readonly IRemoteRepository _remote;
    private readonly ILocalGit _local;
    private readonly ISyncEventStore _history;
    private readonly ISyncEventBus _bus;
    private readonly BatchCommitter _committer;
    private readonly ITimer _timer;
    private readonly TimeProvider _time;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly object _statusGate = new();
    private int _inCycle;
    private int _disposed;
    private bool _started;
    private DateTimeOffset? _lastCycleUtc;
    private SyncError? _lastError;
    private CancellationToken _cycleToken = CancellationToken.None;
    private CancellationTokenRegistration _registration;
    private Task _cycleTask = Task.CompletedTask;

    public SyncLoop(
        SyncConfig config,
        IRemoteRepository remote,
        ILocalGit local,
        ISyncEventStore history,
        ISyncEventBus bus,
        TimeProvider? timeProvider = null)
    {
        _config = config;
        _remote = remote;
        _local = local;
        _history = history;
        _bus = bus;
        _time = timeProvider ?? TimeProvider.System;
        _committer = new BatchCommitter(config, local);
        _timer = _time.CreateTimer(Tick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public bool IsRunning
    {
        get
        {
            lock (_statusGate)
            {
                return _started && _disposed == 0;
            }
        }
    }

    public int PendingBatchCount => _pending.Count;

    public void AcceptBatch(IReadOnlyList<string> relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            _pending.Enqueue(relativePath);
        }
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        lock (_statusGate)
        {
            _started = true;
            _cycleToken = cancellationToken;
            _registration = cancellationToken.Register(Dispose);
        }

        Rearm();
    }

    public async Task<Result<SyncCycleReport>> SyncOnce(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _inCycle, 1, 0) == 1)
        {
            return new Result<SyncCycleReport>.Fail(new SyncError("busy", "a sync cycle is already running"));
        }

        try
        {
            return await CycleAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _inCycle, 0);
        }
    }

    public Task<Result<EngineStatus>> GetStatus(CancellationToken cancellationToken)
    {
        lock (_statusGate)
        {
            var status = new EngineStatus(_started && _disposed == 0, _lastCycleUtc, _lastError);
            return Task.FromResult<Result<EngineStatus>>(new Result<EngineStatus>.Ok(status));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _registration.Dispose();
        _timer.Dispose();
    }

    private void Tick(object? state)
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        Rearm();
        if (Interlocked.CompareExchange(ref _inCycle, 1, 0) == 1)
        {
            return;
        }

        var token = _cycleToken;
        _cycleTask = Task.Run(async () =>
        {
            try
            {
                await CycleAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await StopWithAsync(new SyncError("sync_error", ex.Message), CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _inCycle, 0);
            }
        });
    }

    private void Rearm()
    {
        try
        {
            _timer.Change(_config.PollInterval, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task<Result<SyncCycleReport>> CycleAsync(CancellationToken cancellationToken)
    {
        var started = _time.GetUtcNow();
        var batch = await CommitPendingBatchAsync(cancellationToken).ConfigureAwait(false);
        if (batch.Error is { } batchError)
        {
            return await StopWithAsync(batchError, cancellationToken).ConfigureAwait(false);
        }

        var clean = await _local.IsWorkingTreeEmpty(_config.WatchedFolder, cancellationToken).ConfigureAwait(false);
        if (clean is not Result<bool>.Ok probe)
        {
            return await StopWithAsync(((Result<bool>.Fail)clean).Error, cancellationToken).ConfigureAwait(false);
        }

        if (!probe.Value)
        {
            return await StopWithAsync(DirtyTree, cancellationToken).ConfigureAwait(false);
        }

        var pulled = await PullAsync(cancellationToken).ConfigureAwait(false);
        if (pulled is Result<PullStage>.Fail pullFailure)
        {
            return await StopWithAsync(pullFailure.Error, cancellationToken).ConfigureAwait(false);
        }

        var stage = ((Result<PullStage>.Ok)pulled).Value;
        switch (stage)
        {
            case PullStage.Conflicted conflicted:
                return await StopConflictedAsync(conflicted.ConflictedPaths, cancellationToken).ConfigureAwait(false);
            case PullStage.FastForward forward:
                await RecordAsync(new SyncEventDraft(
                    SyncEventKind.CommitIn, _config.Branch, $"{forward.Outcome.FromSha}..{forward.Outcome.ToSha}", []),
                    cancellationToken).ConfigureAwait(false);
                break;
            case PullStage.AutoMerged merged:
                await RecordAsync(new SyncEventDraft(
                    SyncEventKind.AutoMerged, _config.Branch, $"merge commit {merged.Outcome.ToSha}", merged.Files),
                    cancellationToken).ConfigureAwait(false);
                _bus.Publish(new SyncNotification.AutoMerged(_time.GetUtcNow(), _config.Branch, merged.Files));
                break;
        }

        SyncOutcome? pushed = null;
        if (batch.CommitSha is not null || stage is PullStage.AutoMerged)
        {
            var pushedResult = await _remote.Push(_config.Branch, cancellationToken).ConfigureAwait(false);
            if (pushedResult is not Result<SyncOutcome>.Ok pushOk)
            {
                return await StopWithAsync(((Result<SyncOutcome>.Fail)pushedResult).Error, cancellationToken).ConfigureAwait(false);
            }

            pushed = pushOk.Value;
            await RecordAsync(
                new SyncEventDraft(SyncEventKind.CommitOut, _config.Branch, $"pushed {pushed.ToSha}", []),
                cancellationToken).ConfigureAwait(false);
        }

        var report = new SyncCycleReport(started, stage switch
        {
            PullStage.FastForward forward => forward.Outcome,
            PullStage.AutoMerged merged => merged.Outcome,
            _ => null
        }, pushed);

        if (report.Pulled is not null || pushed is not null)
        {
            _bus.Publish(new SyncNotification.SyncCompleted(_time.GetUtcNow(), report));
        }

        lock (_statusGate)
        {
            _lastCycleUtc = started;
            _lastError = null;
        }

        return new Result<SyncCycleReport>.Ok(report);
    }

    private async Task<BatchStage> CommitPendingBatchAsync(CancellationToken cancellationToken)
    {
        List<string> batch = [];
        while (_pending.TryDequeue(out var relativePath))
        {
            batch.Add(relativePath);
        }

        if (batch.Count == 0)
        {
            return new BatchStage(null, null);
        }

        var committed = await _committer.CommitBatch(batch, cancellationToken).ConfigureAwait(false);
        return committed switch
        {
            Result<CommitOutcome>.Ok outcome => new BatchStage(outcome.Value.CommitSha, null),
            Result<CommitOutcome>.Fail failure => new BatchStage(null, failure.Error),
            _ => throw new InvalidOperationException("batch outcome is neither ok nor fail")
        };
    }

    private async Task<Result<PullStage>> PullAsync(CancellationToken cancellationToken)
    {
        var pulled = await _remote.Pull(_config.Branch, cancellationToken).ConfigureAwait(false);
        if (pulled is Result<SyncOutcome>.Ok fastForward)
        {
            return fastForward.Value.FromSha == fastForward.Value.ToSha
                ? new Result<PullStage>.Ok(new PullStage.Unchanged())
                : new Result<PullStage>.Ok(new PullStage.FastForward(fastForward.Value));
        }

        var error = ((Result<SyncOutcome>.Fail)pulled).Error;
        if (error.Code != ConflictCode)
        {
            return new Result<PullStage>.Fail(error);
        }

        var merged = await _remote.MergeFetchedHead(cancellationToken).ConfigureAwait(false);
        if (merged is Result<MergeOutcome>.Fail mergeFailure)
        {
            return new Result<PullStage>.Fail(mergeFailure.Error);
        }

        return ((Result<MergeOutcome>.Ok)merged).Value switch
        {
            MergeOutcome.Conflicted conflicted =>
                new Result<PullStage>.Ok(new PullStage.Conflicted(conflicted.ConflictedPaths)),
            MergeOutcome.Merged done => done.FromSha == done.ToSha
                ? new Result<PullStage>.Ok(new PullStage.Unchanged())
                : new Result<PullStage>.Ok(new PullStage.AutoMerged(
                    new SyncOutcome(_config.Branch, done.FromSha, done.ToSha), done.Files)),
            _ => throw new InvalidOperationException("merge outcome is neither merged nor conflicted")
        };
    }

    private async Task<Result<SyncCycleReport>> StopWithAsync(SyncError error, CancellationToken cancellationToken)
    {
        await RecordAsync(
            new SyncEventDraft(SyncEventKind.SyncFailed, _config.Branch, error.Message, []),
            cancellationToken).ConfigureAwait(false);
        _bus.Publish(new SyncNotification.SyncFailed(_time.GetUtcNow(), error));
        return Fail(error);
    }

    private async Task<Result<SyncCycleReport>> StopConflictedAsync(
        IReadOnlyList<string> conflictedPaths, CancellationToken cancellationToken)
    {
        await RecordAsync(new SyncEventDraft(
                SyncEventKind.MergeConflicted, _config.Branch, "merge aborted; tree left at pre-merge commit", conflictedPaths),
            cancellationToken).ConfigureAwait(false);
        _bus.Publish(new SyncNotification.MergeConflicted(_time.GetUtcNow(), _config.Branch, conflictedPaths));
        return Fail(SyncError.Conflict($"merge conflicted over {string.Join(", ", conflictedPaths)}"));
    }

    private Result<SyncCycleReport> Fail(SyncError error)
    {
        lock (_statusGate)
        {
            _lastCycleUtc = _time.GetUtcNow();
            _lastError = error;
        }

        return new Result<SyncCycleReport>.Fail(error);
    }

    private Task RecordAsync(SyncEventDraft draft, CancellationToken cancellationToken) =>
        _history.Append(draft, cancellationToken);

    private sealed record BatchStage(string? CommitSha, SyncError? Error);

    private abstract record PullStage
    {
        public sealed record Unchanged : PullStage;

        public sealed record FastForward(SyncOutcome Outcome) : PullStage;

        public sealed record AutoMerged(SyncOutcome Outcome, IReadOnlyList<string> Files) : PullStage;

        public sealed record Conflicted(IReadOnlyList<string> ConflictedPaths) : PullStage;
    }
}
