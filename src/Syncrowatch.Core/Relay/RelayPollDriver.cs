using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Transport;

namespace Syncrowatch.Core.Relay;

public sealed class RelayPollDriver : IDisposable
{
    private readonly ISyncConfigStore _configStore;
    private readonly Func<SyncConfig, IRelayWatcher> _watcherFactory;
    private readonly ISyncEventBus _bus;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private readonly object _gate = new();
    private int _polling;
    private int _disposed;
    private bool _started;

    public RelayPollDriver(
        ISyncConfigStore configStore,
        Func<SyncConfig, IRelayWatcher> watcherFactory,
        ISyncEventBus bus,
        TimeProvider? timeProvider = null)
    {
        _configStore = configStore;
        _watcherFactory = watcherFactory;
        _bus = bus;
        _time = timeProvider ?? TimeProvider.System;
        _timer = _time.CreateTimer(Tick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
        }

        Rearm();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _timer.Dispose();
    }

    private void Tick(object? state)
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        Rearm();
        if (Interlocked.CompareExchange(ref _polling, 1, 0) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await PollOnceAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _bus.Publish(new SyncNotification.SyncFailed(
                    _time.GetUtcNow(),
                    new SyncError("relay_poll_error", RemoteUrlRedactor.Scrub(ex.Message))));
            }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        });
    }

    private void Rearm()
    {
        try
        {
            _timer.Change(SyncConfig.DefaultPollInterval, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task PollOnceAsync()
    {
        var loaded = await _configStore.Load(CancellationToken.None).ConfigureAwait(false);
        if (loaded is not Result<SyncConfig>.Ok ready)
        {
            return;
        }

        var outcome = await _watcherFactory(ready.Value).Poll(CancellationToken.None).ConfigureAwait(false);
        if (outcome is Result<IReadOnlyList<RelayPackage>>.Fail failure)
        {
            var error = failure.Error with { Message = RemoteUrlRedactor.Scrub(failure.Error.Message) };
            _bus.Publish(new SyncNotification.SyncFailed(_time.GetUtcNow(), error));
        }
    }
}
