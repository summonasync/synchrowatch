using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Syncrowatch.Core;
using Syncrowatch.App.Wizard;
using Syncrowatch.Core.Autostart;
using Syncrowatch.Core.Config;

namespace Syncrowatch.App.Settings;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    public const string MaskedToken = "******";
    private const string MissingToken = "not stored";

    private readonly ISyncConfigStore _configStore;
    private readonly ICredentialStore _credentials;
    private readonly IAppPreferencesStore _preferences;
    private readonly IAutostartManager _autostart;

    private string _watchedFolder = string.Empty;
    private string _remoteUrl = string.Empty;
    private string _branch = string.Empty;
    private string _runMode = string.Empty;
    private string _debounce = string.Empty;
    private string _pollInterval = string.Empty;
    private string _identityName = string.Empty;
    private string _identityEmail = string.Empty;
    private string _token = MissingToken;
    private string? _errorMessage;
    private bool _launchAtLogin;

    public SettingsViewModel(
        ISyncConfigStore configStore,
        ICredentialStore credentials,
        IAppPreferencesStore preferences,
        IAutostartManager autostart)
    {
        _configStore = configStore;
        _credentials = credentials;
        _preferences = preferences;
        _autostart = autostart;
        RunWizardCommand = new AsyncCommand(() =>
        {
            RunWizardRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });
        ToggleLaunchAtLoginCommand = new AsyncCommand(() => ToggleLaunchAtLoginAsync(CancellationToken.None));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? RunWizardRequested;

    public AsyncCommand RunWizardCommand { get; }

    public AsyncCommand ToggleLaunchAtLoginCommand { get; }

    public string WatchedFolder => _watchedFolder;

    public string RemoteUrl => _remoteUrl;

    public string Branch => _branch;

    public string RunMode => _runMode;

    public string Debounce => _debounce;

    public string PollInterval => _pollInterval;

    public string IdentityName => _identityName;

    public string IdentityEmail => _identityEmail;

    public string Token => _token;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                Notify(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

    public bool LaunchAtLogin
    {
        get => _launchAtLogin;
        private set => SetField(ref _launchAtLogin, value);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _configStore.Load(cancellationToken).ConfigureAwait(true);
        if (loaded is not Result<SyncConfig>.Ok ok)
        {
            ErrorMessage = loaded is Result<SyncConfig>.Fail failure
                ? failure.Error.Message
                : "settings could not be read";
            return;
        }

        Map(ok.Value);
        var token = await _credentials.Read(cancellationToken).ConfigureAwait(true);
        SetField(ref _token, token is Result<PersonalAccessToken>.Ok ? MaskedToken : MissingToken, nameof(Token));
        var stored = await _preferences.Load(cancellationToken).ConfigureAwait(true);
        if (stored is Result<AppPreferences>.Fail preferenceFailure)
        {
            ErrorMessage = preferenceFailure.Error.Message;
            return;
        }

        if (stored is Result<AppPreferences>.Ok saved)
        {
            LaunchAtLogin = saved.Value.LaunchAtLogin;
        }
    }

    public async Task ToggleLaunchAtLoginAsync(CancellationToken cancellationToken = default)
    {
        ErrorMessage = null;
        var enable = !_launchAtLogin;
        var applied = enable ? EnableAutostart() : _autostart.Disable();
        if (applied is not Result<Unit>.Ok)
        {
            ErrorMessage = applied is Result<Unit>.Fail failure
                ? failure.Error.Message
                : "autostart entry could not be updated";
            Notify(nameof(LaunchAtLogin));
            return;
        }

        var saved = await _preferences.Save(new AppPreferences { LaunchAtLogin = enable }, cancellationToken)
            .ConfigureAwait(true);
        if (saved is not Result<AppPreferences>.Ok)
        {
            ErrorMessage = saved is Result<AppPreferences>.Fail saveFailure
                ? saveFailure.Error.Message
                : "preference could not be saved";
            Notify(nameof(LaunchAtLogin));
            return;
        }

        LaunchAtLogin = enable;
    }

    private Result<Unit> EnableAutostart()
    {
        var exePath = Environment.ProcessPath;
        return exePath is null
            ? new Result<Unit>.Fail(new SyncError("unavailable", "application executable path is unavailable"))
            : _autostart.Enable(exePath);
    }

    private void Map(SyncConfig config)
    {
        _watchedFolder = config.WatchedFolder;
        _remoteUrl = config.RemoteUrl;
        _branch = config.Branch;
        _runMode = Describe(config.Mode);
        _debounce = DescribeDuration(config.Debounce);
        _pollInterval = DescribeDuration(config.PollInterval);
        _identityName = config.Identity.Name;
        _identityEmail = config.Identity.Email;
        ErrorMessage = null;
        NotifyAll();
    }

    private static string Describe(SyncRunMode mode) => mode switch
    {
        SyncRunMode.Auto => "Auto",
        SyncRunMode.Manual => "Manual",
        SyncRunMode.SemiAuto => "Semi-auto",
        _ => mode.ToString()
    };

    private static string DescribeDuration(TimeSpan duration) => $"{duration.TotalSeconds:0} seconds";

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
