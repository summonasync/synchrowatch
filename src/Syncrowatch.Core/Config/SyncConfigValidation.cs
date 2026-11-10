using Synchro.Core;

namespace Syncrowatch.Core.Config;

public static class SyncConfigValidation
{
    public static SyncError? Validate(SyncConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.WatchedFolder))
        {
            return SyncError.Invalid("watched folder is not set");
        }

        if (string.IsNullOrWhiteSpace(config.RemoteUrl))
        {
            return SyncError.Invalid("remote url is not set");
        }

        if (string.IsNullOrWhiteSpace(config.Branch))
        {
            return SyncError.Invalid("branch is not set");
        }

        if (!Enum.IsDefined(config.Mode))
        {
            return SyncError.Invalid("sync mode is not a known value");
        }

        if (config.Identity is null)
        {
            return SyncError.Invalid("commit identity is not set");
        }

        if (string.IsNullOrWhiteSpace(config.Identity.Name))
        {
            return SyncError.Invalid("commit identity name is not set");
        }

        if (string.IsNullOrWhiteSpace(config.Identity.Email))
        {
            return SyncError.Invalid("commit identity email is not set");
        }

        if (config.Debounce < SyncConfig.MinDebounce || config.Debounce > SyncConfig.MaxDebounce)
        {
            return SyncError.Invalid($"debounce must be between {SyncConfig.MinDebounce.TotalSeconds:0} and {SyncConfig.MaxDebounce.TotalSeconds:0} seconds");
        }

        if (config.PollInterval <= TimeSpan.Zero)
        {
            return SyncError.Invalid("pull interval must be positive");
        }

        return null;
    }
}
