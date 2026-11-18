using Avalonia.Controls;

namespace Syncrowatch.App.Settings;

public sealed class WizardSettingsSurface : ISettingsSurface
{
    private readonly Window _wizard;

    public WizardSettingsSurface(Window wizard)
    {
        _wizard = wizard;
        _wizard.Closed += OnWizardClosed;
    }

    public event EventHandler? Closed;

    public void Show() => _wizard.Show();

    private void OnWizardClosed(object? sender, EventArgs e)
    {
        _wizard.Closed -= OnWizardClosed;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
