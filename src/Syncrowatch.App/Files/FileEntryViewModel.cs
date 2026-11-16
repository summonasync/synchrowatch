using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Syncrowatch.App.Files;

public sealed class FileEntryViewModel(string path, string statusText) : INotifyPropertyChanged
{
    private string _statusText = statusText;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; } = path;

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            Notify();
        }
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
