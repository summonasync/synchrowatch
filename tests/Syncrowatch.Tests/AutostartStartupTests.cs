using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.App.Autostart;
using Syncrowatch.Core.Autostart;
using Syncrowatch.Core.Config;
using Xunit;

namespace Syncrowatch.Tests;

public class AutostartStartupTests
{
    [Fact]
    public async Task Run_PreferenceOn_ReassertsEntry()
    {
        var harness = Create(launchOn: true, processPath: () => "/opt/syncrowatch/syncrowatch");

        await harness.Startup.RunAsync();

        harness.Autostart.Received(1).Enable("/opt/syncrowatch/syncrowatch");
    }

    [Fact]
    public async Task Run_PreferenceOff_DoesNotTouchAutostart()
    {
        var harness = Create(launchOn: false, processPath: () => "/opt/syncrowatch/syncrowatch");

        await harness.Startup.RunAsync();

        harness.Autostart.DidNotReceiveWithAnyArgs().Enable(null!);
        harness.Autostart.DidNotReceiveWithAnyArgs().Disable();
    }

    [Fact]
    public async Task Run_ProcessPathNull_SkipsSilently()
    {
        var harness = Create(launchOn: true, processPath: () => null);

        await harness.Startup.RunAsync();

        harness.Autostart.DidNotReceiveWithAnyArgs().Enable(null!);
    }

    [Fact]
    public async Task Run_UnreadablePreferences_DoesNotTouchAutostart()
    {
        var preferences = Substitute.For<IAppPreferencesStore>();
        preferences.Load(Arg.Any<CancellationToken>())
            .Returns(new Result<AppPreferences>.Fail(new SyncError("unavailable", "preferences file could not be read")));
        var autostart = Substitute.For<IAutostartManager>();
        var startup = new AutostartStartup(preferences, autostart, () => "/opt/syncrowatch/syncrowatch");

        await startup.RunAsync();

        autostart.DidNotReceiveWithAnyArgs().Enable(null!);
    }

    [Fact]
    public async Task Run_EnableFails_DoesNotThrow()
    {
        var harness = Create(launchOn: true, processPath: () => "/opt/syncrowatch/syncrowatch");
        harness.Autostart.Enable(Arg.Any<string>())
            .Returns(new Result<Unit>.Fail(new SyncError("unavailable", "autostart entry could not be written")));

        await harness.Startup.RunAsync();
    }

    [Fact]
    public async Task Run_PreferenceLoadThrows_DoesNotThrow()
    {
        var preferences = Substitute.For<IAppPreferencesStore>();
        preferences.Load(Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disk gone"));
        var autostart = Substitute.For<IAutostartManager>();
        var startup = new AutostartStartup(preferences, autostart, () => "/opt/syncrowatch/syncrowatch");

        await startup.RunAsync();
    }

    private static (AutostartStartup Startup, IAutostartManager Autostart) Create(bool launchOn, Func<string?> processPath)
    {
        var preferences = Substitute.For<IAppPreferencesStore>();
        preferences.Load(Arg.Any<CancellationToken>())
            .Returns(new Result<AppPreferences>.Ok(new AppPreferences { LaunchAtLogin = launchOn }));
        var autostart = Substitute.For<IAutostartManager>();
        autostart.Enable(Arg.Any<string>()).Returns(new Result<Unit>.Ok(Unit.Value));
        autostart.Disable().Returns(new Result<Unit>.Ok(Unit.Value));
        return (new AutostartStartup(preferences, autostart, processPath), autostart);
    }
}
