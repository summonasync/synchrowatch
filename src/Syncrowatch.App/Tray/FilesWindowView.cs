using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Syncrowatch.App.Files;

namespace Syncrowatch.App.Tray;

public sealed class FilesWindowView(IServiceProvider services) : IFilesView
{
    private readonly IServiceProvider _services = services;
    private FilesWindow? _window;

    public void Show()
    {
        if (_window is { } existing)
        {
            existing.Show();
            return;
        }

        _window = new FilesWindow(_services);
        _window.Closed += OnWindowClosed;
        _window.Show();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Closed -= OnWindowClosed;
        }

        _window = null;
    }
}
