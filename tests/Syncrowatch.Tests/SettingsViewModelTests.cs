using NSubstitute;
using Shouldly;
using Synchro.Core;
using Syncrowatch.App.Settings;
using Syncrowatch.Core;
using Syncrowatch.Core.Autostart;
using Syncrowatch.Core.Config;
using Xunit;

namespace Syncrowatch.Tests;

public class SettingsViewModelTests
{
    private const string Secret = "ghp_settings_7a1b2c3d4e5f";

    [Fact]
    public async Task Reload_MapsEveryConfigField()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        await viewModel.ReloadAsync();

        viewModel.WatchedFolder.ShouldBe(harness.Config.WatchedFolder);
        viewModel.RemoteUrl.ShouldBe(harness.Config.RemoteUrl);
        viewModel.Branch.ShouldBe(harness.Config.Branch);
        viewModel.RunMode.ShouldBe("Semi-auto");
        viewModel.Debounce.ShouldBe("20 seconds");
        viewModel.PollInterval.ShouldBe("60 seconds");
        viewModel.IdentityName.ShouldBe(harness.Config.Identity.Name);
        viewModel.IdentityEmail.ShouldBe(harness.Config.Identity.Email);
        viewModel.HasError.ShouldBeFalse();
    }

    [Fact]
    public async Task Reload_TokenStored_ShowsMaskOnly()
    {
        var harness = new Harness();
        harness.Credentials.Read(Arg.Any<CancellationToken>())
            .Returns(new Result<PersonalAccessToken>.Ok(new PersonalAccessToken(Secret)));
        var viewModel = harness.ViewModel;

        await viewModel.ReloadAsync();

        viewModel.Token.ShouldBe(SettingsViewModel.MaskedToken);
    }

    [Fact]
    public async Task Reload_TokenValue_NeverReachesAnyMember()
    {
        var harness = new Harness();
        harness.Credentials.Read(Arg.Any<CancellationToken>())
            .Returns(new Result<PersonalAccessToken>.Ok(new PersonalAccessToken(Secret)));
        var viewModel = harness.ViewModel;

        await viewModel.ReloadAsync();

        foreach (var property in typeof(SettingsViewModel).GetProperties())
        {
            if (property.PropertyType == typeof(string) && property.GetValue(viewModel) is string value)
            {
                value.Contains(Secret).ShouldBeFalse($"{property.Name} leaked the token value");
            }
        }
    }

    [Fact]
    public async Task Reload_TokenAbsent_ShowsMissing()
    {
        var harness = new Harness();
        harness.Credentials.Read(Arg.Any<CancellationToken>())
            .Returns(new Result<PersonalAccessToken>.Fail(new SyncError("token_missing", "no token stored")));
        var viewModel = harness.ViewModel;

        await viewModel.ReloadAsync();

        viewModel.Token.ShouldBe("not stored");
    }

    [Fact]
    public async Task Refresh_AfterWizardFinish_ReloadsChangedFields()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;
        await viewModel.ReloadAsync();

        var reconfigured = harness.Config with
        {
            Branch = "release",
            Mode = SyncRunMode.Manual,
            PollInterval = TimeSpan.FromSeconds(90)
        };
        harness.ConfigStore.Load(Arg.Any<CancellationToken>())
            .Returns(new Result<SyncConfig>.Ok(reconfigured));

        await viewModel.ReloadAsync();

        viewModel.Branch.ShouldBe("release");
        viewModel.RunMode.ShouldBe("Manual");
        viewModel.PollInterval.ShouldBe("90 seconds");
    }

    [Fact]
    public async Task Reload_UnreadableConfig_ShowsError()
    {
        var harness = new Harness();
        harness.ConfigStore.Load(Arg.Any<CancellationToken>())
            .Returns(new Result<SyncConfig>.Fail(new SyncError("config_missing", "settings file absent")));
        var viewModel = harness.ViewModel;

        await viewModel.ReloadAsync();

        viewModel.HasError.ShouldBeTrue();
        viewModel.ErrorMessage.ShouldBe("settings file absent");
    }

    [Fact]
    public async Task Reload_PreferenceStored_MapsLaunchAtLogin()
    {
        var harness = new Harness();
        harness.Preferences.Load(Arg.Any<CancellationToken>())
            .Returns(new Result<AppPreferences>.Ok(new AppPreferences { LaunchAtLogin = true }));

        await harness.ViewModel.ReloadAsync();

        harness.ViewModel.LaunchAtLogin.ShouldBeTrue();
        harness.ViewModel.HasError.ShouldBeFalse();
    }

    [Fact]
    public async Task Reload_UnreadablePreferences_ShowsErrorAndKeepsLaunchOff()
    {
        var harness = new Harness();
        harness.Preferences.Load(Arg.Any<CancellationToken>())
            .Returns(new Result<AppPreferences>.Fail(new SyncError("unavailable", "preferences file could not be read")));

        await harness.ViewModel.ReloadAsync();

        harness.ViewModel.HasError.ShouldBeTrue();
        harness.ViewModel.ErrorMessage.ShouldBe("preferences file could not be read");
        harness.ViewModel.LaunchAtLogin.ShouldBeFalse();
    }

    [Fact]
    public async Task ToggleLaunch_OffToOn_EnablesEntryAndPersists()
    {
        var harness = new Harness();
        await harness.ViewModel.ReloadAsync();

        await harness.ViewModel.ToggleLaunchAtLoginCommand.ExecuteAsync(null);

        harness.Autostart.Received(1).Enable(Arg.Any<string>());
        await harness.Preferences.Received(1).Save(
            Arg.Is<AppPreferences>(p => p.LaunchAtLogin),
            Arg.Any<CancellationToken>());
        harness.ViewModel.LaunchAtLogin.ShouldBeTrue();
        harness.ViewModel.HasError.ShouldBeFalse();
    }

    [Fact]
    public async Task ToggleLaunch_OnToOff_DisablesEntryAndPersists()
    {
        var harness = new Harness();
        harness.Preferences.Load(Arg.Any<CancellationToken>())
            .Returns(new Result<AppPreferences>.Ok(new AppPreferences { LaunchAtLogin = true }));
        await harness.ViewModel.ReloadAsync();

        await harness.ViewModel.ToggleLaunchAtLoginCommand.ExecuteAsync(null);

        harness.Autostart.Received(1).Disable();
        await harness.Preferences.Received(1).Save(
            Arg.Is<AppPreferences>(p => !p.LaunchAtLogin),
            Arg.Any<CancellationToken>());
        harness.ViewModel.LaunchAtLogin.ShouldBeFalse();
        harness.ViewModel.HasError.ShouldBeFalse();
    }

    [Fact]
    public async Task ToggleLaunch_EnableFails_ShowsErrorAndLeavesPreferenceUntouched()
    {
        var harness = new Harness();
        await harness.ViewModel.ReloadAsync();
        harness.Autostart.Enable(Arg.Any<string>())
            .Returns(new Result<Unit>.Fail(new SyncError("unavailable", "autostart entry could not be written")));

        await harness.ViewModel.ToggleLaunchAtLoginCommand.ExecuteAsync(null);

        harness.ViewModel.HasError.ShouldBeTrue();
        harness.ViewModel.ErrorMessage.ShouldBe("autostart entry could not be written");
        harness.ViewModel.LaunchAtLogin.ShouldBeFalse();
        await harness.Preferences.DidNotReceive().Save(Arg.Any<AppPreferences>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToggleLaunch_SaveFails_ShowsErrorAndKeepsToggleOff()
    {
        var harness = new Harness();
        await harness.ViewModel.ReloadAsync();
        harness.Preferences.Save(Arg.Any<AppPreferences>(), Arg.Any<CancellationToken>())
            .Returns(new Result<AppPreferences>.Fail(new SyncError("unavailable", "preferences file could not be written")));

        await harness.ViewModel.ToggleLaunchAtLoginCommand.ExecuteAsync(null);

        harness.ViewModel.HasError.ShouldBeTrue();
        harness.ViewModel.ErrorMessage.ShouldBe("preferences file could not be written");
        harness.ViewModel.LaunchAtLogin.ShouldBeFalse();
        harness.Autostart.Received(1).Enable(Arg.Any<string>());
    }

    [Fact]
    public async Task ToggleLaunch_RoundTrip_SyncsDesktopEntryWithPreferenceFile()
    {
        var dir = TempDir.Create();
        try
        {
            var manager = new XdgDesktopAutostartManager(dir);
            var store = new JsonAppPreferencesStore(dir);
            var viewModel = new SettingsViewModel(
                Substitute.For<ISyncConfigStore>(),
                Substitute.For<ICredentialStore>(),
                store,
                manager);

            await viewModel.ToggleLaunchAtLoginCommand.ExecuteAsync(null);

            File.Exists(manager.FilePath).ShouldBeTrue();
            var enabled = await store.Load(CancellationToken.None);
            enabled.ShouldBeOfType<Result<AppPreferences>.Ok>().Value.LaunchAtLogin.ShouldBeTrue();

            await viewModel.ToggleLaunchAtLoginCommand.ExecuteAsync(null);

            File.Exists(manager.FilePath).ShouldBeFalse();
            var disabled = await store.Load(CancellationToken.None);
            disabled.ShouldBeOfType<Result<AppPreferences>.Ok>().Value.LaunchAtLogin.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void RunWizardCommand_RaisesRequest()
    {
        var harness = new Harness();
        var requested = false;
        harness.ViewModel.RunWizardRequested += (_, _) => requested = true;

        harness.ViewModel.RunWizardCommand.Execute(null);

        requested.ShouldBeTrue();
    }

    private sealed class Harness
    {
        public SyncConfig Config { get; } = new()
        {
            WatchedFolder = "/home/dev/slurpy",
            RemoteUrl = "https://github.com/acme/private.git",
            Branch = "main",
            Mode = SyncRunMode.SemiAuto,
            Debounce = TimeSpan.FromSeconds(20),
            PollInterval = TimeSpan.FromSeconds(60),
            Identity = new CommitIdentity { Name = "Syncrowatch", Email = "syncrowatch@users.noreply.github.com" }
        };

        public ISyncConfigStore ConfigStore { get; } = Substitute.For<ISyncConfigStore>();

        public ICredentialStore Credentials { get; } = Substitute.For<ICredentialStore>();

        public IAppPreferencesStore Preferences { get; } = Substitute.For<IAppPreferencesStore>();

        public IAutostartManager Autostart { get; } = Substitute.For<IAutostartManager>();

        public SettingsViewModel ViewModel { get; }

        public Harness()
        {
            ConfigStore.Load(Arg.Any<CancellationToken>())
                .Returns(new Result<SyncConfig>.Ok(Config));
            Credentials.Read(Arg.Any<CancellationToken>())
                .Returns(new Result<PersonalAccessToken>.Fail(new SyncError("token_missing", "no token stored")));
            Preferences.Load(Arg.Any<CancellationToken>())
                .Returns(new Result<AppPreferences>.Ok(new AppPreferences()));
            Preferences.Save(Arg.Any<AppPreferences>(), Arg.Any<CancellationToken>())
                .Returns(call => new Result<AppPreferences>.Ok(call.ArgAt<AppPreferences>(0)));
            Autostart.Enable(Arg.Any<string>()).Returns(new Result<Unit>.Ok(Unit.Value));
            Autostart.Disable().Returns(new Result<Unit>.Ok(Unit.Value));
            ViewModel = new SettingsViewModel(ConfigStore, Credentials, Preferences, Autostart);
        }
    }
}
