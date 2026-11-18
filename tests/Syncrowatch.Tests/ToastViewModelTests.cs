using Shouldly;
using Syncrowatch.App.Notifications;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class ToastViewModelTests
{
    [Theory]
    [InlineData(ToastKind.AutoMerged, "toast-automerged-24.png")]
    [InlineData(ToastKind.SyncCompleted, "toast-synccompleted-24.png")]
    [InlineData(ToastKind.Conflict, "toast-conflict-24.png")]
    [InlineData(ToastKind.Failure, "toast-failure-24.png")]
    [InlineData(ToastKind.RelayPackage, "toast-relaypackage-24.png")]
    [InlineData(ToastKind.RelayWarning, "toast-relaywarning-24.png")]
    [InlineData(ToastKind.LinkCopied, "toast-linkcopied-24.png")]
    [InlineData(ToastKind.LinkPending, "toast-linkpending-24.png")]
    public void Glyph_ResolvesTheCutToastPngForEveryKind(ToastKind kind, string expectedFile)
    {
        var toast = new ToastViewModel(new ToastContent(kind, "t", "m"));

        toast.Glyph.ShouldBe($"avares://syncrowatch/Assets/toasts/{expectedFile}");
    }

    [Theory]
    [InlineData(ToastKind.AutoMerged, true, false, false)]
    [InlineData(ToastKind.SyncCompleted, true, false, false)]
    [InlineData(ToastKind.Conflict, false, true, false)]
    [InlineData(ToastKind.Failure, false, true, false)]
    [InlineData(ToastKind.RelayWarning, false, true, false)]
    [InlineData(ToastKind.RelayPackage, false, false, true)]
    [InlineData(ToastKind.LinkCopied, false, false, true)]
    [InlineData(ToastKind.LinkPending, false, false, true)]
    public void AccentGroup_MapsKindsPerTheRecolorContract(
        ToastKind kind, bool positive, bool attention, bool neutral)
    {
        var toast = new ToastViewModel(new ToastContent(kind, "t", "m"));

        toast.IsPositive.ShouldBe(positive);
        toast.IsAttention.ShouldBe(attention);
        toast.IsNeutral.ShouldBe(neutral);
    }
}
