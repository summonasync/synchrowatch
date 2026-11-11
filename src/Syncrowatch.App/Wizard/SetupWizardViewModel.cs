using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Bootstrap;
using Syncrowatch.Core.Config;

namespace Syncrowatch.App.Wizard;

public sealed class SetupWizardViewModel : INotifyPropertyChanged
{
    private const string FallbackBranch = "main";
    private const string UnacceptableUrlMessage = "remote url must be an https or ssh git address";

    private readonly Func<SyncConfig, IRemoteRepository> _remoteFactory;
    private readonly Func<SyncConfig, IBootstrapper> _bootstrapperFactory;
    private readonly ISyncConfigStore _configStore;
    private readonly ICredentialStore _credentials;
    private readonly IFolderPicker _folderPicker;

    private WizardStep _step = WizardStep.AccessToken;
    private string _accessToken = string.Empty;
    private string _remoteUrl = string.Empty;
    private string _branch = FallbackBranch;
    private string _folderPath = string.Empty;
    private bool _gitMode = true;
    private string _identityName = CommitIdentity.Default.Name;
    private string _identityEmail = CommitIdentity.Default.Email;
    private bool _repoValidated;
    private bool _tokenStored;
    private bool _busy;
    private string? _errorMessage;
    private string? _progress;
    private string? _planSummary;
    private BootstrapPlan? _plan;

    public SetupWizardViewModel(
        Func<SyncConfig, IRemoteRepository> remoteFactory,
        Func<SyncConfig, IBootstrapper> bootstrapperFactory,
        ISyncConfigStore configStore,
        ICredentialStore credentials,
        IFolderPicker folderPicker)
    {
        _remoteFactory = remoteFactory;
        _bootstrapperFactory = bootstrapperFactory;
        _configStore = configStore;
        _credentials = credentials;
        _folderPicker = folderPicker;

        ContinueCommand = new AsyncCommand(ContinueAsync, CanLeaveCurrentStep);
        ValidateRepositoryCommand = new AsyncCommand(ValidateRepositoryAsync, () => IsRepositoryStep && !IsBusy);
        BrowseFolderCommand = new AsyncCommand(BrowseFolderAsync, () => IsFolderStep && !IsBusy);
        ChooseRepoWinsCommand = new AsyncCommand(() => ChooseAsync(BootstrapDirection.RepoWins), ChoicePending);
        ChooseFolderWinsCommand = new AsyncCommand(() => ChooseAsync(BootstrapDirection.FolderWins), ChoicePending);
        FinishCommand = new AsyncCommand(FinishAsync, () => IsBootstrapStep && CanFinish && !IsBusy);
        CancelCommand = new AsyncCommand(CancelAsync, () => IsBootstrapStep && !IsBusy);
        CloseCommand = new AsyncCommand(() =>
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }, () => IsCompleted);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CloseRequested;

    public AsyncCommand ContinueCommand { get; }

    public AsyncCommand ValidateRepositoryCommand { get; }

    public AsyncCommand BrowseFolderCommand { get; }

    public AsyncCommand ChooseRepoWinsCommand { get; }

    public AsyncCommand ChooseFolderWinsCommand { get; }

    public AsyncCommand FinishCommand { get; }

    public AsyncCommand CancelCommand { get; }

    public AsyncCommand CloseCommand { get; }

    public WizardStep Step => _step;

    public bool IsTokenStep => _step == WizardStep.AccessToken;

    public bool IsRepositoryStep => _step == WizardStep.Repository;

    public bool IsFolderStep => _step == WizardStep.Folder;

    public bool IsPreferencesStep => _step == WizardStep.Preferences;

    public bool IsBootstrapStep => _step == WizardStep.Bootstrap;

    public bool IsCompleted => _step == WizardStep.Completed;

    public string AccessToken
    {
        get => _accessToken;
        set
        {
            if (SetField(ref _accessToken, value))
            {
                RefreshCommands();
            }
        }
    }

    public string RemoteUrl
    {
        get => _remoteUrl;
        set
        {
            if (SetField(ref _remoteUrl, value))
            {
                _repoValidated = false;
                RefreshCommands();
            }
        }
    }

    public string FolderPath
    {
        get => _folderPath;
        set
        {
            if (SetField(ref _folderPath, value))
            {
                RefreshCommands();
            }
        }
    }

    public bool UseGitMode
    {
        get => _gitMode;
        set
        {
            if (SetField(ref _gitMode, value))
            {
                Notify(nameof(UsePlainMode));
            }
        }
    }

    public bool UsePlainMode
    {
        get => !_gitMode;
        set
        {
            if (value)
            {
                UseGitMode = false;
            }
        }
    }

    public string IdentityName
    {
        get => _identityName;
        set
        {
            if (SetField(ref _identityName, value))
            {
                RefreshCommands();
            }
        }
    }

    public string IdentityEmail
    {
        get => _identityEmail;
        set
        {
            if (SetField(ref _identityEmail, value))
            {
                RefreshCommands();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetField(ref _errorMessage, value))
            {
                Notify(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

    public string? Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    public bool IsBusy
    {
        get => _busy;
        set => SetField(ref _busy, value);
    }

    public string? PlanSummary
    {
        get => _planSummary;
        set => SetField(ref _planSummary, value);
    }

    public bool NeedsChoice => _plan is BootstrapPlan.Decide;

    public bool CanFinish => _plan is BootstrapPlan.CheckoutRemote or BootstrapPlan.PublishFolder;

    public async Task ContinueAsync()
    {
        switch (_step)
        {
            case WizardStep.AccessToken:
                if (await StoreTokenAsync())
                {
                    MoveTo(WizardStep.Repository);
                }

                break;
            case WizardStep.Repository:
                if (_repoValidated)
                {
                    MoveTo(WizardStep.Folder);
                }

                break;
            case WizardStep.Folder:
                if (HasText(_folderPath))
                {
                    MoveTo(WizardStep.Preferences);
                }

                break;
            case WizardStep.Preferences:
                if (IdentityComplete())
                {
                    MoveTo(WizardStep.Bootstrap);
                    await AssessAsync().ConfigureAwait(true);
                }

                break;
        }
    }

    public async Task ValidateRepositoryAsync()
    {
        if (!SyncConfigValidation.IsAcceptableRemoteUrl(RemoteUrl.Trim()))
        {
            _repoValidated = false;
            ErrorMessage = UnacceptableUrlMessage;
            RefreshCommands();
            return;
        }

        IsBusy = true;
        Progress = "Checking the repository…";
        var remote = _remoteFactory(AssembleConfig());
        var outcome = await remote.ValidateRepository(CancellationToken.None).ConfigureAwait(true);
        IsBusy = false;
        Progress = null;
        switch (outcome)
        {
            case Result<RemoteValidation>.Ok probe when probe.Value.Reachable:
                _branch = probe.Value.DefaultBranch ?? FallbackBranch;
                _repoValidated = true;
                ErrorMessage = null;
                break;
            case Result<RemoteValidation>.Fail refusal:
                _repoValidated = false;
                ErrorMessage = refusal.Error.Message;
                break;
            default:
                _repoValidated = false;
                ErrorMessage = "repository did not answer the validation probe";
                break;
        }

        RefreshCommands();
    }

    public async Task BrowseFolderAsync()
    {
        var picked = await _folderPicker.PickAsync().ConfigureAwait(true);
        if (picked is { } folder)
        {
            FolderPath = folder;
        }
    }

    public async Task ChooseAsync(BootstrapDirection direction)
    {
        if (_plan is not BootstrapPlan.Decide decide)
        {
            return;
        }

        await BootstrapAsync(decide, direction).ConfigureAwait(true);
    }

    public async Task FinishAsync()
    {
        if (_plan is not (BootstrapPlan.CheckoutRemote or BootstrapPlan.PublishFolder))
        {
            return;
        }

        await BootstrapAsync(_plan, null).ConfigureAwait(true);
    }

    public async Task CancelAsync()
    {
        if (_tokenStored)
        {
            _tokenStored = false;
            await _credentials.Delete(CancellationToken.None).ConfigureAwait(true);
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> StoreTokenAsync()
    {
        var token = _accessToken.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        var stored = await _credentials.Write(new PersonalAccessToken(token), CancellationToken.None).ConfigureAwait(true);
        if (stored is Result<PersonalAccessToken>.Fail failure)
        {
            ErrorMessage = failure.Error.Message;
            return false;
        }

        _tokenStored = true;
        AccessToken = string.Empty;
        ErrorMessage = null;
        return true;
    }

    private async Task AssessAsync()
    {
        if (!SyncConfigValidation.IsAcceptableRemoteUrl(RemoteUrl.Trim()))
        {
            ErrorMessage = UnacceptableUrlMessage;
            return;
        }

        IsBusy = true;
        Progress = "Comparing folder and repository…";
        var bootstrapper = _bootstrapperFactory(AssembleConfig());
        var outcome = await bootstrapper.Assess(CancellationToken.None).ConfigureAwait(true);
        IsBusy = false;
        Progress = null;
        if (outcome is Result<BootstrapPlan>.Fail failure)
        {
            ErrorMessage = failure.Error.Message;
            return;
        }

        _plan = ((Result<BootstrapPlan>.Ok)outcome).Value;
        PlanSummary = Describe(_plan);
        ErrorMessage = null;
        NotifyAll();
    }

    private async Task BootstrapAsync(BootstrapPlan plan, BootstrapDirection? direction)
    {
        var config = AssembleConfig();
        if (!SyncConfigValidation.IsAcceptableRemoteUrl(config.RemoteUrl))
        {
            ErrorMessage = UnacceptableUrlMessage;
            return;
        }

        if (SyncConfigValidation.Validate(config) is { } problem)
        {
            ErrorMessage = problem.Message;
            return;
        }

        IsBusy = true;
        Progress = BootstrapProgress(direction);
        var saved = await _configStore.Save(config, CancellationToken.None).ConfigureAwait(true);
        if (saved is Result<SyncConfig>.Fail saveFailure)
        {
            IsBusy = false;
            Progress = null;
            ErrorMessage = saveFailure.Error.Message;
            return;
        }

        var bootstrapper = _bootstrapperFactory(config);
        var outcome = await bootstrapper.Execute(plan, direction, CancellationToken.None).ConfigureAwait(true);
        IsBusy = false;
        Progress = null;
        if (outcome is Result<BootstrapOutcome>.Fail executeFailure)
        {
            ErrorMessage = executeFailure.Error.Message;
            return;
        }

        ErrorMessage = null;
        _plan = null;
        PlanSummary = null;
        MoveTo(WizardStep.Completed);
    }

    private string Describe(BootstrapPlan plan) => plan switch
    {
        BootstrapPlan.CheckoutRemote =>
            $"The folder is empty and branch {_branch} exists on the remote. Its history will be checked out into the folder.",
        BootstrapPlan.PublishFolder =>
            $"The folder has files and branch {_branch} is empty on the remote. The folder will be published as the first commit.",
        BootstrapPlan.Decide decide =>
            $"{decide.Options.LocalFileCount} local files meet existing history on branch {decide.Options.Branch} ({Short(decide.Options.RemoteSha)}). Choose which side wins.",
        _ => "The folder and repository are ready to sync."
    };

    private static string BootstrapProgress(BootstrapDirection? direction) => direction switch
    {
        BootstrapDirection.RepoWins => "Checking out the remote history…",
        BootstrapDirection.FolderWins => "Publishing the folder to the repository…",
        _ => "Applying the bootstrap plan…"
    };

    private static string Short(string sha) => sha.Length <= 10 ? sha : sha[..10];

    private SyncConfig AssembleConfig() => new()
    {
        WatchedFolder = _folderPath.Trim(),
        RemoteUrl = _remoteUrl.Trim(),
        Branch = _branch,
        Mode = _gitMode ? SyncMode.Git : SyncMode.Plain,
        Identity = new CommitIdentity { Name = _identityName.Trim(), Email = _identityEmail.Trim() }
    };

    private bool ChoicePending() => NeedsChoice && !IsBusy;

    private bool CanLeaveCurrentStep() => _step switch
    {
        WizardStep.AccessToken => HasText(_accessToken),
        WizardStep.Repository => _repoValidated && !IsBusy,
        WizardStep.Folder => HasText(_folderPath),
        WizardStep.Preferences => IdentityComplete(),
        _ => false
    };

    private bool IdentityComplete() => HasText(_identityName) && HasText(_identityEmail);

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private void MoveTo(WizardStep step)
    {
        _step = step;
        NotifyAll();
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        foreach (var command in new[]
                 {
                     ContinueCommand,
                     ValidateRepositoryCommand,
                     BrowseFolderCommand,
                     ChooseRepoWinsCommand,
                     ChooseFolderWinsCommand,
                     FinishCommand,
                     CancelCommand,
                     CloseCommand
                 })
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Notify(name);
        return true;
    }
}
