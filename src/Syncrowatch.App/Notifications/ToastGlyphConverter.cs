using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Syncrowatch.App.Notifications;

public sealed class ToastGlyphConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string path && Uri.TryCreate(path, UriKind.Absolute, out var uri)
            ? new Bitmap(AssetLoader.Open(uri))
            : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
