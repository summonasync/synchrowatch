using Shouldly;
using Syncrowatch.App.Tray;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class TrayIconPathsTests
{
    public static TheoryData<TrayState, TrayIconVariant, string> StateAndVariantCases() => new()
    {
        { TrayState.Idle, TrayIconVariant.Symbolic, "avares://syncrowatch/Assets/tray/syncrowatch-tray-idle-symbolic-32.png" },
        { TrayState.Idle, TrayIconVariant.Color, "avares://syncrowatch/Assets/tray/syncrowatch-tray-idle-color-32.png" },
        { TrayState.Syncing, TrayIconVariant.Symbolic, "avares://syncrowatch/Assets/tray/syncrowatch-tray-syncing-symbolic-32.png" },
        { TrayState.Syncing, TrayIconVariant.Color, "avares://syncrowatch/Assets/tray/syncrowatch-tray-syncing-color-32.png" },
        { TrayState.Conflict, TrayIconVariant.Symbolic, "avares://syncrowatch/Assets/tray/syncrowatch-tray-conflict-symbolic-32.png" },
        { TrayState.Conflict, TrayIconVariant.Color, "avares://syncrowatch/Assets/tray/syncrowatch-tray-conflict-color-32.png" },
        { TrayState.Error, TrayIconVariant.Symbolic, "avares://syncrowatch/Assets/tray/syncrowatch-tray-error-symbolic-32.png" },
        { TrayState.Error, TrayIconVariant.Color, "avares://syncrowatch/Assets/tray/syncrowatch-tray-error-color-32.png" }
    };

    [Theory]
    [MemberData(nameof(StateAndVariantCases))]
    public void Resolve_MapsEveryStateToTheManifestNamedAsset(TrayState state, TrayIconVariant variant, string expected)
    {
        TrayIconPaths.Resolve(state, pendingPush: false, variant).ShouldBe(expected);
    }

    [Fact]
    public void Resolve_PushPendingOnIdle_SwapsToThePendingAsset()
    {
        TrayIconPaths.Resolve(TrayState.Idle, pendingPush: true, TrayIconVariant.Symbolic)
            .ShouldBe("avares://syncrowatch/Assets/tray/syncrowatch-tray-pending-symbolic-32.png");

        TrayIconPaths.Resolve(TrayState.Idle, pendingPush: true, TrayIconVariant.Color)
            .ShouldBe("avares://syncrowatch/Assets/tray/syncrowatch-tray-pending-color-32.png");
    }

    [Theory]
    [InlineData(TrayState.Syncing)]
    [InlineData(TrayState.Conflict)]
    [InlineData(TrayState.Error)]
    public void Resolve_PushPendingOnActiveStates_KeepsTheStateAsset(TrayState state)
    {
        TrayIconPaths.Resolve(state, pendingPush: true, TrayIconVariant.Color)
            .ShouldBe(TrayIconPaths.Resolve(state, pendingPush: false, TrayIconVariant.Color));
    }

    [Fact]
    public void Resolve_CoversEveryDeclaredTrayStateWithADistinctAsset()
    {
        var states = Enum.GetValues<TrayState>();

        var mapped = states
            .Select(state => TrayIconPaths.Resolve(state, pendingPush: false, TrayIconVariant.Symbolic))
            .ToArray();

        mapped.Distinct().Count().ShouldBe(states.Length);
    }

    [Fact]
    public void DefaultIcon_PointsAtTheExistingTrayAsset()
    {
        TrayIconPaths.DefaultIcon.ShouldBe("avares://syncrowatch/Assets/syncrowatch-tray.png");
    }
}
