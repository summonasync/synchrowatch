namespace Syncrowatch.Core.Config;

public static class SyncrowatchPaths
{
    public const string SettingsFileName = "settings.json";
    public const string CredentialFileName = "github.pat";
    public const string HistoryFileName = "history.json";
    public const string ConflictFileName = "conflicts.json";
    public const string PreferencesFileName = "preferences.json";

    public static string UserDataDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "syncrowatch");

    public static string SettingsFile(string? directory = null) =>
        Path.Combine(directory ?? UserDataDirectory(), SettingsFileName);

    public static string CredentialFile(string? directory = null) =>
        Path.Combine(directory ?? UserDataDirectory(), CredentialFileName);

    public static string HistoryFile(string? directory = null) =>
        Path.Combine(directory ?? UserDataDirectory(), HistoryFileName);

    public static string ConflictStateFile(string? directory = null) =>
        Path.Combine(directory ?? UserDataDirectory(), ConflictFileName);

    public static string PreferencesFile(string? directory = null) =>
        Path.Combine(directory ?? UserDataDirectory(), PreferencesFileName);
}
