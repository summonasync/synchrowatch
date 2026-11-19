namespace Syncrowatch.Core.Config;

public sealed record AppPreferences
{
    public bool LaunchAtLogin { get; init; } = false;
}
