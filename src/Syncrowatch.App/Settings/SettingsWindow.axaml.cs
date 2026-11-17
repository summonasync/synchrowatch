using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Syncrowatch.App.Wizard;
using Syncrowatch.Core.Config;

namespace Syncrowatch.App.Settings;

public partial class SettingsWindow : Window
{
    private readonly IServiceProvider? _services;
    private readonly SettingsViewModel? _viewModel;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(IServiceProvider services) : this()
    {
        _services = services;
        _viewModel = new SettingsViewModel(
            services.GetRequiredService<ISyncConfigStore>(),
            services.GetRequiredService<ICredentialStore>());
        DataContext = _viewModel;
        Opened += (_, _) => _ = _viewModel.ReloadAsync();
        _viewModel.RunWizardRequested += OnRunWizardRequested;
    }

    private void OnRunWizardRequested(object? sender, EventArgs e)
    {
        if (_services is not { } services || _viewModel is null)
        {
            return;
        }

        var wizard = services.GetRequiredService<SetupWizardWindow>();
        wizard.Closed += OnWizardClosed;
        wizard.Show();
    }

    private void OnWizardClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Closed -= OnWizardClosed;
        }

        _ = _viewModel?.ReloadAsync();
    }
}
