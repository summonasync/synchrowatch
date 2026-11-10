using Shouldly;

namespace Syncrowatch.Tests;

public sealed class GitConfigSandbox : IDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static int _activeGuards;

    private readonly string _globalPath;
    private readonly string? _before;
    private readonly string? _previousValue;
    private readonly string _sandboxPath;

    public static int ActiveGuards => _activeGuards;

    public GitConfigSandbox()
    {
        Gate.Wait();
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _globalPath = Path.Combine(home, ".gitconfig");
            _before = File.Exists(_globalPath) ? File.ReadAllText(_globalPath) : null;
            _previousValue = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
            _sandboxPath = Path.Combine(TempDir.Create(), "global.gitconfig");
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", _sandboxPath);
            Interlocked.Increment(ref _activeGuards);
        }
        catch
        {
            Gate.Release();
            throw;
        }
    }

    public void AssertRealGlobalConfigUnchanged()
    {
        var after = File.Exists(_globalPath) ? File.ReadAllText(_globalPath) : null;
        after.ShouldBe(_before);
        File.Exists(_sandboxPath).ShouldBeFalse();
    }

    public void Dispose()
    {
        try
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", _previousValue);
            Directory.Delete(Path.GetDirectoryName(_sandboxPath)!, true);
        }
        finally
        {
            Interlocked.Decrement(ref _activeGuards);
            Gate.Release();
        }
    }
}
