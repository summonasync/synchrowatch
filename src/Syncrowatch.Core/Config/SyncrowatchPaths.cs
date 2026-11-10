namespace Syncrowatch.Core.Config;

public static class SyncrowatchPaths
{
    public const string SettingsFileName = "settings.json";
    public const string CredentialFileName = "github.pat";

    public static string UserDataDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "syncrowatch");

    public static string SettingsFile(string? directory = null) =>
        Path.Combine(directory ?? UserDataDirectory(), SettingsFileName);

    public static string CredentialFile(string? directory = null) =>
        Path.Combine(directory ?? UserDataDirectory(), CredentialFileName);
}
