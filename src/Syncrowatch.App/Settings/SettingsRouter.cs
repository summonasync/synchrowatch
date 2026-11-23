using Syncrowatch.Core;
using Syncrowatch.Core.Config;

namespace Syncrowatch.App.Settings;

public sealed class SettingsRouter(
    ISyncConfigStore configStore,
    Func<ISettingsSurface> wizard,
    Func<ISettingsSurface> preview) : ISettingsRouter
{
    public event EventHandler? WizardClosed;

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await configStore.Load(cancellationToken).ConfigureAwait(true);
        var isConfigured = loaded is Result<SyncConfig>.Ok;
        OpenSurface(isConfigured ? preview() : wizard(), raisesWizardClosed: !isConfigured);
    }

    public void OpenWizard() => OpenSurface(wizard(), raisesWizardClosed: true);

    private void OpenSurface(ISettingsSurface surface, bool raisesWizardClosed)
    {
        EventHandler? onClosed = null;
        onClosed = (_, _) =>
        {
            surface.Closed -= onClosed!;
            if (raisesWizardClosed)
            {
                WizardClosed?.Invoke(this, EventArgs.Empty);
            }
        };
        surface.Closed += onClosed;
        surface.Show();
    }
}
