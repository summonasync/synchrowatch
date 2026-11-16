using Syncrowatch.App.Wizard;

namespace Syncrowatch.App.Tray;

public sealed class DetectionPromptViewModel
{
    private const int ShortPathLength = 40;

    public DetectionPromptViewModel(IReadOnlyList<string> paths)
    {
        Message = Describe(paths);
        SyncNowCommand = new AsyncCommand(() =>
        {
            SyncNowRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });
        LaterCommand = new AsyncCommand(() =>
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });
    }

    public string Message { get; }

    public AsyncCommand SyncNowCommand { get; }

    public AsyncCommand LaterCommand { get; }

    public event EventHandler? SyncNowRequested;

    public event EventHandler? Dismissed;

    private static string Describe(IReadOnlyList<string> paths) => paths.Count == 1
        ? $"\"{Shorten(paths[0])}\" changed locally. Sync it to the repository now?"
        : $"{paths.Count} files changed locally. Sync them to the repository now?";

    private static string Shorten(string path) => path.Length <= ShortPathLength
        ? path
        : "…" + path[^ShortPathLength..];
}
