using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Synchro.Core;
using Syncrowatch.App.Notifications;
using Syncrowatch.App.Tray;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Transport;

namespace Syncrowatch.App.Files;

public partial class FilesWindow : Window, IFilesWindow
{
    private readonly FilesViewModel? _viewModel;

    public FilesWindow()
    {
        InitializeComponent();
    }

    public FilesWindow(IServiceProvider services) : this()
    {
        _viewModel = new FilesViewModel(
            services.GetRequiredService<ISyncConfigStore>(),
            services.GetRequiredService<ILocalGit>(),
            services.GetRequiredService<ISyncEventStore>(),
            services.GetRequiredService<ISyncEventBus>(),
            services.GetRequiredService<IPendingPathsSource>(),
            services.GetRequiredService<Func<SyncConfig, IRemoteRepository>>(),
            services.GetRequiredService<ToastService>(),
            clipboardWriter: CopyToClipboard,
            uiPost: action => Dispatcher.UIThread.Post(action));
        DataContext = _viewModel;
        Opened += (_, _) => _ = _viewModel.RefreshAsync();
        Closed += (_, _) => _viewModel.Dispose();
    }

    private Task CopyToClipboard(string text) =>
        Clipboard is { } clipboard ? clipboard.SetTextAsync(text) : Task.CompletedTask;
}
