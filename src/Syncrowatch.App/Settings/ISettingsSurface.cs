namespace Syncrowatch.App.Settings;

public interface ISettingsSurface
{
    void Show();

    event EventHandler? Closed;
}
