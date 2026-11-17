namespace Syncrowatch.App.Settings;

public interface ISettingsRouter
{
    Task OpenAsync(CancellationToken cancellationToken = default);
}
