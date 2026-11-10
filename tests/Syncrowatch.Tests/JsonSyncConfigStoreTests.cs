using Shouldly;
using Synchro.Core;
using Syncrowatch.Core.Config;
using Xunit;

namespace Syncrowatch.Tests;

public class JsonSyncConfigStoreTests
{
    private static readonly SyncConfig Config = new()
    {
        WatchedFolder = "/home/peer/notes",
        RemoteUrl = "https://github.com/peer/notes.git",
        Branch = "main",
        Mode = Synchro.Core.SyncMode.Git,
        Debounce = TimeSpan.FromSeconds(17),
        PollInterval = TimeSpan.FromSeconds(90),
        Identity = new CommitIdentity { Name = "Peer", Email = "peer@users.noreply.github.com" }
    };

    [Fact]
    public async Task Save_Load_RoundTripsEveryField()
    {
        var dir = TempDir.Create();
        try
        {
            var store = new JsonSyncConfigStore(dir);

            await store.Save(Config, CancellationToken.None);
            var outcome = await store.Load(CancellationToken.None);

            outcome.ShouldBeOfType<Result<SyncConfig>.Ok>().Value.ShouldBe(Config);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Load_MinimalFile_AppliesDefaults()
    {
        var dir = TempDir.Create();
        try
        {
            await File.WriteAllTextAsync(
                SyncrowatchPaths.SettingsFile(dir),
                """
                {
                  "WatchedFolder": "/home/peer/notes",
                  "RemoteUrl": "https://github.com/peer/notes.git",
                  "Branch": "main",
                  "Mode": "Git"
                }
                """);

            var outcome = await new JsonSyncConfigStore(dir).Load(CancellationToken.None);

            var ok = outcome.ShouldBeOfType<Result<SyncConfig>.Ok>();
            ok.Value.Debounce.ShouldBeGreaterThanOrEqualTo(SyncConfig.MinDebounce);
            ok.Value.Debounce.ShouldBeLessThanOrEqualTo(SyncConfig.MaxDebounce);
            ok.Value.PollInterval.ShouldBe(SyncConfig.DefaultPollInterval);
            ok.Value.Identity.ShouldBe(CommitIdentity.Default);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Load_WithoutSettingsFile_ReturnsNotFound()
    {
        var dir = TempDir.Create();
        try
        {
            var outcome = await new JsonSyncConfigStore(dir).Load(CancellationToken.None);

            var fail = outcome.ShouldBeOfType<Result<SyncConfig>.Fail>();
            fail.Error.Code.ShouldBe("not_found");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Load_CorruptJson_ReturnsInvalid()
    {
        var dir = TempDir.Create();
        try
        {
            await File.WriteAllTextAsync(SyncrowatchPaths.SettingsFile(dir), "}{ not json");

            var outcome = await new JsonSyncConfigStore(dir).Load(CancellationToken.None);

            var fail = outcome.ShouldBeOfType<Result<SyncConfig>.Fail>();
            fail.Error.Code.ShouldBe("invalid");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task SettingsFile_NeverContainsStoredToken()
    {
        var dir = TempDir.Create();
        try
        {
            const string token = "ghp_settingsleakprobe_5a4b3c2d";

            await new CredentialStore(dir).Write(new PersonalAccessToken(token), CancellationToken.None);
            await new JsonSyncConfigStore(dir).Save(Config, CancellationToken.None);

            var settingsJson = await File.ReadAllTextAsync(SyncrowatchPaths.SettingsFile(dir));
            settingsJson.ShouldNotContain(token);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
