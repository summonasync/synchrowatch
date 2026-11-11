namespace Syncrowatch.App.Notifications;

public interface IToastWindowFactory
{
    void Present(ToastViewModel toast, Action expired);

    void Dismiss(ToastViewModel toast);
}
