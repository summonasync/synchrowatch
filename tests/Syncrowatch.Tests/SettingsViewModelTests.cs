using NSubstitute;
using Shouldly;
using Synchro.Core;
using Syncrowatch.App.Settings;
using Syncrowatch.Core;
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

        public SettingsViewModel ViewModel { get; }

        public Harness()
        {
            ConfigStore.Load(Arg.Any<CancellationToken>())
                .Returns(new Result<SyncConfig>.Ok(Config));
            Credentials.Read(Arg.Any<CancellationToken>())
                .Returns(new Result<PersonalAccessToken>.Fail(new SyncError("token_missing", "no token stored")));
            ViewModel = new SettingsViewModel(ConfigStore, Credentials);
        }
    }
}
