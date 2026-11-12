using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Syncrowatch.App.Wizard;

public sealed class StorageProviderFolderPicker(TopLevel owner) : IFolderPicker
{
    public async Task<string?> PickAsync()
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Choose the folder to keep in sync"
        }).ConfigureAwait(true);

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
