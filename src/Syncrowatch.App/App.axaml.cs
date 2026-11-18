using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Syncrowatch.App.Tray;
using Syncrowatch.App.Wizard;

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
            if (LaunchSettingsWindow(desktop))
            {
                return;
            }

            var icons = TrayIcon.GetIcons(this);
            if (icons is { Count: > 0 })
            {
                _tray = new TrayController(CompositionRoot.Build(), icons[0], desktop);
                _ = _tray.LaunchAsync();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static bool LaunchSettingsWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.Args is not { Length: > 0 } args || !args.Contains("--settings"))
        {
            return false;
        }

        var wizard = CompositionRoot.Build().GetRequiredService<SetupWizardWindow>();
        wizard.Closed += (_, _) => desktop.Shutdown();
        wizard.Show();
        return true;
    }
}
