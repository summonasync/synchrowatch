using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Syncrowatch.App.Settings;

public sealed class SettingsWindowView(IServiceProvider services) : ISettingsSurface
{
    private readonly IServiceProvider _services = services;
    private SettingsWindow? _window;

    public void Show()
    {
        if (_window is { } existing)
        {
            existing.Show();
            return;
        }

        _window = new SettingsWindow(_services);
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
