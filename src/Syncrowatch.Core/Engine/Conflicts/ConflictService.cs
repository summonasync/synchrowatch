using System.Threading.Channels;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Transport;

namespace Syncrowatch.Core.Engine.Conflicts;

public sealed class ConflictService : IConflictService, IDisposable
{
    private readonly SyncConfig _config;
    private readonly ConflictDetector _detector;
    private readonly ResolutionExecutor _executor;
    private readonly IConflictStore _store;
    private readonly ISyncEventStore? _history;
    private readonly ISyncEventBus _bus;
    private readonly TimeProvider _time;
    private readonly Channel<string> _arrivals = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly Task _drain;

    public ConflictService(
        SyncConfig config,
        ILocalGit local,
        IConflictStore store,
        ISyncEventBus bus,
        ISyncEventStore? history = null,
        TimeProvider? timeProvider = null)
    {
        _config = config;
        _store = store;
        _history = history;
        _bus = bus;
        _time = timeProvider ?? TimeProvider.System;
        _detector = new ConflictDetector(config, local, store, timeProvider);
        _executor = new ResolutionExecutor(config, local, store, timeProvider);
        bus.Received += OnNotification;
        _drain = DrainAsync();
    }

    public async Task<Result<IReadOnlyList<PendingConflict>>> Pending(CancellationToken cancellationToken)
    {
        var pending = await _store.Pending().ConfigureAwait(false);
        if (pending is Result<IReadOnlyList<ConflictRecord>>.Fail failure)
        {
            return new Result<IReadOnlyList<PendingConflict>>.Fail(failure.Error);
        }

        var conflicts = ((Result<IReadOnlyList<ConflictRecord>>.Ok)pending).Value
            .Select(record => new PendingConflict(record.Path, record.Kind, record.CopyPath, record.DetectedUtc))
            .ToList();
        return new Result<IReadOnlyList<PendingConflict>>.Ok(conflicts);
    }

    public async Task<Result<ConflictResolution>> Resolve(
        string relativePath, ConflictOption option, CancellationToken cancellationToken)
    {
        var resolved = await _executor.Resolve(relativePath, option, cancellationToken).ConfigureAwait(false);
        if (resolved is Result<ConflictResolution>.Fail executorFailure
            || ((Result<ConflictResolution>.Ok)resolved).Value.RemainingPaths.Count == 0)
        {
            return resolved;
        }

        var detected = await _detector
            .HandleConflicted(((Result<ConflictResolution>.Ok)resolved).Value.RemainingPaths, cancellationToken)
            .ConfigureAwait(false);
        return detected is Result<IReadOnlyList<ConflictItem>>.Fail detectionFailure
            ? new Result<ConflictResolution>.Fail(detectionFailure.Error)
            : resolved;
    }

    public void Dispose()
    {
        _bus.Received -= OnNotification;
        _arrivals.Writer.TryComplete();
        _executor.Dispose();
    }

    private void OnNotification(SyncNotification? notification)
    {
        if (notification is SyncNotification.MergeConflicted conflicted)
        {
            foreach (var path in conflicted.ConflictedPaths)
            {
                _arrivals.Writer.TryWrite(path);
            }
        }
    }

    private async Task DrainAsync()
    {
        await foreach (var path in _arrivals.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            Result<IReadOnlyList<ConflictItem>> handled;
            try
            {
                handled = await _detector.HandleConflicted([path], CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await ReportDetectionFailureAsync(
                    path, new SyncError("conflict_detection_failed", ex.Message), CancellationToken.None)
                    .ConfigureAwait(false);
                continue;
            }

            if (handled is Result<IReadOnlyList<ConflictItem>>.Fail detectionFailure)
            {
                await ReportDetectionFailureAsync(path, detectionFailure.Error, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task ReportDetectionFailureAsync(string path, SyncError error, CancellationToken cancellationToken)
    {
        var detailed = new SyncError(
            "conflict_detection_failed",
            $"conflict detection failed for '{path}' ({error.Code}): {error.Message}");
        if (_history is { } history)
        {
            await history.Append(
                new SyncEventDraft(SyncEventKind.SyncFailed, _config.Branch, detailed.Message, [path]), cancellationToken)
                .ConfigureAwait(false);
        }

        _bus.Publish(new SyncNotification.SyncFailed(_time.GetUtcNow(), detailed));
    }
}
