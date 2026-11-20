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
    public void Enable_CleanPath_KeepsExactEntryBytes()
    {
        var dir = TempDir.Create();
        try
        {
            var manager = new XdgDesktopAutostartManager(dir);

            manager.Enable("/opt/syncrowatch/Syncrowatch.App");

            File.ReadAllText(manager.FilePath).ShouldBe(
                "[Desktop Entry]\nType=Application\nExec=/opt/syncrowatch/Syncrowatch.App\nName=Syncrowatch\nX-GNOME-Autostart-enabled=true");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Enable_PathWithSpaces_WritesQuotedExecLine()
    {
        var dir = TempDir.Create();
        try
        {
            var manager = new XdgDesktopAutostartManager(dir);

            manager.Enable("/opt/my apps/Syncrowatch.App");

            File.ReadAllText(manager.FilePath).ShouldContain("Exec=\"/opt/my apps/Syncrowatch.App\"");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BuildExecValue_CleanPath_StaysUnquoted()
    {
        XdgDesktopAutostartManager.BuildExecValue("/opt/syncrowatch/Syncrowatch.App")
            .ShouldBe("/opt/syncrowatch/Syncrowatch.App");
    }

    [Theory]
    [InlineData("/opt/my apps/Syncrowatch.App", "\"/opt/my apps/Syncrowatch.App\"")]
    [InlineData("/opt/tab\tthere/app", "\"/opt/tab\tthere/app\"")]
    [InlineData("/opt/\"quoted\"/app", "\"/opt/\\\"quoted\\\"/app\"")]
    [InlineData("/opt/back\\slash/app", "\"/opt/back\\\\slash/app\"")]
    public void BuildExecValue_SpecialCharacterPaths_AreQuotedAndEscaped(string exePath, string expected)
    {
        XdgDesktopAutostartManager.BuildExecValue(exePath).ShouldBe(expected);
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
