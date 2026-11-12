namespace Syncrowatch.App.Wizard;

public interface IFolderPicker
{
    Task<string?> PickAsync();
}
