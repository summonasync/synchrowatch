using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Syncrowatch.App.Tray;
using Syncrowatch.Core.History;

namespace Syncrowatch.App.History;

public partial class HistoryWindow : Window
{
    private readonly HistoryViewModel? _viewModel;
    private readonly IServiceProvider? _services;

    public HistoryWindow()
    {
        InitializeComponent();
    }

    public HistoryWindow(IServiceProvider services) : this()
    {
        _services = services;
        _viewModel = new HistoryViewModel(
            services.GetRequiredService<ISyncEventStore>(),
            services.GetRequiredService<IPendingConflictReader>(),
            services.GetRequiredService<IConflictResolver>());
        DataContext = _viewModel;
        Opened += (_, _) => _ = _viewModel.RefreshAsync();
    }

    private void OnOpenFilesClicked(object? sender, RoutedEventArgs e) =>
        _services?.GetRequiredService<IFilesView>().Show();
}
