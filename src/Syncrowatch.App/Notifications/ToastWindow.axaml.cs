using Avalonia.Controls;
using Avalonia.Threading;

namespace Syncrowatch.App.Notifications;

public partial class ToastWindow : Window
{
    private const int StackGap = 8;

    private readonly ToastViewModel? _toast;
    private IDisposable? _dismissTimer;

    public ToastWindow()
    {
        InitializeComponent();
    }

    public ToastWindow(ToastViewModel toast) : this()
    {
        _toast = toast;
        DataContext = toast;
    }

    public event EventHandler? Expired;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_toast is not { } toast)
        {
            return;
        }

        PositionAtSlot(toast);
        _dismissTimer = DispatcherTimer.RunOnce(Expire, Hold(toast.Kind));
    }

    protected override void OnClosed(EventArgs e)
    {
        _dismissTimer?.Dispose();
        base.OnClosed(e);
    }

    private void Expire()
    {
        Expired?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void PositionAtSlot(ToastViewModel toast)
    {
        if (Screens.Primary is not { } screen)
        {
            return;
        }

        var area = screen.WorkingArea;
        var slotOffset = toast.Slot * (Height + StackGap);
        Position = new Avalonia.PixelPoint(
            area.Right - (int)Width - 24,
            area.Bottom - (int)Height - 24 - (int)slotOffset);
    }

    private static TimeSpan Hold(ToastKind kind) => kind switch
    {
        ToastKind.Conflict or ToastKind.Failure => TimeSpan.FromSeconds(8),
        _ => TimeSpan.FromSeconds(5)
    };
}
