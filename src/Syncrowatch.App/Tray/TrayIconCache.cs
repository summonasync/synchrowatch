using Avalonia.Controls;

namespace Syncrowatch.App.Tray;

public sealed class TrayIconCache
{
    private readonly ITrayIconLoader _loader;
    private readonly TrayIconVariant _variant;
    private readonly Dictionary<(TrayState State, bool Pending, TrayIconVariant Variant), WindowIcon?> _icons = new();
    private readonly object _gate = new();

    public TrayIconCache(ITrayIconLoader loader, TrayIconVariant variant)
    {
        _loader = loader;
        _variant = variant;
    }

    public WindowIcon? IconFor(TrayState state, bool pendingPush)
    {
        lock (_gate)
        {
            var key = (state, pendingPush, _variant);
            if (_icons.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var icon = _loader.Load(TrayIconPaths.Resolve(state, pendingPush, _variant))
                       ?? _loader.Load(TrayIconPaths.DefaultIcon);
            _icons[key] = icon;
            return icon;
        }
    }
}
