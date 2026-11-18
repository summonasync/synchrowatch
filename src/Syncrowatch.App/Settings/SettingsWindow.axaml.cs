using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Syncrowatch.Core.Config;

namespace Syncrowatch.App.Settings;

public partial class SettingsWindow : Window
{
    private readonly ISettingsRouter? _router;
    private readonly SettingsViewModel? _viewModel;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(IServiceProvider services) : this()
    {
        _router = services.GetRequiredService<ISettingsRouter>();
        _viewModel = new SettingsViewModel(
            services.GetRequiredService<ISyncConfigStore>(),
            services.GetRequiredService<ICredentialStore>());
        DataContext = _viewModel;
        Opened += (_, _) => _ = _viewModel.ReloadAsync();
        _viewModel.RunWizardRequested += OnRunWizardRequested;
        _router.WizardClosed += OnWizardClosed;
        Closed += OnWindowClosed;
    }

    private void OnRunWizardRequested(object? sender, EventArgs e) => _router?.OpenWizard();

    private void OnWizardClosed(object? sender, EventArgs e) => _ = _viewModel?.ReloadAsync();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_router is not null)
        {
            _router.WizardClosed -= OnWizardClosed;
        }
    }
}
