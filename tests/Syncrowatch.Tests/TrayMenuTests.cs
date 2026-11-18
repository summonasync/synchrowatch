using Avalonia.Controls;
using Shouldly;
using Syncrowatch.App.Tray;
using Xunit;

namespace Syncrowatch.Tests;

public class TrayMenuTests
{
    [Fact]
    public void BuildMenu_ContainsSettingsBetweenFilesAndExit()
    {
        var menu = TrayController.BuildMenu(
            new NativeMenuItem("Sync now"),
            new NativeMenuItem("Pause"),
            new NativeMenuItem("Auto"),
            new NativeMenuItem("Manual"),
            new NativeMenuItem("Semi-auto"),
            new NativeMenuItem("Open history"),
            new NativeMenuItem("Open files"),
            new NativeMenuItem("Settings"),
            new NativeMenuItem("Exit"));

        var headers = menu.Items.OfType<NativeMenuItem>().Select(item => item.Header).ToList();

        headers.ShouldContain("Settings");
        headers.IndexOf("Settings").ShouldBe(headers.IndexOf("Open files") + 1);
    }

    [Fact]
    public void BuildMenu_KeepsSeparatorGroups()
    {
        var menu = TrayController.BuildMenu(
            new NativeMenuItem("Sync now"),
            new NativeMenuItem("Pause"),
            new NativeMenuItem("Auto"),
            new NativeMenuItem("Manual"),
            new NativeMenuItem("Semi-auto"),
            new NativeMenuItem("Open history"),
            new NativeMenuItem("Open files"),
            new NativeMenuItem("Settings"),
            new NativeMenuItem("Exit"));

        menu.Items.OfType<NativeMenuItemSeparator>().Count().ShouldBe(3);
    }
}
