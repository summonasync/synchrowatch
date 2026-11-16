namespace Syncrowatch.App.Tray;

public interface IFilesWindow
{
    event EventHandler? Closed;

    void Show();
}
