namespace Syncrowatch.Core.Engine;

public interface IPendingPathsSource
{
    IReadOnlyCollection<string> PendingPaths { get; }

    void Capture(ChangeBatcher batcher);
}
