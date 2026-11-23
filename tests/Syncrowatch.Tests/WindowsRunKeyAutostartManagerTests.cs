using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.Core.Autostart;
using Xunit;

namespace Syncrowatch.Tests;

public class WindowsRunKeyAutostartManagerTests
{
    [Fact]
    public void BuildRunKeyData_PathWithSpacesAndBackslashes_IsFullyQuoted()
    {
        var data = WindowsRunKeyAutostartManager.BuildRunKeyData(@"C:\Program Files\Syncro watch\Syncrowatch.App.exe");

        data.ShouldBe("\"C:\\Program Files\\Syncro watch\\Syncrowatch.App.exe\"");
    }

    [Fact]
    public void BuildRunKeyData_AlreadyQuotedInput_IsWrappedWithoutUnquoting()
    {
        var data = WindowsRunKeyAutostartManager.BuildRunKeyData("\"C:\\Syncrowatch\\Syncrowatch.App.exe\"");

        data.ShouldBe("\"\"C:\\Syncrowatch\\Syncrowatch.App.exe\"\"");
    }

    [Fact]
    public void Enable_OnLinux_ReturnsUnsupportedWithoutTouchingRegistry()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var outcome = new WindowsRunKeyAutostartManager().Enable(@"C:\Syncrowatch\Syncrowatch.App.exe");

        outcome.ShouldBeOfType<Result<Unit>.Fail>().Error.Code.ShouldBe("unsupported");
    }

    [Fact]
    public void IsApplied_OnLinux_ReturnsUnsupportedWithoutTouchingRegistry()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var outcome = new WindowsRunKeyAutostartManager().IsApplied();

        outcome.ShouldBeOfType<Result<bool>.Fail>().Error.Code.ShouldBe("unsupported");
    }
}
