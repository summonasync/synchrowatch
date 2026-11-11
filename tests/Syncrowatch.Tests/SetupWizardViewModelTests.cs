using NSubstitute;
using Shouldly;
using Synchro.Core;
using Syncrowatch.App.Wizard;
using Syncrowatch.Core;
using Syncrowatch.Core.Bootstrap;
using Syncrowatch.Core.Config;
using Xunit;

namespace Syncrowatch.Tests;

public class SetupWizardViewModelTests
{
    private const string Token = "ghp_wizardprobe_5b4a3c2d1e0f";
    private const string RepoUrl = "https://syncrowatch.test/acme/private.git";
    private const string FolderPath = "/tmp/wizard-watch";
    private const string UnacceptableMessage = "remote url must be an https or ssh git address";

    [Theory]
    [InlineData("")]
    [InlineData("-oProxyCommand=evil")]
    [InlineData("ftp://example.com/repo.git")]
    [InlineData("https://localhost")]
    [InlineData("/srv/bare.git")]
    public async Task Validate_UnacceptableUrl_RefusesBeforeAnyTransportCall(string remoteUrl)
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();
        viewModel.RemoteUrl = remoteUrl;

        await viewModel.ValidateRepositoryAsync();

        harness.Remote.ReceivedCalls().ShouldBeEmpty();
        harness.ConfigStore.ReceivedCalls().ShouldBeEmpty();
        viewModel.ErrorMessage.ShouldBe(UnacceptableMessage);
        viewModel.Step.ShouldBe(WizardStep.Repository);
        viewModel.ContinueCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Validate_PublicRepository_ShowsRefusalAndBlocksAdvance()
    {
        var harness = new Harness();
        harness.Remote.ValidateRepository(Arg.Any<CancellationToken>())
            .Returns(new Result<RemoteValidation>.Fail(
                new SyncError("public_repo", $"repository {RepoUrl} is publicly readable; sync only accepts private repositories")));
        var viewModel = harness.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();
        viewModel.RemoteUrl = RepoUrl;

        await viewModel.ValidateRepositoryAsync();

        viewModel.ErrorMessage.ShouldNotBeNull().ShouldContain("publicly readable");
        viewModel.Step.ShouldBe(WizardStep.Repository);
        viewModel.ContinueCommand.CanExecute(null).ShouldBeFalse();
        harness.ConfigStore.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Continue_EmptyToken_LeavesCredentialStoreUntouched()
    {
        var harness = new Harness();

        await harness.ViewModel.ContinueAsync();

        harness.ViewModel.Step.ShouldBe(WizardStep.AccessToken);
        harness.Credentials.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Continue_ValidToken_StoresTokenAndClearsField()
    {
        var harness = new Harness();
        harness.ViewModel.AccessToken = $"  {Token}  ";

        await harness.ViewModel.ContinueAsync();

        harness.ViewModel.Step.ShouldBe(WizardStep.Repository);
        harness.ViewModel.AccessToken.ShouldBeEmpty();
        await harness.Credentials.Received(1).Write(
            Arg.Is<PersonalAccessToken>(candidate => candidate.Value == Token), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Continue_WithoutValidation_KeepsRepositoryStep()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();
        viewModel.RemoteUrl = RepoUrl;

        await viewModel.ContinueAsync();

        viewModel.Step.ShouldBe(WizardStep.Repository);
        harness.Bootstrapper.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Continue_EditedUrlAfterValidation_RequiresRevalidation()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();
        viewModel.RemoteUrl = RepoUrl;
        await viewModel.ValidateRepositoryAsync();
        viewModel.ContinueCommand.CanExecute(null).ShouldBeTrue();

        viewModel.RemoteUrl = "https://syncrowatch.test/other.git";

        viewModel.ContinueCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Browse_PickedFolder_FillsPath()
    {
        var harness = new Harness();
        harness.FolderPicker.PickAsync().Returns(FolderPath);
        var viewModel = harness.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();

        await viewModel.BrowseFolderAsync();

        viewModel.FolderPath.ShouldBe(FolderPath);
    }

    [Fact]
    public async Task Cancel_WithPendingChoice_PersistsNothingAndExecutesNothing()
    {
        var harness = new Harness();
        await ReachBootstrapAsync(harness, new Result<BootstrapPlan>.Ok(
            new BootstrapPlan.Decide(new BootstrapOptions("main", "a1b2c3d4e5f6a7b8a9b0", 3))));
        var closed = false;
        harness.ViewModel.CloseRequested += (_, _) => closed = true;

        await harness.ViewModel.CancelAsync();

        harness.ConfigStore.ReceivedCalls().ShouldBeEmpty();
        await harness.Bootstrapper.DidNotReceiveWithAnyArgs().Execute(null!, default, default);
        await harness.Credentials.Received(1).Delete(Arg.Any<CancellationToken>());
        closed.ShouldBeTrue();
    }

    [Fact]
    public async Task Finish_PublishFolderPlan_PersistsInputAndExecutesBootstrap()
    {
        var harness = new Harness();
        SyncConfig? saved = null;
        harness.ConfigStore.Save(Arg.Any<SyncConfig>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                saved = call.ArgAt<SyncConfig>(0);
                return new Result<SyncConfig>.Ok(saved);
            });
        await ReachBootstrapAsync(harness, new Result<BootstrapPlan>.Ok(new BootstrapPlan.PublishFolder()));

        await harness.ViewModel.FinishAsync();

        saved.ShouldNotBeNull();
        saved!.WatchedFolder.ShouldBe(FolderPath);
        saved.RemoteUrl.ShouldBe(RepoUrl);
        saved.Branch.ShouldBe("main");
        saved.Mode.ShouldBe(SyncMode.Git);
        saved.Identity.Name.ShouldBe(CommitIdentity.Default.Name);
        saved.Identity.Email.ShouldBe(CommitIdentity.Default.Email);
        saved.Debounce.ShouldBe(SyncConfig.DefaultDebounce);
        saved.PollInterval.ShouldBe(SyncConfig.DefaultPollInterval);
        await harness.Bootstrapper.Received(1).Execute(
            Arg.Is<BootstrapPlan>(plan => plan is BootstrapPlan.PublishFolder),
            null, Arg.Any<CancellationToken>());
        await harness.Credentials.Received(1).Write(
            Arg.Is<PersonalAccessToken>(candidate => candidate.Value == Token), Arg.Any<CancellationToken>());
        harness.ViewModel.Step.ShouldBe(WizardStep.Completed);
        harness.ViewModel.AccessToken.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(BootstrapDirection.RepoWins)]
    [InlineData(BootstrapDirection.FolderWins)]
    public async Task Choose_BothSidesNonEmpty_ExecutesWithChosenDirection(BootstrapDirection direction)
    {
        var harness = new Harness();
        await ReachBootstrapAsync(harness, new Result<BootstrapPlan>.Ok(
            new BootstrapPlan.Decide(new BootstrapOptions("main", "b0a9b8a7f6e5d4c3b2a1", 7))));

        await harness.ViewModel.ChooseAsync(direction);

        await harness.Bootstrapper.Received(1).Execute(
            Arg.Any<BootstrapPlan>(), Arg.Is<BootstrapDirection?>(value => value == direction), Arg.Any<CancellationToken>());
        await harness.ConfigStore.Received(1).Save(Arg.Any<SyncConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Finish_InvalidConfig_PersistsNothingAndExecutesNothing()
    {
        var harness = new Harness();
        await ReachBootstrapAsync(harness, new Result<BootstrapPlan>.Ok(new BootstrapPlan.PublishFolder()));
        harness.ViewModel.IdentityName = "   ";

        await harness.ViewModel.FinishAsync();

        harness.ConfigStore.ReceivedCalls().ShouldBeEmpty();
        await harness.Bootstrapper.DidNotReceiveWithAnyArgs().Execute(null!, default, default);
        harness.ViewModel.HasError.ShouldBeTrue();
        harness.ViewModel.Step.ShouldBe(WizardStep.Bootstrap);
    }

    private static async Task ReachBootstrapAsync(Harness harness, Result<BootstrapPlan> assessOutcome)
    {
        harness.Bootstrapper.Assess(Arg.Any<CancellationToken>()).Returns(assessOutcome);
        var viewModel = harness.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();
        viewModel.RemoteUrl = RepoUrl;
        await viewModel.ValidateRepositoryAsync();
        await viewModel.ContinueAsync();
        viewModel.FolderPath = FolderPath;
        await viewModel.ContinueAsync();
        await viewModel.ContinueAsync();
        viewModel.Step.ShouldBe(WizardStep.Bootstrap);
    }

    private sealed class Harness
    {
        public IRemoteRepository Remote { get; } = Substitute.For<IRemoteRepository>();

        public IBootstrapper Bootstrapper { get; } = Substitute.For<IBootstrapper>();

        public ISyncConfigStore ConfigStore { get; } = Substitute.For<ISyncConfigStore>();

        public ICredentialStore Credentials { get; } = Substitute.For<ICredentialStore>();

        public IFolderPicker FolderPicker { get; } = Substitute.For<IFolderPicker>();

        public SetupWizardViewModel ViewModel { get; }

        public Harness()
        {
            Credentials.Write(Arg.Any<PersonalAccessToken>(), Arg.Any<CancellationToken>())
                .Returns(call => new Result<PersonalAccessToken>.Ok(call.ArgAt<PersonalAccessToken>(0)));
            Credentials.Delete(Arg.Any<CancellationToken>())
                .Returns(new Result<Unit>.Ok(Unit.Value));
            ConfigStore.Save(Arg.Any<SyncConfig>(), Arg.Any<CancellationToken>())
                .Returns(call => new Result<SyncConfig>.Ok(call.ArgAt<SyncConfig>(0)));
            Remote.ValidateRepository(Arg.Any<CancellationToken>())
                .Returns(new Result<RemoteValidation>.Ok(new RemoteValidation(true, "main")));
            ViewModel = new SetupWizardViewModel(
                _ => Remote,
                _ => Bootstrapper,
                ConfigStore,
                Credentials,
                FolderPicker);
        }
    }
}
