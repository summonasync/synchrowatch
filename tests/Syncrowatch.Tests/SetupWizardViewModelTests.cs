using NSubstitute;
using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.App.Wizard;
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
        await harness.Credentials.DidNotReceiveWithAnyArgs().Read(default);
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
    public async Task Dismissal_AfterTokenPage_DeletesStoredToken()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();

        viewModel.RequiresDismissalCleanup.ShouldBeTrue();

        var closable = await viewModel.ConfirmDismissalAsync();

        closable.ShouldBeTrue();
        viewModel.RequiresDismissalCleanup.ShouldBeFalse();
        await harness.Credentials.Received(1).Delete(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dismissal_AfterFinishedBootstrap_KeepsToken()
    {
        var harness = new Harness();
        await ReachBootstrapAsync(harness, new Result<BootstrapPlan>.Ok(new BootstrapPlan.PublishFolder()));
        await harness.ViewModel.FinishAsync();
        harness.ViewModel.Step.ShouldBe(WizardStep.Completed);

        harness.ViewModel.RequiresDismissalCleanup.ShouldBeFalse();

        var closable = await harness.ViewModel.ConfirmDismissalAsync();

        closable.ShouldBeTrue();
        await harness.Credentials.DidNotReceiveWithAnyArgs().Delete(default);
    }

    [Fact]
    public async Task Assess_WithPublishablePlan_RaisesFinishCanExecuteChanged()
    {
        var harness = new Harness();
        var raisesWithFinishablePlan = 0;
        harness.ViewModel.FinishCommand.CanExecuteChanged += (_, _) =>
        {
            if (harness.ViewModel.CanFinish)
            {
                raisesWithFinishablePlan++;
            }
        };

        await ReachBootstrapAsync(harness, new Result<BootstrapPlan>.Ok(new BootstrapPlan.PublishFolder()));

        harness.ViewModel.CanFinish.ShouldBeTrue();
        raisesWithFinishablePlan.ShouldBeGreaterThanOrEqualTo(1);
        harness.ViewModel.FinishCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task Assess_WithWatchEmptyPlan_EnablesFinishAndDescribesFreshStart()
    {
        var harness = new Harness();
        await ReachBootstrapAsync(harness, new Result<BootstrapPlan>.Ok(new BootstrapPlan.WatchEmpty()));

        harness.ViewModel.CanFinish.ShouldBeTrue();
        harness.ViewModel.PlanSummary.ShouldNotBeNull().ShouldContain("both empty");
        harness.ViewModel.FinishCommand.CanExecute(null).ShouldBeTrue();
        await harness.ViewModel.FinishAsync();
        harness.ViewModel.Step.ShouldBe(WizardStep.Completed);
        await harness.ConfigStore.Received(1).Save(Arg.Any<SyncConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dismissal_OnFirstStep_NeedsNoCleanupCall()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        viewModel.RequiresDismissalCleanup.ShouldBeFalse();

        var closable = await viewModel.ConfirmDismissalAsync();

        closable.ShouldBeTrue();
        harness.Credentials.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancel_WhenDeleteFails_SurfacesErrorAndStaysOpen()
    {
        var harness = new Harness();
        harness.Credentials.Delete(Arg.Any<CancellationToken>())
            .Returns(new Result<Unit>.Fail(new SyncError("unavailable", "credential file could not be removed")));
        var viewModel = harness.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();
        viewModel.RemoteUrl = RepoUrl;
        var closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;

        await viewModel.CancelAsync();

        closed.ShouldBeFalse();
        viewModel.HasError.ShouldBeTrue();
        viewModel.ErrorMessage.ShouldNotBeNull().ShouldContain("credential file");
        await harness.Credentials.Received(1).Delete(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Browse_PickerExplodes_SurfacesTypedErrorAndReleasesBusy()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;
        harness.FolderPicker.PickAsync()
            .Returns(Task.FromException<string?>(new InvalidOperationException("folder probe exploded")));
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();
        viewModel.RemoteUrl = RepoUrl;
        await viewModel.ValidateRepositoryAsync();
        await viewModel.ContinueAsync();
        viewModel.Step.ShouldBe(WizardStep.Folder);

        viewModel.BrowseFolderCommand.Execute(null);

        viewModel.ErrorMessage.ShouldNotBeNull().ShouldContain("folder probe exploded");
        viewModel.IsBusy.ShouldBeFalse();
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
        saved.Mode.ShouldBe(SyncRunMode.Auto);
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

    [Theory]
    [InlineData(SyncRunMode.Auto)]
    [InlineData(SyncRunMode.Manual)]
    [InlineData(SyncRunMode.SemiAuto)]
    public async Task Finish_SelectedRunMode_IsPersisted(SyncRunMode mode)
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
        SelectRunMode(harness.ViewModel, mode);

        await harness.ViewModel.FinishAsync();

        saved.ShouldNotBeNull();
        saved!.Mode.ShouldBe(mode);
    }

    [Fact]
    public async Task Validate_PublicRepository_ExposesPublicKindForRefusalForm()
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

        viewModel.RepoErrorKind.ShouldBe("public_repo");
        viewModel.RepoErrorIsPublic.ShouldBeTrue();
        viewModel.RepoValidated.ShouldBeFalse();
        viewModel.HasError.ShouldBeTrue();
    }

    [Fact]
    public async Task Validate_UnreachableRepository_ExposesNonPublicKindForSemicolonForm()
    {
        var harness = new Harness();
        harness.Remote.ValidateRepository(Arg.Any<CancellationToken>())
            .Returns(new Result<RemoteValidation>.Fail(
                new SyncError("unreachable", "could not resolve host syncrowatch.invalid")));
        var viewModel = harness.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();
        viewModel.RemoteUrl = "https://syncrowatch.invalid/acme/private.git";

        await viewModel.ValidateRepositoryAsync();

        viewModel.RepoErrorKind.ShouldBe("unreachable");
        viewModel.RepoErrorIsPublic.ShouldBeFalse();
        viewModel.RepoValidated.ShouldBeFalse();
        viewModel.HasError.ShouldBeTrue();
    }

    [Fact]
    public async Task Validate_SuccessfulProbe_ExposesBranchAndValidatedForm()
    {
        var harness = new Harness();
        harness.Remote.ValidateRepository(Arg.Any<CancellationToken>())
            .Returns(new Result<RemoteValidation>.Ok(new RemoteValidation(true, "develop")));
        var viewModel = harness.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();
        viewModel.RemoteUrl = RepoUrl;

        await viewModel.ValidateRepositoryAsync();

        viewModel.RepoValidated.ShouldBeTrue();
        viewModel.Branch.ShouldBe("develop");
        viewModel.RepoErrorKind.ShouldBeNull();
        viewModel.HasError.ShouldBeFalse();
    }

    [Fact]
    public async Task EditUrl_AfterValidation_ClearsValidationFormState()
    {
        var harness = new Harness();
        harness.Remote.ValidateRepository(Arg.Any<CancellationToken>())
            .Returns(new Result<RemoteValidation>.Ok(new RemoteValidation(true, "main")));
        var viewModel = harness.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();
        viewModel.RemoteUrl = RepoUrl;
        await viewModel.ValidateRepositoryAsync();

        viewModel.RemoteUrl = "https://syncrowatch.test/other.git";

        viewModel.RepoValidated.ShouldBeFalse();
        viewModel.RepoErrorKind.ShouldBeNull();
        viewModel.RepoErrorIsPublic.ShouldBeFalse();
    }

    [Theory]
    [InlineData(BootstrapDirection.RepoWins)]
    [InlineData(BootstrapDirection.FolderWins)]
    public async Task Choose_BothSidesNonEmpty_MarksChosenDirection(BootstrapDirection direction)
    {
        var harness = new Harness();
        await ReachBootstrapAsync(harness, new Result<BootstrapPlan>.Ok(
            new BootstrapPlan.Decide(new BootstrapOptions("main", "a1b2c3d4e5f6a7b8a9b0", 3))));

        await harness.ViewModel.ChooseAsync(direction);

        harness.ViewModel.IsRepoWinsChosen.ShouldBe(direction == BootstrapDirection.RepoWins);
        harness.ViewModel.IsFolderWinsChosen.ShouldBe(direction == BootstrapDirection.FolderWins);
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("checkout")]
    [InlineData("watch")]
    [InlineData("decide")]
    public async Task Assess_WithPlan_FlagsOnlyMatchingBootstrapCard(string scenario)
    {
        var harness = new Harness();
        var assessOutcome = scenario switch
        {
            "publish" => new Result<BootstrapPlan>.Ok(new BootstrapPlan.PublishFolder()),
            "checkout" => new Result<BootstrapPlan>.Ok(new BootstrapPlan.CheckoutRemote()),
            "watch" => new Result<BootstrapPlan>.Ok(new BootstrapPlan.WatchEmpty()),
            _ => new Result<BootstrapPlan>.Ok(
                new BootstrapPlan.Decide(new BootstrapOptions("main", "c3d4e5f6a7b8a9b0c1d2", 5)))
        };
        await ReachBootstrapAsync(harness, assessOutcome);
        var viewModel = harness.ViewModel;

        viewModel.HasPlan.ShouldBeTrue();
        viewModel.PlanIsPublishFolder.ShouldBe(scenario == "publish");
        viewModel.PlanIsCheckoutRemote.ShouldBe(scenario == "checkout");
        viewModel.PlanIsWatchEmpty.ShouldBe(scenario == "watch");
        viewModel.NeedsChoice.ShouldBe(scenario == "decide");
    }

    [Fact]
    public async Task Steps_AdvancedToBootstrap_MarkPassedRailSteps()
    {
        var harness = new Harness();
        await ReachBootstrapAsync(harness, new Result<BootstrapPlan>.Ok(new BootstrapPlan.PublishFolder()));
        var viewModel = harness.ViewModel;

        viewModel.TokenDone.ShouldBeTrue();
        viewModel.RepositoryDone.ShouldBeTrue();
        viewModel.FolderDone.ShouldBeTrue();
        viewModel.PreferencesDone.ShouldBeTrue();
        viewModel.BootstrapDone.ShouldBeFalse();

        await viewModel.FinishAsync();

        viewModel.BootstrapDone.ShouldBeTrue();
        viewModel.Step.ShouldBe(WizardStep.Completed);
    }

    [Theory]
    [InlineData(WizardStep.Repository, WizardStep.AccessToken)]
    [InlineData(WizardStep.Folder, WizardStep.Repository)]
    [InlineData(WizardStep.Preferences, WizardStep.Folder)]
    [InlineData(WizardStep.Bootstrap, WizardStep.Preferences)]
    public async Task Back_FromStep_MovesToPreviousStep(WizardStep from, WizardStep expected)
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;
        await AdvanceAsync(harness, from);
        viewModel.Step.ShouldBe(from);
        viewModel.BackCommand.CanExecute(null).ShouldBeTrue();

        await viewModel.BackAsync();

        viewModel.Step.ShouldBe(expected);
    }

    [Fact]
    public async Task Back_OnFirstStep_Refuses()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        viewModel.BackCommand.CanExecute(null).ShouldBeFalse();
        await viewModel.BackAsync();

        viewModel.Step.ShouldBe(WizardStep.AccessToken);
    }

    [Fact]
    public async Task Back_AfterCompleted_Refuses()
    {
        var harness = new Harness();
        await ReachBootstrapAsync(harness, new Result<BootstrapPlan>.Ok(new BootstrapPlan.PublishFolder()));
        await harness.ViewModel.FinishAsync();
        var viewModel = harness.ViewModel;
        viewModel.Step.ShouldBe(WizardStep.Completed);

        viewModel.BackCommand.CanExecute(null).ShouldBeFalse();
        await viewModel.BackAsync();

        viewModel.Step.ShouldBe(WizardStep.Completed);
    }

    [Fact]
    public async Task Back_WhileAssessPending_DisabledUntilBusyClears()
    {
        var harness = new Harness();
        var assessGate = new TaskCompletionSource<Result<BootstrapPlan>>();
        harness.Bootstrapper.Assess(Arg.Any<CancellationToken>()).Returns(assessGate.Task);
        var viewModel = harness.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();
        viewModel.RemoteUrl = RepoUrl;
        await viewModel.ValidateRepositoryAsync();
        await viewModel.ContinueAsync();
        viewModel.FolderPath = FolderPath;
        await viewModel.ContinueAsync();
        viewModel.Step.ShouldBe(WizardStep.Preferences);

        var advancing = viewModel.ContinueAsync();
        viewModel.Step.ShouldBe(WizardStep.Bootstrap);
        viewModel.IsBusy.ShouldBeTrue();
        viewModel.BackCommand.CanExecute(null).ShouldBeFalse();

        await viewModel.BackAsync();
        viewModel.Step.ShouldBe(WizardStep.Bootstrap);

        assessGate.SetResult(new Result<BootstrapPlan>.Ok(new BootstrapPlan.PublishFolder()));
        await advancing;

        viewModel.BackCommand.CanExecute(null).ShouldBeTrue();
        await viewModel.BackAsync();
        viewModel.Step.ShouldBe(WizardStep.Preferences);
    }

    [Fact]
    public async Task Back_FromPreferences_PreservesEnteredFieldsAcrossRoundTrip()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;
        await AdvanceAsync(harness, WizardStep.Preferences);
        SelectRunMode(viewModel, SyncRunMode.Manual);
        viewModel.IdentityName = "Ops Bot";
        viewModel.IdentityEmail = "ops@syncrowatch.test";

        await viewModel.BackAsync();

        viewModel.Step.ShouldBe(WizardStep.Folder);
        viewModel.RemoteUrl.ShouldBe(RepoUrl);
        viewModel.FolderPath.ShouldBe(FolderPath);
        viewModel.IdentityName.ShouldBe("Ops Bot");
        viewModel.IdentityEmail.ShouldBe("ops@syncrowatch.test");
        viewModel.UseManualMode.ShouldBeTrue();

        await viewModel.ContinueAsync();

        viewModel.Step.ShouldBe(WizardStep.Preferences);
        viewModel.IdentityName.ShouldBe("Ops Bot");
        viewModel.IdentityEmail.ShouldBe("ops@syncrowatch.test");
        viewModel.UseManualMode.ShouldBeTrue();
    }

    [Fact]
    public async Task Back_FromFolder_KeepsRepositoryValidationWhenUrlUnchanged()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;
        await AdvanceAsync(harness, WizardStep.Folder);

        await viewModel.BackAsync();

        viewModel.Step.ShouldBe(WizardStep.Repository);
        viewModel.RepoValidated.ShouldBeTrue();
        viewModel.Branch.ShouldBe("main");
        viewModel.ContinueCommand.CanExecute(null).ShouldBeTrue();

        await viewModel.ContinueAsync();

        viewModel.Step.ShouldBe(WizardStep.Folder);
        await harness.Remote.Received(1).ValidateRepository(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Back_FromRepository_RefillsTokenFieldFromStoreAndKeepsCredential()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();
        viewModel.Step.ShouldBe(WizardStep.Repository);
        viewModel.AccessToken.ShouldBeEmpty();

        await viewModel.BackAsync();

        viewModel.Step.ShouldBe(WizardStep.AccessToken);
        viewModel.AccessToken.ShouldBe(Token);
        await harness.Credentials.Received(1).Read(Arg.Any<CancellationToken>());
        await harness.Credentials.DidNotReceiveWithAnyArgs().Delete(default);

        viewModel.AccessToken = "ghp_rotated_token_9f8e7d6c";
        await viewModel.ContinueAsync();

        viewModel.Step.ShouldBe(WizardStep.Repository);
        await harness.Credentials.Received(1).Write(
            Arg.Is<PersonalAccessToken>(candidate => candidate.Value == Token), Arg.Any<CancellationToken>());
        await harness.Credentials.Received(1).Write(
            Arg.Is<PersonalAccessToken>(candidate => candidate.Value == "ghp_rotated_token_9f8e7d6c"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("missing_file")]
    public async Task Back_FromRepository_FailedStoreRead_LeavesTokenFieldEmpty(string errorCode)
    {
        var harness = new Harness();
        harness.Credentials.Read(Arg.Any<CancellationToken>())
            .Returns(new Result<PersonalAccessToken>.Fail(new SyncError(errorCode, "credential store refused the read")));
        var viewModel = harness.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();
        viewModel.Step.ShouldBe(WizardStep.Repository);

        await viewModel.BackAsync();

        viewModel.Step.ShouldBe(WizardStep.AccessToken);
        viewModel.AccessToken.ShouldBeEmpty();
        viewModel.HasError.ShouldBeFalse();
        viewModel.ContinueCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Back_FromBootstrap_ExecutesNothingAndRewindsToPreferences()
    {
        var harness = new Harness();
        await ReachBootstrapAsync(harness, new Result<BootstrapPlan>.Ok(
            new BootstrapPlan.Decide(new BootstrapOptions("main", "a1b2c3d4e5f6a7b8a9b0", 3))));
        var viewModel = harness.ViewModel;

        await viewModel.BackAsync();

        viewModel.Step.ShouldBe(WizardStep.Preferences);
        await harness.Bootstrapper.DidNotReceiveWithAnyArgs().Execute(null!, default, default);
        harness.ConfigStore.ReceivedCalls().ShouldBeEmpty();
    }

    private static void SelectRunMode(SetupWizardViewModel viewModel, SyncRunMode mode)
    {
        switch (mode)
        {
            case SyncRunMode.Auto:
                viewModel.UseAutoMode = true;
                break;
            case SyncRunMode.Manual:
                viewModel.UseManualMode = true;
                break;
            case SyncRunMode.SemiAuto:
                viewModel.UseSemiAutoMode = true;
                break;
        }
    }

    private static async Task AdvanceAsync(Harness harness, WizardStep target)
    {
        harness.Bootstrapper.Assess(Arg.Any<CancellationToken>())
            .Returns(new Result<BootstrapPlan>.Ok(new BootstrapPlan.PublishFolder()));
        var viewModel = harness.ViewModel;
        viewModel.AccessToken = Token;
        if (target == WizardStep.AccessToken)
        {
            return;
        }

        await viewModel.ContinueAsync();
        if (target == WizardStep.Repository)
        {
            return;
        }

        viewModel.RemoteUrl = RepoUrl;
        await viewModel.ValidateRepositoryAsync();
        await viewModel.ContinueAsync();
        if (target == WizardStep.Folder)
        {
            return;
        }

        viewModel.FolderPath = FolderPath;
        await viewModel.ContinueAsync();
        if (target == WizardStep.Preferences)
        {
            return;
        }

        await viewModel.ContinueAsync();
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
            Credentials.Read(Arg.Any<CancellationToken>())
                .Returns(new Result<PersonalAccessToken>.Ok(new PersonalAccessToken(Token)));
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
