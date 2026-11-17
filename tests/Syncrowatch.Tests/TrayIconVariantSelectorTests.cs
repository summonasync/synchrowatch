using Shouldly;
using Syncrowatch.App.Tray;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class TrayIconVariantSelectorTests
{
    [Fact]
    public void For_Windows_PicksTheColorVariant()
    {
        TrayIconVariantSelector.For(isWindows: true).ShouldBe(TrayIconVariant.Color);
    }

    [Fact]
    public void For_UnixDesktops_PicksTheSymbolicVariant()
    {
        TrayIconVariantSelector.For(isWindows: false).ShouldBe(TrayIconVariant.Symbolic);
    }
}
