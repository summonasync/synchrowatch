using Shouldly;
using Syncrowatch.Core.Autostart;
using Xunit;

namespace Syncrowatch.Tests;

public class AutostartManagerTests
{
    [Fact]
    public void ForCurrentPlatform_RoutesByOperatingSystem()
    {
        var manager = AutostartManager.ForCurrentPlatform();

        if (OperatingSystem.IsWindows())
        {
            manager.ShouldBeOfType<WindowsRunKeyAutostartManager>();
        }
        else
        {
            manager.ShouldBeOfType<XdgDesktopAutostartManager>();
        }
    }
}
