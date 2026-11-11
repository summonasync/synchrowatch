namespace Syncrowatch.Core.Engine;

public sealed class FolderWatcher : IDisposable
{
    private readonly string _root;
    private readonly FileSystemWatcher _watcher;
    private CancellationTokenRegistration _registration;
    private int _disposed;

    public event Action<FolderChange>? ChangeDetected;

    public event Action<WatcherFailure>? Failed;

    public FolderWatcher(string root)
    {
        _root = Path.GetFullPath(root);
        _watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Created += (_, args) => Raise(args.FullPath, WatchChangeKind.Created);
        _watcher.Changed += (_, args) => Raise(args.FullPath, WatchChangeKind.Changed);
        _watcher.Deleted += (_, args) => Raise(args.FullPath, WatchChangeKind.Deleted);
        _watcher.Renamed += (_, args) =>
        {
            Raise(args.OldFullPath, WatchChangeKind.Deleted);
            Raise(args.FullPath, WatchChangeKind.Created);
        };
        _watcher.Error += (_, args) =>
        {
            var failure = new WatcherFailure($"watcher error over '{_root}': {args.GetException().Message}", args.GetException());
            Failed?.Invoke(failure);
        };
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        _watcher.EnableRaisingEvents = true;
        _registration = cancellationToken.Register(Dispose);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _registration.Dispose();
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }

    private void Raise(string fullPath, WatchChangeKind kind)
    {
        var relative = Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
        if (relative is "" or "." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            Failed?.Invoke(new WatcherFailure($"watch event for '{fullPath}' escapes the watched folder", null));
            return;
        }

        var change = new FolderChange(relative, kind);
        try
        {
            ChangeDetected?.Invoke(change);
        }
        catch (Exception ex)
        {
            Failed?.Invoke(new WatcherFailure($"change handler failed for '{relative}': {ex.Message}", ex));
        }
    }
}
