using Avalonia.Threading;

namespace Syncrowatch.App.Tray;

public sealed class DetectionPrompt : IDetectionPrompt
{
    private readonly object _gate = new();
    private DetectionPromptDialog? _dialog;

    public async Task<bool> RequestAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var dialog = await Dispatcher.UIThread.InvokeAsync(() => Present(paths));
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

    private DetectionPromptDialog? Present(IReadOnlyList<string> paths)
    {
        lock (_gate)
        {
            if (_dialog is not null)
            {
                return null;
            }

            var dialog = new DetectionPromptDialog(paths);
            _dialog = dialog;
            dialog.Show();
            return dialog;
        }
    }

    private void Release(DetectionPromptDialog dialog)
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
