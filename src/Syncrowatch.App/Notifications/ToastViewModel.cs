using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Syncrowatch.App.Notifications;

public sealed class ToastViewModel(ToastContent content) : INotifyPropertyChanged
{
    private int _slot;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ToastContent Content { get; } = content;

    public ToastKind Kind => Content.Kind;

    public string Title => Content.Title;

    public string Message => Content.Message;

    public string? Command => Content.Command;

    public bool HasCommand => !string.IsNullOrWhiteSpace(Content.Command);

    public string Glyph => GlyphSource(Content.Kind);

    public bool IsPositive => Content.Kind is ToastKind.AutoMerged or ToastKind.SyncCompleted;

    public bool IsAttention => Content.Kind is ToastKind.Conflict or ToastKind.Failure or ToastKind.RelayWarning;

    public bool IsNeutral => Content.Kind is ToastKind.RelayPackage or ToastKind.LinkCopied or ToastKind.LinkPending;

    public int Slot
    {
        get => _slot;
        set
        {
            if (_slot != value)
            {
                _slot = value;
                Notify();
            }
        }
    }

    private static string GlyphSource(ToastKind kind) =>
        $"avares://syncrowatch/Assets/toasts/toast-{kind.ToString().ToLowerInvariant()}-24.png";

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
