using Avalonia.Controls;
using Syncrowatch.Core.Engine;

namespace Syncrowatch.App.Tray;

public partial class PushConfirmationDialog : Window
{
    private readonly TaskCompletionSource<bool> _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _settled;

    public PushConfirmationDialog()
    {
        InitializeComponent();
    }

    public PushConfirmationDialog(PendingPush push) : this()
    {
        var model = new PushConfirmationViewModel(push);
        DataContext = model;
        model.Confirmed += (_, _) => Decide(true);
        model.Dismissed += (_, _) => Decide(false);
        Closing += OnClosing;
    }

    public Task<bool> Decision => _decision.Task;

    public void Abandon() => Decide(false);

    private void Decide(bool confirmed)
    {
        if (_settled)
        {
            return;
        }

        _settled = true;
        _decision.TrySetResult(confirmed);
        Close();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_settled)
        {
            _settled = true;
            _decision.TrySetResult(false);
        }
    }
}
