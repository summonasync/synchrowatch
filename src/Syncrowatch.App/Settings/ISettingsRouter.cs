namespace Syncrowatch.App.Settings;

public interface ISettingsRouter
{
    event EventHandler? WizardClosed;

    Task OpenAsync(CancellationToken cancellationToken = default);

    void OpenWizard();
}
