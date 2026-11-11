namespace Syncrowatch.App.Notifications;

public sealed class ToastViewModel(ToastContent content)
{
    public ToastContent Content { get; } = content;

    public ToastKind Kind => Content.Kind;

    public string Title => Content.Title;

    public string Message => Content.Message;

    public string? Command => Content.Command;

    public bool HasCommand => !string.IsNullOrWhiteSpace(Content.Command);

    public int Slot { get; init; }
}
