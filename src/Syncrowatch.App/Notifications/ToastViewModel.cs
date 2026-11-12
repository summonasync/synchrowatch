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

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
