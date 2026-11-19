using Shouldly;
using Synchro.Core;
using Syncrowatch.Core.Config;
using Xunit;

namespace Syncrowatch.Tests;

public class AppPreferencesStoreTests
{
    [Fact]
    public async Task Save_Load_RoundTripsLaunchAtLogin()
    {
        var dir = TempDir.Create();
        try
        {
            var store = new JsonAppPreferencesStore(dir);

            await store.Save(new AppPreferences { LaunchAtLogin = true }, CancellationToken.None);
            var outcome = await store.Load(CancellationToken.None);

            outcome.ShouldBeOfType<Result<AppPreferences>.Ok>().Value.LaunchAtLogin.ShouldBeTrue();
            File.Exists(store.FilePath + ".tmp").ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Load_WithoutPreferencesFile_ReturnsDefaults()
    {
        var dir = TempDir.Create();
        try
        {
            var outcome = await new JsonAppPreferencesStore(dir).Load(CancellationToken.None);

            outcome.ShouldBeOfType<Result<AppPreferences>.Ok>().Value.ShouldBe(new AppPreferences());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Load_CorruptJson_ReturnsDefaults()
    {
        var dir = TempDir.Create();
        try
        {
            await File.WriteAllTextAsync(SyncrowatchPaths.PreferencesFile(dir), "}{ not json");

            var outcome = await new JsonAppPreferencesStore(dir).Load(CancellationToken.None);

            outcome.ShouldBeOfType<Result<AppPreferences>.Ok>().Value.LaunchAtLogin.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Save_Twice_LastWriteWins()
    {
        var dir = TempDir.Create();
        try
        {
            var store = new JsonAppPreferencesStore(dir);

            await store.Save(new AppPreferences { LaunchAtLogin = true }, CancellationToken.None);
            await store.Save(new AppPreferences { LaunchAtLogin = false }, CancellationToken.None);
            var outcome = await store.Load(CancellationToken.None);

            outcome.ShouldBeOfType<Result<AppPreferences>.Ok>().Value.LaunchAtLogin.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void PreferencesFile_ResolvesInsideUserDirectory()
    {
        SyncrowatchPaths.PreferencesFile().ShouldEndWith(Path.Combine("syncrowatch", SyncrowatchPaths.PreferencesFileName));

        var dir = Path.Combine(Path.GetTempPath(), "prefs-probe");
        SyncrowatchPaths.PreferencesFile(dir).ShouldBe(Path.Combine(dir, SyncrowatchPaths.PreferencesFileName));
    }
}
