using Avalonia.Controls;

namespace Syncrowatch.App.Settings;

public sealed class WizardSettingsSurface(Window wizard) : ISettingsSurface
{
    public void Show() => wizard.Show();
}
