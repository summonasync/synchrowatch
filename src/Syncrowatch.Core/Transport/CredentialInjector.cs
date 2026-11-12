using Syncrowatch.Core.Config;

namespace Syncrowatch.Core.Transport;

public sealed class CredentialInjector : IDisposable
{
    public const string UsernameVariable = "SYNCROWATCH_ASKPASS_USERNAME";
    public const string PasswordVariable = "SYNCROWATCH_ASKPASS_PASSWORD";

    private const string AskpassUsername = "x-access-token";

    private const string UnixScript = """
#!/bin/sh
case "$1" in
*Username*) printf '%s\n' "$SYNCROWATCH_ASKPASS_USERNAME" ;;
*) printf '%s\n' "$SYNCROWATCH_ASKPASS_PASSWORD" ;;
esac
""";

    private const string WindowsScript = """
@echo off
set "arg=%~1"
if "%arg:Username=%"=="%arg%" (echo %SYNCROWATCH_ASKPASS_PASSWORD%) else (echo %SYNCROWATCH_ASKPASS_USERNAME%)
""";

    private static readonly UnixFileMode AccessMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _directory;

    private CredentialInjector(string directory, string scriptPath, IReadOnlyDictionary<string, string> environment)
    {
        _directory = directory;
        Environment = environment;
        ScriptPath = scriptPath;
    }

    public string ScriptPath { get; }

    public IReadOnlyDictionary<string, string> Environment { get; }

    public static CredentialInjector Create(PersonalAccessToken token)
    {
        var directory = Path.Combine(Path.GetTempPath(), "syncrowatch-askpass-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, AccessMode);
        }
        var scriptPath = Path.Combine(directory, ScriptFileName());
        File.WriteAllText(scriptPath, ScriptText());
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptPath, AccessMode);
        }

        return new CredentialInjector(directory, scriptPath, BuildEnvironment(scriptPath, token));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string ScriptFileName() =>
        OperatingSystem.IsWindows() ? "askpass.cmd" : "askpass.sh";

    private static string ScriptText() =>
        OperatingSystem.IsWindows() ? WindowsScript : UnixScript;

    private static IReadOnlyDictionary<string, string> BuildEnvironment(string scriptPath, PersonalAccessToken token) =>
        new Dictionary<string, string>
        {
            ["GIT_ASKPASS"] = scriptPath,
            ["GIT_TERMINAL_PROMPT"] = "0",
            [UsernameVariable] = AskpassUsername,
            [PasswordVariable] = token.Value
        };
}
