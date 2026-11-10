using Shouldly;
using Xunit;

namespace Syncrowatch.Tests;

public class GitConfigSandboxTests
{
    [Fact]
    public async Task ConcurrentAcquisition_SerializesEnvironmentMutation()
    {
        var previous = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        var gate = new object();
        var active = 0;
        var peak = 0;

        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            using var guard = new GitConfigSandbox();
            var now = Interlocked.Increment(ref active);
            lock (gate)
            {
                peak = Math.Max(peak, now);
            }

            Thread.Sleep(15);
            Interlocked.Decrement(ref active);
        }));
        await Task.WhenAll(tasks);

        peak.ShouldBe(1);
        Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL").ShouldBe(previous);
    }

    [Fact]
    public void Dispose_RestoresPreviousEnvironmentValue()
    {
        var previous = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        string sandboxPath;
        var guard = new GitConfigSandbox();
        try
        {
            sandboxPath = Path.Combine(TempDir.Create(), "probe.gitconfig");
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", sandboxPath);
        }
        finally
        {
            guard.Dispose();
        }

        Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL").ShouldBe(previous);
    }
}
