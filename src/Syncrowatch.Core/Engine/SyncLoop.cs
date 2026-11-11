using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Transport;

namespace Syncrowatch.Core.Engine;

public sealed class SyncLoop : ISyncEngine, IDisposable
{
    private const string ConflictCode = "conflict";
    private const int MaxPendingPaths = 4096;

    private static readonly SyncError DirtyTree =
        new("dirty_tree", "watched folder carries uncommitted changes; sync cycle refused");

    private static readonly Regex RemoteUrlPattern = new(@"(?:https?|ssh)://[^\s'""']+", RegexOptions.Compiled);

    private readonly SyncConfig _config;
    private readonly IRemoteRepository _remote;
    private readonly ILocalGit _local;
    private readonly ISyncEventStore _history;
    private readonly ISyncEventBus _bus;
    private readonly BatchCommitter _committer;
    private readonly IPushConfirmation? _confirmation;
    private readonly ITimer _timer;
    private readonly TimeProvider _time;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly object _statusGate = new();
    private readonly object _pendingPushGate = new();
    private int _inCycle;
    private int _disposed;
    private bool _started;
    private int _paused;
    private int _mode;
    private PendingPush? _pendingPush;
    private DateTimeOffset? _lastCycleUtc;
    private SyncError? _lastError;
    private SyncEventDraft? _lastFailureEvent;
    private CancellationToken _cycleToken = CancellationToken.None;
    private CancellationTokenRegistration _registration;
    private Task _cycleTask = Task.CompletedTask;

    public SyncLoop(
        SyncConfig config,
        IRemoteRepository remote,
        ILocalGit local,
        ISyncEventStore history,
        ISyncEventBus bus,
        TimeProvider? timeProvider = null,
        IPushConfirmation? pushConfirmation = null)
    {
        _config = config;
        _remote = remote;
        _local = local;
        _history = history;
        _bus = bus;
        _time = timeProvider ?? TimeProvider.System;
        _confirmation = pushConfirmation;
        _mode = (int)config.Mode;
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
            if (_pending.Count >= MaxPendingPaths)
            {
                return;
            }

            _pending.Enqueue(relativePath);
        }
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        lock (_statusGate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _cycleToken = cancellationToken;
            _registration = cancellationToken.Register(Dispose);
        }

        if (ShouldPoll())
        {
            Rearm();
        }
    }

    public void SetMode(SyncRunMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        Volatile.Write(ref _mode, (int)mode);
        if (mode != SyncRunMode.SemiAuto)
        {
            lock (_pendingPushGate)
            {
                _pendingPush = null;
            }
        }

        if (ShouldPoll())
        {
            Rearm();
        }
        else if (mode == SyncRunMode.Manual)
        {
            Disarm();
        }
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        Volatile.Write(ref _paused, 1);
        Disarm();
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        Volatile.Write(ref _paused, 0);
        if (ShouldPoll())
        {
            Rearm();
        }
    }

    public async Task<Result<SyncOutcome>> ConfirmPendingPush(CancellationToken cancellationToken)
    {
        PendingPush? staged;
        lock (_pendingPushGate)
        {
            staged = _pendingPush;
            _pendingPush = null;
        }

        if (staged is null)
        {
            return new Result<SyncOutcome>.Fail(new SyncError("no_pending_push", "no push is waiting for confirmation"));
        }

        var pushed = await _remote.Push(staged.Branch, cancellationToken).ConfigureAwait(false);
        if (pushed is Result<SyncOutcome>.Fail failure)
        {
            lock (_pendingPushGate)
            {
                _pendingPush ??= staged;
            }

            return new Result<SyncOutcome>.Fail(failure.Error);
        }

        var outcome = ((Result<SyncOutcome>.Ok)pushed).Value;
        await RecordAsync(
            new SyncEventDraft(SyncEventKind.CommitOut, staged.Branch, $"pushed {outcome.ToSha}", []),
            cancellationToken).ConfigureAwait(false);
        _bus.Publish(new SyncNotification.SyncCompleted(
            _time.GetUtcNow(), new SyncCycleReport(_time.GetUtcNow(), null, outcome)));
        return new Result<SyncOutcome>.Ok(outcome);
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

        CancellationToken token;
        lock (_statusGate)
        {
            token = _cycleToken;
        }

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
                await FailCycleAsync(new SyncError("sync_error", ex.Message), CancellationToken.None).ConfigureAwait(false);
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
        var batchError = await CommitPendingBatchAsync(cancellationToken).ConfigureAwait(false);
        if (batchError is not null)
        {
            return await FailCycleAsync(batchError, cancellationToken).ConfigureAwait(false);
        }

        var clean = await _local.IsWorkingTreeEmpty(_config.WatchedFolder, cancellationToken).ConfigureAwait(false);
        if (clean is not Result<bool>.Ok probe)
        {
            return await FailCycleAsync(((Result<bool>.Fail)clean).Error, cancellationToken).ConfigureAwait(false);
        }

        if (!probe.Value)
        {
            return await FailCycleAsync(DirtyTree, cancellationToken).ConfigureAwait(false);
        }

        var pulled = await PullAsync(cancellationToken).ConfigureAwait(false);
        if (pulled is Result<PullStage>.Fail pullFailure)
        {
            return await FailCycleAsync(pullFailure.Error, cancellationToken).ConfigureAwait(false);
        }

        var stage = ((Result<PullStage>.Ok)pulled).Value;
        switch (stage)
        {
            case PullStage.Conflicted conflicted:
                return await FailConflictedAsync(conflicted.ConflictedPaths, cancellationToken).ConfigureAwait(false);
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

        var head = await _local.HeadInfo(_config.WatchedFolder, cancellationToken).ConfigureAwait(false);
        if (head is not Result<LocalHead>.Ok headOk)
        {
            return await FailCycleAsync(((Result<LocalHead>.Fail)head).Error, cancellationToken).ConfigureAwait(false);
        }

        var tracking = await _remote.TrackingTip(_config.Branch, cancellationToken).ConfigureAwait(false);
        if (tracking is not Result<string>.Ok tipOk)
        {
            return await FailCycleAsync(((Result<string>.Fail)tracking).Error, cancellationToken).ConfigureAwait(false);
        }

        var pulledOutcome = stage switch
        {
            PullStage.FastForward forward => forward.Outcome,
            PullStage.AutoMerged merged => merged.Outcome,
            _ => null
        };

        SyncOutcome? pushed = null;
        if (headOk.Value.Sha != tipOk.Value)
        {
            if ((SyncRunMode)Volatile.Read(ref _mode) == SyncRunMode.SemiAuto)
            {
                return StagePush(started, pulledOutcome, headOk.Value.Sha);
            }

            var pushedResult = await _remote.Push(_config.Branch, cancellationToken).ConfigureAwait(false);
            if (pushedResult is not Result<SyncOutcome>.Ok pushOk)
            {
                return await FailCycleAsync(((Result<SyncOutcome>.Fail)pushedResult).Error, cancellationToken).ConfigureAwait(false);
            }

            pushed = pushOk.Value;
            await RecordAsync(
                new SyncEventDraft(SyncEventKind.CommitOut, _config.Branch, $"pushed {pushed.ToSha}", []),
                cancellationToken).ConfigureAwait(false);
        }

        var report = new SyncCycleReport(started, pulledOutcome, pushed);

        if (report.Pulled is not null || pushed is not null)
        {
            _bus.Publish(new SyncNotification.SyncCompleted(_time.GetUtcNow(), report));
        }

        lock (_statusGate)
        {
            _lastCycleUtc = started;
            _lastError = null;
            _lastFailureEvent = null;
        }

        return new Result<SyncCycleReport>.Ok(report);
    }

    private async Task<SyncError?> CommitPendingBatchAsync(CancellationToken cancellationToken)
    {
        lock (_pendingPushGate)
        {
            if (_pendingPush is not null)
            {
                return null;
            }
        }

        List<string> batch = [];
        while (_pending.TryDequeue(out var relativePath))
        {
            batch.Add(relativePath);
        }

        if (batch.Count == 0)
        {
            return null;
        }

        var committed = await _committer.CommitBatch(batch, cancellationToken).ConfigureAwait(false);
        return committed switch
        {
            Result<CommitOutcome>.Ok => null,
            Result<CommitOutcome>.Fail failure => failure.Error,
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

    private Result<SyncCycleReport> StagePush(DateTimeOffset started, SyncOutcome? pulled, string headSha)
    {
        PendingPush staged;
        bool announce;
        lock (_pendingPushGate)
        {
            announce = _pendingPush is null || _pendingPush.Sha != headSha;
            staged = new PendingPush(_config.Branch, headSha, _time.GetUtcNow());
            _pendingPush = staged;
        }

        if (announce)
        {
            _bus.Publish(new SyncNotification.PushPending(staged));
        }

        if (_confirmation is not null)
        {
            _ = AskForConfirmationAsync(_confirmation, staged);
        }

        return new Result<SyncCycleReport>.Ok(new SyncCycleReport(started, pulled, null));
    }

    private async Task AskForConfirmationAsync(IPushConfirmation confirmation, PendingPush staged)
    {
        try
        {
            if (await confirmation.RequestAsync(staged, CancellationToken.None).ConfigureAwait(false))
            {
                await ConfirmPendingPush(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
        }
    }

    private bool ShouldPoll() =>
        _started
        && Volatile.Read(ref _paused) == 0
        && (SyncRunMode)Volatile.Read(ref _mode) != SyncRunMode.Manual;

    private void Disarm()
    {
        try
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task<Result<SyncCycleReport>> FailCycleAsync(SyncError error, CancellationToken cancellationToken)
    {
        var redacted = error with { Message = RedactRemote(error.Message) };
        await AnnounceFailure(
            new SyncEventDraft(SyncEventKind.SyncFailed, _config.Branch, redacted.Message, []),
            new SyncNotification.SyncFailed(_time.GetUtcNow(), redacted),
            cancellationToken).ConfigureAwait(false);
        return Fail(redacted);
    }

    private async Task<Result<SyncCycleReport>> FailConflictedAsync(
        IReadOnlyList<string> conflictedPaths, CancellationToken cancellationToken)
    {
        await AnnounceFailure(
            new SyncEventDraft(
                SyncEventKind.MergeConflicted, _config.Branch, "merge aborted; tree left at pre-merge commit", conflictedPaths),
            new SyncNotification.MergeConflicted(_time.GetUtcNow(), _config.Branch, conflictedPaths),
            cancellationToken).ConfigureAwait(false);
        return Fail(SyncError.Conflict($"merge conflicted over {string.Join(", ", conflictedPaths)}"));
    }

    private async Task AnnounceFailure(SyncEventDraft draft, SyncNotification notification, CancellationToken cancellationToken)
    {
        if (RepeatsLastFailure(draft))
        {
            return;
        }

        await _history.Append(draft, cancellationToken).ConfigureAwait(false);
        _bus.Publish(notification);
    }

    private bool RepeatsLastFailure(SyncEventDraft draft)
    {
        if (_lastFailureEvent is { } previous
            && previous.Kind == draft.Kind
            && previous.Detail == draft.Detail
            && previous.Paths.SequenceEqual(draft.Paths))
        {
            return true;
        }

        _lastFailureEvent = draft;
        return false;
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

    private static string RedactRemote(string message) =>
        RemoteUrlPattern.Replace(message, "[redacted-remote]");

    private abstract record PullStage
    {
        public sealed record Unchanged : PullStage;

        public sealed record FastForward(SyncOutcome Outcome) : PullStage;

        public sealed record AutoMerged(SyncOutcome Outcome, IReadOnlyList<string> Files) : PullStage;

        public sealed record Conflicted(IReadOnlyList<string> ConflictedPaths) : PullStage;
    }
}
