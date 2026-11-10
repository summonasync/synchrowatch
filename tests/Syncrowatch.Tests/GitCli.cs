using System.Diagnostics;

namespace Syncrowatch.Tests;

public static class GitCli
{
    public static (int ExitCode, string Output) Run(string workingDirectory, params string[] arguments)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(10_000);
        return (process.ExitCode, output);
    }

    public static void Ensure(string workingDirectory, params string[] arguments)
    {
        var (exitCode, output) = Run(workingDirectory, arguments);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {output}");
        }
    }

    public static void ConfigureIdentity(string workingDirectory)
    {
        Ensure(workingDirectory, "config", "user.name", "Test Driver");
        Ensure(workingDirectory, "config", "user.email", "driver@test.local");
    }

    public static void CommitAll(string workingDirectory, string message)
    {
        Ensure(workingDirectory, "add", "-A");
        Ensure(workingDirectory, "commit", "-m", message);
    }

    public static void Push(string workingDirectory, string remote, string branch) =>
        Ensure(workingDirectory, "push", remote, branch);

    public static string Head(string workingDirectory) =>
        Run(workingDirectory, "rev-parse", "HEAD").Output.Trim();
}
