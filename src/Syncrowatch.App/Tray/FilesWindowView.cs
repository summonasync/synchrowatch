namespace Syncrowatch.App.Tray;

public sealed class FilesWindowView(Func<IFilesWindow> createWindow) : IFilesView
{
    private readonly Func<IFilesWindow> _createWindow = createWindow;
    private IFilesWindow? _window;

    public void Show()
    {
        if (_window is { } existing)
        {
            existing.Show();
            return;
        }

        _window = _createWindow();
        _window.Closed += OnWindowClosed;
        _window.Show();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is IFilesWindow window)
        {
            window.Closed -= OnWindowClosed;
        }

        _window = null;
    }
}
