using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Syncrowatch.App.Tray;

public sealed class AssetLoaderTrayIconLoader : ITrayIconLoader
{
    public WindowIcon? Load(string assetPath)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(assetPath, UriKind.Absolute));
            return new WindowIcon(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
