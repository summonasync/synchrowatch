using System.ComponentModel;
using System.Diagnostics;

namespace Syncrowatch.Core.Transport;

public sealed class GitProcess : IGitRunner
{
    private const int MinimumSecretLength = 16;

    private readonly string _fileName;

    public GitProcess(string fileName = "git") => _fileName = fileName;

    public async Task<GitCommandResult> RunAsync(GitInvocation invocation, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(invocation.Timeout);

        using var process = Start(invocation);
        var stdout = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await Drain(stdout);
            await Drain(stderr);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new GitTimeoutException(invocation.Timeout);
        }

        return Redact(new GitCommandResult(process.ExitCode, await stdout, await stderr), invocation.Environment);
    }

    private Process Start(GitInvocation invocation)
    {
        var startInfo = new ProcessStartInfo(_fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (invocation.WorkingDirectory is { } directory)
        {
            startInfo.WorkingDirectory = directory;
        }

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in invocation.Environment)
        {
            startInfo.Environment[key] = value;
        }

        try
        {
            return Process.Start(startInfo)!;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            throw new GitMissingException(ex);
        }
    }

    private static async Task Drain(Task<string> pending)
    {
        try
        {
            await pending;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
    }

    private static GitCommandResult Redact(GitCommandResult result, IReadOnlyDictionary<string, string> environment)
    {
        var stdout = result.StandardOutput;
        var stderr = result.StandardError;
        foreach (var value in environment.Values)
        {
            if (value.Length < MinimumSecretLength)
            {
                continue;
            }

            stdout = stdout.Replace(value, "***");
            stderr = stderr.Replace(value, "***");
        }

        return result with { StandardOutput = stdout, StandardError = stderr };
    }
}
