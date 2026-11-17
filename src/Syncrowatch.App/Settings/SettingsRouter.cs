using Synchro.Core;
using Syncrowatch.Core.Config;

namespace Syncrowatch.App.Settings;

public sealed class SettingsRouter(
    ISyncConfigStore configStore,
    Func<ISettingsSurface> wizard,
    Func<ISettingsSurface> preview) : ISettingsRouter
{
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await configStore.Load(cancellationToken).ConfigureAwait(true);
        if (loaded is Result<SyncConfig>.Ok ok && SyncConfigValidation.Validate(ok.Value) is null)
        {
            preview().Show();
            return;
        }

        wizard().Show();
    }
}
