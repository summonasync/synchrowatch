using Avalonia.Threading;

namespace Syncrowatch.App.Notifications;

public sealed class ToastWindowFactory : IToastWindowFactory
{
    private readonly Dictionary<ToastViewModel, ToastWindow> _open = [];

    public void Present(ToastViewModel toast, Action expired) => Dispatcher.UIThread.Post(() =>
    {
        var window = new ToastWindow(toast);
        window.Expired += (_, _) => expired();
        window.Closed += (_, _) => _open.Remove(toast);
        _open[toast] = window;
        window.Show();
    });

    public void Dismiss(ToastViewModel toast) => Dispatcher.UIThread.Post(() =>
    {
        if (_open.TryGetValue(toast, out var window))
        {
            window.Close();
        }
    });
}
