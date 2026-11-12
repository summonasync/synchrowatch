namespace Syncrowatch.Core.Engine;

public interface IFileWatching : IDisposable
{
    void Start(CancellationToken cancellationToken = default);

    void Suspend();

    void Resume();

    event Action<FolderChange>? ChangeDetected;

    event Action<WatcherFailure>? Failed;
}
