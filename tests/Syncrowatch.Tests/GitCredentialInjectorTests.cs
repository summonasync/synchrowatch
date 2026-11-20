using System.Diagnostics;
using Shouldly;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public class GitCredentialInjectorTests
{
    private const string TokenValue = "ghp_injector_probe_7a6b5c4d";

    [Fact]
    public void Create_ShimOnDisk_WithoutSecretInside()
    {
        using var injector = CredentialInjector.Create(new PersonalAccessToken(TokenValue));

        var scriptPath = injector.Environment["GIT_ASKPASS"];
        File.Exists(scriptPath).ShouldBeTrue();
        File.ReadAllText(scriptPath).ShouldNotContain(TokenValue);
        injector.Environment[CredentialInjector.PasswordVariable].ShouldBe(TokenValue);
    }

    [Fact]
    public void Dispose_RemovesShimDirectoryCompletely()
    {
        string directory;
        string scriptPath;
        using (var injector = CredentialInjector.Create(new PersonalAccessToken(TokenValue)))
        {
            scriptPath = injector.Environment["GIT_ASKPASS"];
            directory = Path.GetDirectoryName(scriptPath)!;
        }

        File.Exists(scriptPath).ShouldBeFalse();
        Directory.Exists(directory).ShouldBeFalse();
    }

    [Fact]
    public void Create_PromptDisabledInEnvironment()
    {
        using var injector = CredentialInjector.Create(new PersonalAccessToken(TokenValue));

        injector.Environment["GIT_TERMINAL_PROMPT"].ShouldBe("0");
        injector.Environment[CredentialInjector.UsernameVariable].ShouldNotBeEmpty();
    }

    [Fact]
    public void Create_OnLinux_ShimIsOwnerOnly()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var injector = CredentialInjector.Create(new PersonalAccessToken(TokenValue));

        File.GetUnixFileMode(injector.Environment["GIT_ASKPASS"])
            .ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public void Shim_AskedByGit_AnswersUsernameAndPasswordPrompts()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using var injector = CredentialInjector.Create(new PersonalAccessToken(TokenValue));

        AskShim(injector, "Username for 'https://github.com': ")
            .ShouldBe(injector.Environment[CredentialInjector.UsernameVariable]);
        AskShim(injector, "Password for 'https://x-access-token@github.com': ")
            .ShouldBe(TokenValue);
    }

    private static string AskShim(CredentialInjector injector, string prompt)
    {
        var psi = new ProcessStartInfo(injector.Environment["GIT_ASKPASS"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add(prompt);
        foreach (var (key, value) in injector.Environment)
        {
            psi.Environment[key] = value;
        }

        using var process = Process.Start(psi)!;
        var answer = process.StandardOutput.ReadToEnd().TrimEnd('\n');
        process.WaitForExit(5_000);
        process.ExitCode.ShouldBe(0);
        return answer;
    }
}
