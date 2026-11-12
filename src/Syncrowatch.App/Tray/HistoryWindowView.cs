using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Syncrowatch.App.History;

namespace Syncrowatch.App.Tray;

public sealed class HistoryWindowView(IServiceProvider services) : IHistoryView
{
    private readonly IServiceProvider _services = services;
    private HistoryWindow? _window;

    public void Show()
    {
        if (_window is { } existing)
        {
            existing.Show();
            return;
        }

        _window = new HistoryWindow(_services);
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
