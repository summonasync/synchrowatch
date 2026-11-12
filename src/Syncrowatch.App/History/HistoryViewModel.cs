using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Synchro.Core;
using Syncrowatch.App.Wizard;
using Syncrowatch.Core.History;

namespace Syncrowatch.App.History;

public sealed class HistoryViewModel : INotifyPropertyChanged
{
    private const int QueryLimit = 200;

    private readonly ISyncEventStore _store;
    private readonly IPendingConflictReader _conflicts;
    private readonly TimeProvider _time;
    private SyncEventKind? _filter;
    private IReadOnlyList<HistoryEntryViewModel> _entries = [];
    private IReadOnlyList<PendingConflictViewModel> _pending = [];
    private string? _errorMessage;

    public HistoryViewModel(
        ISyncEventStore store,
        IPendingConflictReader conflicts,
        TimeProvider? timeProvider = null)
    {
        _store = store;
        _conflicts = conflicts;
        _time = timeProvider ?? TimeProvider.System;
        RefreshCommand = new AsyncCommand(LoadAsync, () => true, ReportFailure);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncCommand RefreshCommand { get; }

    public IReadOnlyList<HistoryEntryViewModel> Entries
    {
        get => _entries;
        private set => SetField(ref _entries, value);
    }

    public IReadOnlyList<PendingConflictViewModel> PendingConflicts
    {
        get => _pending;
        private set
        {
            if (SetField(ref _pending, value))
            {
                Notify(nameof(HasPendingConflicts));
            }
        }
    }

    public bool HasPendingConflicts => _pending.Count > 0;

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

    public bool ShowAll
    {
        get => _filter is null;
        set => SelectFilter(null, value);
    }

    public bool ShowPulled
    {
        get => _filter == SyncEventKind.CommitIn;
        set => SelectFilter(SyncEventKind.CommitIn, value);
    }

    public bool ShowPushed
    {
        get => _filter == SyncEventKind.CommitOut;
        set => SelectFilter(SyncEventKind.CommitOut, value);
    }

    public bool ShowMerges
    {
        get => _filter == SyncEventKind.AutoMerged;
        set => SelectFilter(SyncEventKind.AutoMerged, value);
    }

    public bool ShowConflicts
    {
        get => _filter == SyncEventKind.MergeConflicted;
        set => SelectFilter(SyncEventKind.MergeConflicted, value);
    }

    public bool ShowFailures
    {
        get => _filter == SyncEventKind.SyncFailed;
        set => SelectFilter(SyncEventKind.SyncFailed, value);
    }

    public async Task RefreshAsync()
    {
        try
        {
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    private async Task LoadAsync()
    {
        var now = _time.GetUtcNow();
        var query = new HistoryQuery
        {
            Limit = QueryLimit,
            Kinds = _filter is { } kind ? [kind] : null
        };

        var events = await _store.Query(query, CancellationToken.None).ConfigureAwait(true);
        var pending = await _conflicts.Read(CancellationToken.None).ConfigureAwait(true);
        switch (events, pending)
        {
            case (Result<IReadOnlyList<SyncEvent>>.Ok timeline, Result<IReadOnlyList<PendingConflict>>.Ok unresolved):
                ErrorMessage = null;
                Entries = timeline.Value
                    .Select(@event => new HistoryEntryViewModel(@event, now))
                    .ToList();
                PendingConflicts = unresolved.Value
                    .Select(conflict => new PendingConflictViewModel(conflict, now))
                    .ToList();
                break;
            case (Result<IReadOnlyList<SyncEvent>>.Fail refusal, _):
                ErrorMessage = refusal.Error.Message;
                break;
            case (_, Result<IReadOnlyList<PendingConflict>>.Fail refusal):
                ErrorMessage = refusal.Error.Message;
                break;
        }
    }

    private void ReportFailure(Exception exception) =>
        ErrorMessage = $"history could not be read: {exception.Message}";

    private void SelectFilter(SyncEventKind? kind, bool selected)
    {
        if (!selected || _filter == kind)
        {
            return;
        }

        _filter = kind;
        NotifyFilterButtons();
        RefreshCommand.Execute(null);
    }

    private void NotifyFilterButtons()
    {
        foreach (var name in new[]
                 {
                     nameof(ShowAll),
                     nameof(ShowPulled),
                     nameof(ShowPushed),
                     nameof(ShowMerges),
                     nameof(ShowConflicts),
                     nameof(ShowFailures)
                 })
        {
            Notify(name);
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
