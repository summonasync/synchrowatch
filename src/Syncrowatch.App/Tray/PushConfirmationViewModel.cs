using Syncrowatch.App.Wizard;
using Syncrowatch.Core.Engine;

namespace Syncrowatch.App.Tray;

public sealed class PushConfirmationViewModel
{
    private const int ShortShaLength = 10;

    public PushConfirmationViewModel(PendingPush push)
    {
        Message = Describe(push);
        ConfirmCommand = new AsyncCommand(() =>
        {
            Confirmed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });
        DismissCommand = new AsyncCommand(() =>
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });
    }

    public string Message { get; }

    public AsyncCommand ConfirmCommand { get; }

    public AsyncCommand DismissCommand { get; }

    public event EventHandler? Confirmed;

    public event EventHandler? Dismissed;

    private static string Describe(PendingPush push)
    {
        var sha = push.Sha.Length <= ShortShaLength ? push.Sha : push.Sha[..ShortShaLength];
        return $"Branch {push.Branch} carries local commits ({sha}) that have not been pushed yet. Push them to the repository now?";
    }
}
