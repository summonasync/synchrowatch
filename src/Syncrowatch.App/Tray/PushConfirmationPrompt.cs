using Avalonia.Threading;
using Syncrowatch.Core.Engine;

namespace Syncrowatch.App.Tray;

public sealed class PushConfirmationPrompt : IPushConfirmation
{
    private readonly object _gate = new();
    private PushConfirmationDialog? _dialog;

    public async Task<bool> RequestAsync(PendingPush push, CancellationToken cancellationToken)
    {
        var dialog = await Dispatcher.UIThread.InvokeAsync(() => Present(push));
        if (dialog is null)
        {
            return false;
        }

        try
        {
            return await dialog.Decision.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Release(dialog);
            await Dispatcher.UIThread.InvokeAsync(dialog.Abandon);
        }
    }

    private PushConfirmationDialog? Present(PendingPush push)
    {
        lock (_gate)
        {
            if (_dialog is not null)
            {
                return null;
            }

            var dialog = new PushConfirmationDialog(push);
            _dialog = dialog;
            dialog.Show();
            return dialog;
        }
    }

    private void Release(PushConfirmationDialog dialog)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_dialog, dialog))
            {
                _dialog = null;
            }
        }
    }
}
