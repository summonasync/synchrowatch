namespace Syncrowatch.Core.Autostart;

public static class AutostartManager
{
    public static IAutostartManager ForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsRunKeyAutostartManager();
        }

        return new XdgDesktopAutostartManager();
    }
}
