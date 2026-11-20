using Syncrowatch.Core.Engine;

namespace Syncrowatch.Tests;

public sealed class FakeWatching : IFileWatching
{
    private bool _raising;

    public int StartCount { get; private set; }

    public int SuspendCount { get; private set; }

    public int ResumeCount { get; private set; }

    public int DisposeCount { get; private set; }

    public event Action<FolderChange>? ChangeDetected;

    public event Action<WatcherFailure>? Failed;

    public void Start(CancellationToken cancellationToken = default)
    {
        StartCount++;
        _raising = true;
    }

    public void Suspend()
    {
        SuspendCount++;
        _raising = false;
    }

    public void Resume()
    {
        ResumeCount++;
        _raising = true;
    }

    public void Dispose() => DisposeCount++;

    public void Emit(string relativePath)
    {
        if (_raising)
        {
            ChangeDetected?.Invoke(new FolderChange(relativePath, WatchChangeKind.Changed));
        }
    }

    public void Break(string message) => Failed?.Invoke(new WatcherFailure($"watcher error: {message}", null));
}
