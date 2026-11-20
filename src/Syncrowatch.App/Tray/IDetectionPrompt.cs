namespace Syncrowatch.App.Tray;

public interface IDetectionPrompt
{
    Task<bool> RequestAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken);
}
