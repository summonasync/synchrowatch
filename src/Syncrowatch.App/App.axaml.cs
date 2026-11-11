using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Syncrowatch.App.Tray;

namespace Syncrowatch.App;

public class App : Application
{
    private TrayController? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var icons = TrayIcon.GetIcons(this);
            if (icons is { Count: > 0 })
            {
                _tray = new TrayController(CompositionRoot.Build(), icons[0], desktop);
                _ = _tray.LaunchAsync();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
