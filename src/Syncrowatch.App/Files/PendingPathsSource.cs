using Syncrowatch.Core.Engine;

namespace Syncrowatch.App.Files;

public sealed class PendingPathsSource : IPendingPathsSource
{
    private volatile ChangeBatcher? _batcher;

    public IReadOnlyCollection<string> PendingPaths => _batcher?.PendingPaths ?? [];

    public void Capture(ChangeBatcher batcher) => _batcher = batcher;
}
