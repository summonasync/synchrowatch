using NSubstitute;
using Shouldly;
using Synchro.Core;
using Syncrowatch.App.Settings;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Xunit;

namespace Syncrowatch.Tests;

public class SettingsRouterTests
{
    [Fact]
    public async Task Open_ValidConfig_ShowsPreview()
    {
        var harness = new Harness();
        harness.ConfigStore.Load(Arg.Any<CancellationToken>())
            .Returns(new Result<SyncConfig>.Ok(ValidConfig()));

        await harness.Router.OpenAsync();

        harness.Preview.Received(1).Show();
        harness.Wizard.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Open_AbsentConfig_ShowsWizard()
    {
        var harness = new Harness();
        harness.ConfigStore.Load(Arg.Any<CancellationToken>())
            .Returns(new Result<SyncConfig>.Fail(new SyncError("config_missing", "settings file absent")));

        await harness.Router.OpenAsync();

        harness.Wizard.Received(1).Show();
        harness.Preview.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Open_CorruptConfig_ShowsWizard()
    {
        var harness = new Harness();
        harness.ConfigStore.Load(Arg.Any<CancellationToken>())
            .Returns(new Result<SyncConfig>.Fail(SyncError.Invalid("settings file is not valid configuration")));

        await harness.Router.OpenAsync();

        harness.Wizard.Received(1).Show();
        harness.Preview.ReceivedCalls().ShouldBeEmpty();
    }

    private static SyncConfig ValidConfig() => new()
    {
        WatchedFolder = "/home/dev/slurpy",
        RemoteUrl = "https://github.com/acme/private.git",
        Branch = "main",
        Mode = SyncRunMode.Auto,
        Identity = new CommitIdentity { Name = "Syncrowatch", Email = "syncrowatch@users.noreply.github.com" }
    };

    private sealed class Harness
    {
        public ISyncConfigStore ConfigStore { get; } = Substitute.For<ISyncConfigStore>();

        public ISettingsSurface Wizard { get; } = Substitute.For<ISettingsSurface>();

        public ISettingsSurface Preview { get; } = Substitute.For<ISettingsSurface>();

        public SettingsRouter Router { get; }

        public Harness() => Router = new SettingsRouter(
            ConfigStore,
            () => Wizard,
            () => Preview);
    }
}
