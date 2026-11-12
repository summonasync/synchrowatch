using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Syncrowatch.Core.History;

namespace Syncrowatch.App.History;

public partial class HistoryWindow : Window
{
    private readonly HistoryViewModel? _viewModel;

    public HistoryWindow()
    {
        InitializeComponent();
    }

    public HistoryWindow(IServiceProvider services) : this()
    {
        _viewModel = new HistoryViewModel(
            services.GetRequiredService<ISyncEventStore>(),
            services.GetRequiredService<IPendingConflictReader>());
        DataContext = _viewModel;
        Opened += (_, _) => _ = _viewModel.RefreshAsync();
    }
}
