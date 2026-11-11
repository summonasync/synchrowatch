using System.ComponentModel;
using System.Runtime.CompilerServices;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.History;

namespace Syncrowatch.App.Tray;

public sealed class TrayStateTracker : INotifyPropertyChanged, IDisposable
{
    private readonly ISyncEventBus _bus;
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

    public TrayState State => _state;

    public bool PushPending => _pushPending;

    public string ToolTipText
    {
        get
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

    public void BeginSync()
    {
        if (SetField(ref _state, TrayState.Syncing))
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

        if (SetField(ref _state, settled))
        {
            Notify(nameof(State));
            Notify(nameof(ToolTipText));
        }
    }

    public void SetPaused(bool paused)
    {
        if (SetField(ref _paused, paused))
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
                if (_pushPending)
                {
                    _pushPending = false;
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
                if (!_pushPending)
                {
                    _pushPending = true;
                    Notify(nameof(PushPending));
                    Notify(nameof(ToolTipText));
                }

                break;
        }
    }

    private void SetState(TrayState state)
    {
        if (SetField(ref _state, state))
        {
            Notify(nameof(ToolTipText));
        }
    }

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
