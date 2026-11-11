using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Bootstrap;
using Syncrowatch.Core.Config;

namespace Syncrowatch.App.Wizard;

public partial class SetupWizardWindow : Window
{
    public SetupWizardWindow()
    {
        InitializeComponent();
    }

    public SetupWizardWindow(IServiceProvider services) : this()
    {
        DataContext = new SetupWizardViewModel(
            services.GetRequiredService<Func<SyncConfig, IRemoteRepository>>(),
            services.GetRequiredService<Func<SyncConfig, IBootstrapper>>(),
            services.GetRequiredService<ISyncConfigStore>(),
            services.GetRequiredService<ICredentialStore>(),
            new StorageProviderFolderPicker(this));
        if (DataContext is SetupWizardViewModel viewModel)
        {
            viewModel.CloseRequested += (_, _) => Close();
        }
    }
}
