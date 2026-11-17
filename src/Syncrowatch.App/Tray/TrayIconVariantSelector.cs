namespace Syncrowatch.App.Tray;

public static class TrayIconVariantSelector
{
    public static TrayIconVariant Detect() => For(OperatingSystem.IsWindows());

    public static TrayIconVariant For(bool isWindows) =>
        isWindows ? TrayIconVariant.Color : TrayIconVariant.Symbolic;
}
