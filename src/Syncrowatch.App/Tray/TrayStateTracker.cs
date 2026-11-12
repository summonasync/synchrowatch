using System.ComponentModel;
using System.Runtime.CompilerServices;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.History;

namespace Syncrowatch.App.Tray;

public sealed class TrayStateTracker : INotifyPropertyChanged, IDisposable
{
    private readonly ISyncEventBus _bus;
    private readonly object _gate = new();
    private TrayState _state = TrayState.Idle;
    private bool _paused;
    private bool _pushPending;
    private int _disposed;

    public TrayStateTracker(ISyncEventBus bus)
    {
        _bus = bus;
        _bus.Received += OnNotification;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TrayState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool PushPending
    {
        get
        {
            lock (_gate)
            {
                return _pushPending;
            }
        }
    }

    public string ToolTipText
    {
        get
        {
            lock (_gate)
            {
                var label = _state switch
                {
                    TrayState.Syncing => "syncing",
                    TrayState.Conflict => "merge conflict",
                    TrayState.Error => "error",
                    _ => "idle"
                };

                List<string> notes = [];
                if (_paused)
                {
                    notes.Add("paused");
                }

                if (_pushPending)
                {
                    notes.Add("push pending confirmation");
                }

                return notes.Count == 0
                    ? $"Syncrowatch — {label}"
                    : $"Syncrowatch — {label}, {string.Join(", ", notes)}";
            }
        }
    }

    public void BeginSync()
    {
        bool changed;
        lock (_gate)
        {
            changed = _state != TrayState.Syncing;
            _state = TrayState.Syncing;
        }

        if (changed)
        {
            Notify(nameof(State));
        }

        Notify(nameof(ToolTipText));
    }

    public void SettleSync(Result<SyncCycleReport> outcome)
    {
        var settled = outcome switch
        {
            Result<SyncCycleReport>.Ok => TrayState.Idle,
            Result<SyncCycleReport>.Fail failure when failure.Error.Code == "conflict" => TrayState.Conflict,
            _ => TrayState.Error
        };

        bool changed;
        lock (_gate)
        {
            changed = _state != settled;
            _state = settled;
        }

        if (changed)
        {
            Notify(nameof(State));
            Notify(nameof(ToolTipText));
        }
    }

    public void SetPaused(bool paused)
    {
        bool changed;
        lock (_gate)
        {
            changed = _paused != paused;
            _paused = paused;
        }

        if (changed)
        {
            Notify(nameof(ToolTipText));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _bus.Received -= OnNotification;
    }

    private void OnNotification(SyncNotification notification)
    {
        switch (notification)
        {
            case SyncNotification.SyncCompleted:
                bool pendingCleared;
                lock (_gate)
                {
                    pendingCleared = _pushPending;
                    _pushPending = false;
                }

                if (pendingCleared)
                {
                    Notify(nameof(PushPending));
                }

                SetState(TrayState.Idle);
                break;
            case SyncNotification.AutoMerged:
                SetState(TrayState.Idle);
                break;
            case SyncNotification.MergeConflicted:
                SetState(TrayState.Conflict);
                break;
            case SyncNotification.SyncFailed:
                SetState(TrayState.Error);
                break;
            case SyncNotification.PushPending:
                bool newlyPending;
                lock (_gate)
                {
                    newlyPending = !_pushPending;
                    _pushPending = true;
                }

                if (newlyPending)
                {
                    Notify(nameof(PushPending));
                    Notify(nameof(ToolTipText));
                }

                break;
        }
    }

    public void SetState(TrayState state)
    {
        bool changed;
        lock (_gate)
        {
            changed = _state != state;
            _state = state;
        }

        if (changed)
        {
            Notify(nameof(ToolTipText));
        }
    }

    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
