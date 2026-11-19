using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Autostart;
using Xunit;

namespace Syncrowatch.Tests;

public class XdgDesktopAutostartManagerTests
{
    [Fact]
    public void Enable_WritesDesktopEntry_WithExecLine()
    {
        var dir = TempDir.Create();
        try
        {
            var manager = new XdgDesktopAutostartManager(dir);

            var outcome = manager.Enable("/opt/syncrowatch/Syncrowatch.App");

            outcome.ShouldBeOfType<Result<Unit>.Ok>();
            var content = File.ReadAllText(manager.FilePath);
            content.ShouldContain("[Desktop Entry]");
            content.ShouldContain("Type=Application");
            content.ShouldContain("Exec=/opt/syncrowatch/Syncrowatch.App");
            content.ShouldContain("Name=Syncrowatch");
            content.ShouldContain("X-GNOME-Autostart-enabled=true");
            manager.IsApplied().ShouldBeOfType<Result<bool>.Ok>().Value.ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Disable_DeletesTheEntryFile()
    {
        var dir = TempDir.Create();
        try
        {
            var manager = new XdgDesktopAutostartManager(dir);
            manager.Enable("/opt/syncrowatch/Syncrowatch.App");

            var outcome = manager.Disable();

            outcome.ShouldBeOfType<Result<Unit>.Ok>();
            File.Exists(manager.FilePath).ShouldBeFalse();
            manager.IsApplied().ShouldBeOfType<Result<bool>.Ok>().Value.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Enable_Twice_IsIdempotent()
    {
        var dir = TempDir.Create();
        try
        {
            var manager = new XdgDesktopAutostartManager(dir);

            manager.Enable("/opt/syncrowatch/Syncrowatch.App");
            manager.Enable("/opt/syncrowatch/Syncrowatch.App");

            Directory.GetFiles(dir).ShouldHaveSingleItem().ShouldBe(manager.FilePath);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Disable_WithoutAnyEntry_Succeeds()
    {
        var dir = TempDir.Create();
        try
        {
            var outcome = new XdgDesktopAutostartManager(dir).Disable();

            outcome.ShouldBeOfType<Result<Unit>.Ok>();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Enable_UnwritableDirectory_ReturnsUnavailable()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var dir = TempDir.Create();
        var entry = Path.Combine(dir, "readonly");
        try
        {
            Directory.CreateDirectory(entry);
            File.SetUnixFileMode(entry, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            var manager = new XdgDesktopAutostartManager(entry);

            var outcome = manager.Enable("/opt/syncrowatch/Syncrowatch.App");

            outcome.ShouldBeOfType<Result<Unit>.Fail>().Error.Code.ShouldBe("unavailable");
        }
        finally
        {
            File.SetUnixFileMode(entry, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(dir, true);
        }
    }
}
