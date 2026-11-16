using System.ComponentModel;
using System.Runtime.CompilerServices;
using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.History;

namespace Syncrowatch.Core.Engine;

public sealed class ModeController : INotifyPropertyChanged, IDisposable
{
    private readonly ISyncEngine _engine;
    private readonly ISyncConfigStore _configStore;
    private readonly IFileWatching _watching;
    private readonly ISyncEventBus? _bus;
    private readonly TimeProvider _time;
    private readonly IPendingPathsSource? _pendingPaths;
    private readonly CancellationTokenSource _lifetime = new();
    private ChangeBatcher? _batcher;
    private SyncRunMode _mode = SyncRunMode.Auto;
    private bool _running;
    private bool _paused;
    private bool _configured;
    private string? _errorMessage;
    private int _disposed;

    public ModeController(
        ISyncEngine engine,
        ISyncConfigStore configStore,
        IFileWatching watching,
        ISyncEventBus? bus = null,
        TimeProvider? timeProvider = null,
        IPendingPathsSource? pendingPaths = null)
    {
        _engine = engine;
        _configStore = configStore;
        _watching = watching;
        _bus = bus;
        _time = timeProvider ?? TimeProvider.System;
        _pendingPaths = pendingPaths;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SyncRunMode Mode => _mode;

    public bool IsPaused => _paused;

    public bool IsConfigured => _configured;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                Notify(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

    public async Task LaunchAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        if (_running)
        {
            return;
        }

        var loaded = await _configStore.Load(cancellationToken).ConfigureAwait(false);
        if (loaded is not Result<SyncConfig>.Ok ready)
        {
            return;
        }

        var config = ready.Value;
        ApplyMode(config.Mode);
        var batcher = new ChangeBatcher(
            config.WatchedFolder,
            config.Debounce,
            SyncIgnoreRules.ForFolder(config.WatchedFolder),
            _time);
        _watching.ChangeDetected += batcher.Accept;
        batcher.BatchReady += _engine.AcceptBatch;
        batcher.BatchReady += AnnounceDetected;
        _batcher = batcher;
        _pendingPaths?.Capture(batcher);
        _watching.Start(_lifetime.Token);
        _engine.Start(_lifetime.Token);
        _configured = true;
        _running = true;
        ErrorMessage = null;
        NotifyAll();
    }

    public async Task SetModeAsync(SyncRunMode mode, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        if (!_running || mode == _mode)
        {
            return;
        }

        if (!await PersistModeAsync(mode, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        ApplyMode(mode);
    }

    public async Task<Result<SyncCycleReport>> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        _batcher?.FlushNow();
        return await _engine.SyncOnce(cancellationToken).ConfigureAwait(false);
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        if (!_running || _paused)
        {
            return;
        }

        _paused = true;
        _engine.Pause();
        _watching.Suspend();
        Notify(nameof(IsPaused));
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);
        if (!_running || !_paused)
        {
            return;
        }

        _paused = false;
        _engine.Resume();
        _watching.Resume();
        Notify(nameof(IsPaused));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (_batcher is not null)
        {
            _watching.ChangeDetected -= _batcher.Accept;
            _batcher.BatchReady -= _engine.AcceptBatch;
            _batcher.BatchReady -= AnnounceDetected;
            _batcher.Dispose();
        }

        _lifetime.Cancel();
        _lifetime.Dispose();
        _watching.Dispose();
        _engine.Dispose();
    }

    private void AnnounceDetected(IReadOnlyList<string> paths)
    {
        if (_mode != SyncRunMode.SemiAuto || _bus is null)
        {
            return;
        }

        _bus.Publish(new SyncNotification.ChangesDetected(_time.GetUtcNow(), paths));
    }

    private async Task<bool> PersistModeAsync(SyncRunMode mode, CancellationToken cancellationToken)
    {
        var loaded = await _configStore.Load(cancellationToken).ConfigureAwait(false);
        if (loaded is not Result<SyncConfig>.Ok current)
        {
            ErrorMessage = ((Result<SyncConfig>.Fail)loaded).Error.Message;
            return false;
        }

        var saved = await _configStore.Save(current.Value with { Mode = mode }, cancellationToken).ConfigureAwait(false);
        if (saved is Result<SyncConfig>.Fail failure)
        {
            ErrorMessage = failure.Error.Message;
            return false;
        }

        return true;
    }

    private void ApplyMode(SyncRunMode mode)
    {
        _mode = mode;
        _engine.SetMode(mode);
        Notify(nameof(Mode));
    }

    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Notify(name);
        return true;
    }
}
