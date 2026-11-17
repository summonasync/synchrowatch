using Avalonia.Controls;

namespace Syncrowatch.App.Tray;

public interface ITrayIconLoader
{
    WindowIcon? Load(string assetPath);
}
