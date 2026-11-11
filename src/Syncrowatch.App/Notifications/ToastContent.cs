namespace Syncrowatch.App.Notifications;

public sealed record ToastContent(ToastKind Kind, string Title, string Message, string? Command = null);
