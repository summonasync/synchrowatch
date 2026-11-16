using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Synchro.Core;
using Syncrowatch.App.History;
using Syncrowatch.App.Notifications;
using Syncrowatch.App.Wizard;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Transport;

namespace Syncrowatch.App.Files;

public sealed class FilesViewModel : INotifyPropertyChanged, IDisposable
{
    private const int TimelineLimit = 50;
    private const int SyncedShaLookupLimit = 10;
    private const string PushedDetailPrefix = "pushed ";

    private readonly ISyncConfigStore _configStore;
    private readonly ILocalGit _git;
    private readonly ISyncEventStore _events;
    private readonly ISyncEventBus _bus;
    private readonly IPendingPathsSource _pendingPaths;
    private readonly Func<SyncConfig, IRemoteRepository> _remoteFactory;
    private readonly ToastService _toasts;
    private readonly TimeProvider _time;
    private readonly Func<string, Task> _clipboardWriter;
    private readonly Action<Action> _uiPost;
    private readonly object _gate = new();

    private IReadOnlyList<FileEntryViewModel> _files = [];
    private IReadOnlyList<FileEntryViewModel> _visibleFiles = [];
    private FileEntryViewModel? _selectedFile;
    private IReadOnlyList<CommitEntryViewModel> _timeline = [];
    private string _filterText = string.Empty;
    private string? _errorMessage;
    private string? _linkHint;
    private bool _pushPending;
    private string? _browserBase;
    private string _watchedFolder = string.Empty;
    private string _headSha = string.Empty;
    private string _syncedSha = string.Empty;
    private int _disposed;

    private static readonly IReadOnlySet<string> EmptyPaths = new HashSet<string>();

    public FilesViewModel(
        ISyncConfigStore configStore,
        ILocalGit git,
        ISyncEventStore events,
        ISyncEventBus bus,
        IPendingPathsSource pendingPaths,
        Func<SyncConfig, IRemoteRepository> remoteFactory,
        ToastService toasts,
        TimeProvider? timeProvider = null,
        Func<string, Task>? clipboardWriter = null,
        Action<Action>? uiPost = null)
    {
        _configStore = configStore;
        _git = git;
        _events = events;
        _bus = bus;
        _pendingPaths = pendingPaths;
        _remoteFactory = remoteFactory;
        _toasts = toasts;
        _time = timeProvider ?? TimeProvider.System;
        _clipboardWriter = clipboardWriter ?? (_ => Task.CompletedTask);
        _uiPost = uiPost ?? (action => action());
        CopyLinkCommand = new AsyncCommand(CopyLinkAsync, () => CanCopyLink, HandleCommandFailure);
        _bus.Received += OnNotification;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncCommand CopyLinkCommand { get; }

    public IReadOnlyList<FileEntryViewModel> VisibleFiles
    {
        get => _visibleFiles;
        private set => SetField(ref _visibleFiles, value);
    }

    public FileEntryViewModel? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (!SetField(ref _selectedFile, value))
            {
                return;
            }

            Notify(nameof(HasSelection));
            _ = LoadTimelineAsync();
        }
    }

    public bool HasSelection => _selectedFile is not null;

    public IReadOnlyList<CommitEntryViewModel> Timeline
    {
        get => _timeline;
        private set => SetField(ref _timeline, value);
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetField(ref _filterText, value))
            {
                return;
            }

            ApplyFilter();
        }
    }

    public bool CanCopyLink => _browserBase is not null && _headSha.Length > 0;

    public string? LinkHint
    {
        get => _linkHint;
        private set
        {
            if (SetField(ref _linkHint, value))
            {
                Notify(nameof(HasLinkHint));
            }
        }
    }

    public bool HasLinkHint => !string.IsNullOrWhiteSpace(_linkHint);

    public bool PushPending
    {
        get => _pushPending;
        private set => SetField(ref _pushPending, value);
    }

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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _bus.Received -= OnNotification;
    }

    private async Task LoadAsync()
    {
        var loaded = await _configStore.Load(CancellationToken.None).ConfigureAwait(true);
        if (loaded is not Result<SyncConfig>.Ok ready)
        {
            ErrorMessage = RemoteUrlRedactor.Scrub(((Result<SyncConfig>.Fail)loaded).Error.Message);
            return;
        }

        var config = ready.Value;
        _watchedFolder = config.WatchedFolder;
        _browserBase = GitHubRemoteLink.TryParseBrowserBase(config.RemoteUrl);

        var listing = await _git.ListTrackedFiles(_watchedFolder, CancellationToken.None).ConfigureAwait(true);
        if (listing is not Result<IReadOnlyList<string>>.Ok files)
        {
            ErrorMessage = RemoteUrlRedactor.Scrub(((Result<IReadOnlyList<string>>.Fail)listing).Error.Message);
            return;
        }

        var head = await _git.HeadInfo(_watchedFolder, CancellationToken.None).ConfigureAwait(true);
        _headSha = head is Result<LocalHead>.Ok headOk ? headOk.Value.Sha : string.Empty;
        _syncedSha = await ResolveSyncedShaAsync(config).ConfigureAwait(true);

        var unpushed = await LoadUnpushedPathsAsync().ConfigureAwait(true);
        var pending = _pendingPaths.PendingPaths;
        RebuildFiles(files.Value, pending, unpushed);
        PushPending = _headSha.Length > 0 && _headSha != _syncedSha;
        RefreshLinkHint();
        ErrorMessage = null;
        CopyLinkCommand.RaiseCanExecuteChanged();
    }

    private async Task<string> ResolveSyncedShaAsync(SyncConfig config)
    {
        var query = new HistoryQuery { Limit = SyncedShaLookupLimit, Kinds = [SyncEventKind.CommitOut] };
        var pushed = await _events.Query(query, CancellationToken.None).ConfigureAwait(true);
        if (pushed is Result<IReadOnlyList<SyncEvent>>.Ok records)
        {
            var synced = records.Value
                .Select(ParsePushedSha)
                .FirstOrDefault(sha => sha.Length > 0) ?? string.Empty;
            if (synced.Length > 0)
            {
                return synced;
            }
        }

        var tip = await _remoteFactory(config).TrackingTip(config.Branch, CancellationToken.None)
            .ConfigureAwait(true);
        return tip is Result<string>.Ok value ? value.Value : string.Empty;
    }

    private async Task<IReadOnlySet<string>> LoadUnpushedPathsAsync()
    {
        if (_syncedSha.Length == 0 || _headSha.Length == 0 || _syncedSha == _headSha)
        {
            return EmptyPaths;
        }

        var changed = await _git.ChangedPaths(_watchedFolder, _syncedSha, _headSha, CancellationToken.None)
            .ConfigureAwait(true);
        return changed is Result<IReadOnlyList<string>>.Ok paths
            ? paths.Value.ToHashSet(StringComparer.Ordinal)
            : throw new InvalidOperationException(((Result<IReadOnlyList<string>>.Fail)changed).Error.Message);
    }

    private void RebuildFiles(
        IReadOnlyList<string> paths,
        IReadOnlyCollection<string> pending,
        IReadOnlySet<string> unpushed)
    {
        _files = paths
            .Select(path => new FileEntryViewModel(path, GlanceFor(path, pending, unpushed)))
            .ToList();
        ApplyFilter();

        var previous = _selectedFile?.Path;
        _selectedFile = previous is null
            ? null
            : _files.FirstOrDefault(entry => entry.Path == previous);
        Notify(nameof(SelectedFile));
        Notify(nameof(HasSelection));
    }

    private void ApplyFilter()
    {
        VisibleFiles = string.IsNullOrWhiteSpace(_filterText)
            ? _files
            : _files.Where(entry => entry.Path.Contains(_filterText, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void RefreshLinkHint() =>
        LinkHint = _browserBase is null
            ? "copy link needs a github.com https remote"
            : _headSha.Length == 0
                ? "no local commits to pin yet"
                : null;

    private async Task LoadTimelineAsync()
    {
        if (_selectedFile is not { } file || _watchedFolder.Length == 0)
        {
            Timeline = [];
            return;
        }

        try
        {
            var log = await _git.LogPath(_watchedFolder, file.Path, TimelineLimit, 0, CancellationToken.None)
                .ConfigureAwait(true);
            if (log is not Result<IReadOnlyList<GitLogEntry>>.Ok entries)
            {
                ErrorMessage = RemoteUrlRedactor.Scrub(((Result<IReadOnlyList<GitLogEntry>>.Fail)log).Error.Message);
                return;
            }

            var now = _time.GetUtcNow();
            Timeline = entries.Value.Select(record => new CommitEntryViewModel(record, now)).ToList();
            ErrorMessage = null;
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    private async Task RefreshRowsAsync(IReadOnlyList<string> paths)
    {
        if (_files.Count == 0 || _watchedFolder.Length == 0)
        {
            return;
        }

        try
        {
            var head = await _git.HeadInfo(_watchedFolder, CancellationToken.None).ConfigureAwait(true);
            if (head is Result<LocalHead>.Ok headOk)
            {
                _headSha = headOk.Value.Sha;
            }

            var unpushed = await LoadUnpushedPathsAsync().ConfigureAwait(true);
            var pending = _pendingPaths.PendingPaths;
            var affected = paths.ToHashSet(StringComparer.Ordinal);
            foreach (var entry in _files.Where(entry => affected.Contains(entry.Path)))
            {
                entry.StatusText = GlanceFor(entry.Path, pending, unpushed);
            }

            PushPending = _headSha.Length > 0 && _headSha != _syncedSha;
            RefreshLinkHint();
            CopyLinkCommand.RaiseCanExecuteChanged();
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    private async Task CopyLinkAsync()
    {
        if (_selectedFile is not { } file || !CanCopyLink)
        {
            return;
        }

        try
        {
            var link = BuildShareLink(file.Path);
            await _clipboardWriter(link).ConfigureAwait(true);
            _toasts.Show(_pushPending
                ? new ToastContent(
                    ToastKind.LinkPending,
                    "Link copied — not pushed yet",
                    $"{file.Path} may show a 404 until the push lands")
                : new ToastContent(ToastKind.LinkCopied, "Link copied", file.Path));
        }
        catch (Exception exception)
        {
            ErrorMessage = $"link copy failed: {RemoteUrlRedactor.Scrub(exception.Message)}";
        }
    }

    private string BuildShareLink(string relativePath)
    {
        var segments = relativePath.Replace('\\', '/').Split('/');
        var encoded = string.Join('/', segments.Select(Uri.EscapeDataString));
        return GitHubRemoteLink.BuildLink(_browserBase!, _headSha, encoded);
    }

    private void OnNotification(SyncNotification notification)
    {
        _uiPost(() =>
        {
            switch (notification)
            {
                case SyncNotification.SyncCompleted:
                    _ = RefreshAsync();
                    break;
                case SyncNotification.PushPending:
                    PushPending = true;
                    break;
                case SyncNotification.AutoMerged merged:
                    _ = RefreshRowsAsync(merged.Files);
                    break;
                case SyncNotification.MergeConflicted conflicted:
                    _ = RefreshRowsAsync(conflicted.ConflictedPaths);
                    break;
                case SyncNotification.ChangesDetected detected:
                    _ = RefreshRowsAsync(detected.Paths);
                    break;
            }
        });
    }

    private static string GlanceFor(string path, IReadOnlyCollection<string> pending, IReadOnlySet<string> unpushed) =>
        pending.Contains(path) ? "pending batch"
            : unpushed.Contains(path) ? "unpushed"
            : "clean";

    private static string ParsePushedSha(SyncEvent @event) =>
        @event.Detail.StartsWith(PushedDetailPrefix, StringComparison.Ordinal)
            ? @event.Detail[PushedDetailPrefix.Length..].Trim()
            : string.Empty;

    private void HandleCommandFailure(Exception exception) =>
        ErrorMessage = $"link copy failed: {RemoteUrlRedactor.Scrub(exception.Message)}";

    private void ReportFailure(Exception exception) =>
        ErrorMessage = $"files could not be read: {RemoteUrlRedactor.Scrub(exception.Message)}";

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
