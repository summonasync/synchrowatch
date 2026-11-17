using Avalonia.Controls;
using Shouldly;
using Syncrowatch.App.Tray;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class TrayIconCacheTests
{
    [Fact]
    public void IconFor_MissingStateAsset_FallsBackToDefaultAndCachesTheMiss()
    {
        var loader = new RecordingLoader();
        var cache = new TrayIconCache(loader, TrayIconVariant.Symbolic);

        cache.IconFor(TrayState.Idle, pendingPush: false).ShouldBeNull();
        cache.IconFor(TrayState.Idle, pendingPush: false).ShouldBeNull();

        loader.Requests.ShouldBe(
        [
            TrayIconPaths.Resolve(TrayState.Idle, pendingPush: false, TrayIconVariant.Symbolic),
            TrayIconPaths.DefaultIcon
        ]);
    }

    [Fact]
    public void IconFor_RepeatedSwapsAcrossKeys_DecodeOnceEach()
    {
        var loader = new RecordingLoader();
        var cache = new TrayIconCache(loader, TrayIconVariant.Color);

        cache.IconFor(TrayState.Syncing, pendingPush: false);
        cache.IconFor(TrayState.Idle, pendingPush: true);
        cache.IconFor(TrayState.Error, pendingPush: false);
        var decodeCount = loader.Requests.Count;

        cache.IconFor(TrayState.Syncing, pendingPush: false);
        cache.IconFor(TrayState.Idle, pendingPush: true);
        cache.IconFor(TrayState.Error, pendingPush: false);

        decodeCount.ShouldBe(6);
        loader.Requests.Count.ShouldBe(decodeCount);
        loader.Requests.ShouldContain(TrayIconPaths.Resolve(TrayState.Syncing, false, TrayIconVariant.Color));
        loader.Requests.ShouldContain(TrayIconPaths.Resolve(TrayState.Idle, true, TrayIconVariant.Color));
        loader.Requests.ShouldContain(TrayIconPaths.Resolve(TrayState.Error, false, TrayIconVariant.Color));
    }

    [Fact]
    public void IconFor_AsksForTheAssetOfTheConfiguredVariant()
    {
        var symbolic = new RecordingLoader();
        var color = new RecordingLoader();

        new TrayIconCache(symbolic, TrayIconVariant.Symbolic).IconFor(TrayState.Conflict, pendingPush: false);
        new TrayIconCache(color, TrayIconVariant.Color).IconFor(TrayState.Conflict, pendingPush: false);

        symbolic.Requests[0].ShouldBe(TrayIconPaths.Resolve(TrayState.Conflict, false, TrayIconVariant.Symbolic));
        color.Requests[0].ShouldBe(TrayIconPaths.Resolve(TrayState.Conflict, false, TrayIconVariant.Color));
    }

    private sealed class RecordingLoader : ITrayIconLoader
    {
        private readonly List<string> _requests = [];

        public IReadOnlyList<string> Requests => _requests;

        public WindowIcon? Load(string assetPath)
        {
            _requests.Add(assetPath);
            return null;
        }
    }
}
