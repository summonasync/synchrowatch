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

        if (!IsAcceptableRemoteUrl(config.RemoteUrl))
        {
            return SyncError.Invalid("remote url must be an https or ssh git address");
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

    public static bool IsAcceptableRemoteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith('-'))
        {
            return false;
        }

        foreach (var scheme in (string[])[HttpsPrefix, SshPrefix])
        {
            if (url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                return HasHost(url[scheme.Length..]);
            }
        }

        return IsScpLike(url);
    }

    private const string HttpsPrefix = "https://";
    private const string SshPrefix = "ssh://";

    private static bool HasHost(string rest)
    {
        var host = rest.Split('/', 2)[0].Trim();
        return host.Length > 0 && host.Contains('.');
    }

    private static bool IsScpLike(string url)
    {
        var colon = url.IndexOf(':');
        var at = url.IndexOf('@');
        if (colon <= 0 || at <= 0 || at >= colon)
        {
            return false;
        }

        var host = url[(at + 1)..colon];
        return host.Length > 0 && colon < url.Length - 1;
    }
}
