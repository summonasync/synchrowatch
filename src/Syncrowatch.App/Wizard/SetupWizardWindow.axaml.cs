using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Syncrowatch.Core;
using Syncrowatch.Core.Bootstrap;
using Syncrowatch.Core.Config;

namespace Syncrowatch.App.Wizard;

public partial class SetupWizardWindow : Window
{
    private readonly SetupWizardViewModel? _viewModel;
    private bool _dismissalConfirmed;

    public SetupWizardWindow()
    {
        InitializeComponent();
    }

    public SetupWizardWindow(IServiceProvider services) : this()
    {
        _viewModel = new SetupWizardViewModel(
            services.GetRequiredService<Func<SyncConfig, IRemoteRepository>>(),
            services.GetRequiredService<Func<SyncConfig, IBootstrapper>>(),
            services.GetRequiredService<ISyncConfigStore>(),
            services.GetRequiredService<ICredentialStore>(),
            new StorageProviderFolderPicker(this));
        DataContext = _viewModel;
        _viewModel.CloseRequested += (_, _) => Close();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_dismissalConfirmed || _viewModel is null || !_viewModel.RequiresDismissalCleanup)
        {
            return;
        }

        e.Cancel = true;
        _ = ConfirmDismissalAsync();
    }

    private async Task ConfirmDismissalAsync()
    {
        if (_viewModel is null)
        {
            return;
        }

        if (await _viewModel.ConfirmDismissalAsync().ConfigureAwait(true))
        {
            _dismissalConfirmed = true;
            Close();
        }
    }
}
