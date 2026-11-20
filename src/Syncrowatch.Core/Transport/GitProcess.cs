using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Syncrowatch.Core.Transport;

public sealed class GitProcess : IGitRunner
{
    private const int MinimumSecretLength = 8;

    private readonly string _fileName;

    public GitProcess(string fileName = "git") => _fileName = fileName;

    public async Task<GitCommandResult> RunAsync(GitInvocation invocation, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(invocation.Timeout);

        using var process = Start(invocation);
        var stdout = ReadAllAsync(process.StandardOutput.BaseStream, timeoutSource.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
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

        return Redact(
            new GitCommandResult(process.ExitCode, Encoding.UTF8.GetString(await stdout.ConfigureAwait(false)), await stderr.ConfigureAwait(false))
            {
                OutputBytes = await stdout.ConfigureAwait(false)
            },
            invocation.Environment);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private Process Start(GitInvocation invocation)
    {
        var startInfo = new ProcessStartInfo(_fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
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
        catch (Win32Exception ex)
        {
            throw new GitMissingException(ex);
        }
    }

    private static async Task Drain(Task pending)
    {
        try
        {
            await pending.ConfigureAwait(false);
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
