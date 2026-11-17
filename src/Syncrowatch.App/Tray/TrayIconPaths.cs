namespace Syncrowatch.App.Tray;

public static class TrayIconPaths
{
    public const string DefaultIcon = "avares://syncrowatch/Assets/syncrowatch-tray.png";
    public const int PreferredSize = 32;

    public static string Resolve(TrayState state, bool pendingPush, TrayIconVariant variant) =>
        pendingPush && state == TrayState.Idle
            ? Asset("pending", VariantName(variant))
            : Asset(StateName(state), VariantName(variant));

    private static string Asset(string name, string variant) =>
        $"avares://syncrowatch/Assets/tray/syncrowatch-tray-{name}-{variant}-{PreferredSize}.png";

    private static string StateName(TrayState state) => state switch
    {
        TrayState.Idle => "idle",
        TrayState.Syncing => "syncing",
        TrayState.Conflict => "conflict",
        TrayState.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    private static string VariantName(TrayIconVariant variant) => variant switch
    {
        TrayIconVariant.Symbolic => "symbolic",
        TrayIconVariant.Color => "color",
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
    };
}
