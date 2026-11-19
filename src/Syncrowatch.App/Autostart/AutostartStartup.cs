using Synchro.Core;
using Syncrowatch.Core.Autostart;
using Syncrowatch.Core.Config;

namespace Syncrowatch.App.Autostart;

public sealed class AutostartStartup(
    IAppPreferencesStore preferences,
    IAutostartManager autostart,
    Func<string?> processPath)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var loaded = await preferences.Load(cancellationToken).ConfigureAwait(true);
            if (loaded is not Result<AppPreferences>.Ok { Value.LaunchAtLogin: true })
            {
                return;
            }

            if (processPath() is not { Length: > 0 } exePath)
            {
                return;
            }

            autostart.Enable(exePath);
        }
        catch (Exception)
        {
        }
    }
}
