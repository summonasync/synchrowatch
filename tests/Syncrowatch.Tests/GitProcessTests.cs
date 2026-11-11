using Shouldly;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public class GitProcessTests
{
    [Fact]
    public async Task RunAsync_VersionCommand_CapturesExitCodeAndOutput()
    {
        var result = await new GitProcess().RunAsync(
            new GitInvocation { Arguments = ["--version"] }, CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        result.StandardOutput.ShouldContain("git version");
        result.StandardError.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_FailingCommand_SurfacesStderrAndExitCode()
    {
        var dir = TempDir.Create();
        try
        {
            var result = await new GitProcess().RunAsync(
                new GitInvocation { Arguments = ["-C", dir, "status"] }, CancellationToken.None);

            result.Succeeded.ShouldBeFalse();
            result.StandardError.ShouldNotBeEmpty();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task RunAsync_MissingExecutable_ThrowsTypedError()
    {
        var runner = new GitProcess("syncrowatch-missing-git-stub");

        await Should.ThrowAsync<GitMissingException>(() => runner.RunAsync(
            new GitInvocation { Arguments = ["--version"] }, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_TimeoutElapses_KillsProcessAndThrowsTimeout()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var stubPath = WriteStub("#!/bin/sh\nsleep 30\n");
        try
        {
            var runner = new GitProcess(stubPath);
            var invocation = new GitInvocation
            {
                Arguments = ["--version"],
                Timeout = TimeSpan.FromMilliseconds(400)
            };

            await Should.ThrowAsync<GitTimeoutException>(() => runner.RunAsync(invocation, CancellationToken.None));
        }
        finally
        {
            File.Delete(stubPath);
        }
    }

    [Fact]
    public async Task RunAsync_CallerCancels_KillsProcessAndRethrows()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var stubPath = WriteStub("#!/bin/sh\nsleep 30\n");
        try
        {
            using var cancellation = new CancellationTokenSource(200);
            var runner = new GitProcess(stubPath);

            await Should.ThrowAsync<OperationCanceledException>(() => runner.RunAsync(
                new GitInvocation { Arguments = ["--version"] }, cancellation.Token));
        }
        finally
        {
            File.Delete(stubPath);
        }
    }

    [Fact]
    public async Task RunAsync_SecretEchoedIntoStreams_IsRedacted()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var stubPath = WriteStub("""
#!/bin/sh
printf '%s\n' "$SYNCROWATCH_ASKPASS_PASSWORD"
echo "stub-diagnostic" >&2
""");
        try
        {
            var runner = new GitProcess(stubPath);
            var invocation = new GitInvocation
            {
                Arguments = ["--version"],
                Environment = new Dictionary<string, string>
                {
                    [CredentialInjector.PasswordVariable] = "ghp_redact_probe_123"
                }
            };
            var result = await RunUntilExecutableAsync(runner, invocation);

            result.StandardOutput.ShouldNotContain("ghp_redact_probe_123");
            result.StandardOutput.ShouldContain("***");
            result.StandardError.ShouldContain("stub-diagnostic");
        }
        finally
        {
            File.Delete(stubPath);
        }
    }

    private static async Task<GitCommandResult> RunUntilExecutableAsync(GitProcess runner, GitInvocation invocation)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await runner.RunAsync(invocation, CancellationToken.None);
            }
            catch (GitMissingException missing) when (attempt < 5 && missing.InnerException?.Message.Contains("Text file busy") == true)
            {
                await Task.Delay(50 * attempt);
            }
        }
    }

    private static string WriteStub(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), "syncrowatch-stub-git-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, body);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}
