using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Syncrowatch.App;

public partial class Brand : ResourceDictionary
{
    public Brand() => AvaloniaXamlLoader.Load(this);
}
