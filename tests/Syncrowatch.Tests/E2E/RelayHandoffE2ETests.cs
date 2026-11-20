using Shouldly;
using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Relay;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests.E2E;

[Collection("git-config-sandbox")]
public sealed class RelayHandoffE2ETests : IDisposable
{
    private readonly SyncE2EStand _stand = new();
    private readonly FakeTimeProvider _time = new();
    private readonly string _builder;
    private readonly string _seenDirectory;

    public RelayHandoffE2ETests()
    {
        _builder = Path.Combine(_stand.Root, "relay-drop");
        _seenDirectory = Path.Combine(_stand.Root, "seen");
        GitCli.Ensure(_stand.Root, "init", "-b", "relay/synchr", _builder);
        GitCli.ConfigureIdentity(_builder);
    }

    [Fact]
    public async Task MixedHostileAndValidPackages_WarnSkipDetectWithSafeCommand_ThenDeduplicate()
    {
        await _stand.BindAsync(_stand.CloneA);
        Drop("with space", "pack.synchr", "one");
        Drop("$(id)", "pack.synchr", "two");
        Drop("back`tick", "pack.synchr", "three");
        Drop("проект", "pack.synchr", "four");
        Drop("alpha", "pa\"ck.synchr", "five");
        Drop("alpha", "good-pack.synchr", "six sealed bytes");

        using var driver = BuildDriver();
        driver.Start();
        _time.Advance(SyncConfig.DefaultPollInterval);
        await SyncE2EStand.AwaitAsync(
            async () => _stand.SnapshotNotifications().OfType<SyncNotification.RelayPackageDetected>().Any(),
            "relay package detection");

        var detection = _stand.SnapshotNotifications().OfType<SyncNotification.RelayPackageDetected>()
            .ShouldHaveSingleItem();
        detection.Project.ShouldBe("alpha");
        detection.PackageFile.ShouldBe("good-pack.synchr");
        detection.CliCommand.ShouldBe("synchro apply \"alpha/good-pack.synchr\" --peer \"Syncrowatch\"");
        detection.CliCommand.ShouldMatch(@"^[A-Za-z0-9._\- /""]+$");
        _stand.SnapshotNotifications().OfType<SyncNotification.RelayEntriesSkipped>()
            .ShouldHaveSingleItem().Count.ShouldBe(5);
        SeenEntries().ShouldHaveSingleItem().ShouldContain("alpha/good-pack.synchr");

        var baseline = _stand.SnapshotNotifications().Count;
        await AwaitQuietAsync(baseline);

        _stand.SnapshotNotifications().Count.ShouldBe(baseline);
        _stand.SnapshotNotifications().OfType<SyncNotification.RelayPackageDetected>().ShouldHaveSingleItem();

        Drop("alpha", "second-pack.synchr", "more sealed bytes");
        _time.Advance(SyncConfig.DefaultPollInterval);
        await SyncE2EStand.AwaitAsync(
            async () => _stand.SnapshotNotifications().OfType<SyncNotification.RelayPackageDetected>().Count() == 2,
            "second relay package detection");

        _stand.SnapshotNotifications().OfType<SyncNotification.RelayPackageDetected>().Last()
            .PackageFile.ShouldBe("second-pack.synchr");
        SeenEntries().Count.ShouldBe(2);
    }

    [Fact]
    public async Task CorruptedSeenState_QuarantinesToBackupWithSingleWarning_AndStillDetectsFreshPackages()
    {
        await _stand.BindAsync(_stand.CloneA);
        Directory.CreateDirectory(_seenDirectory);
        File.WriteAllText(Path.Combine(_seenDirectory, RelaySeenStateStore.FileName), "{ broken");
        Drop("alpha", "fix-pack.synchr", "sealed bytes");

        using var driver = BuildDriver();
        driver.Start();
        _time.Advance(SyncConfig.DefaultPollInterval);
        await SyncE2EStand.AwaitAsync(
            async () => _stand.SnapshotNotifications().OfType<SyncNotification.RelayPackageDetected>().Any(),
            "detection after seen-state recovery");

        _stand.SnapshotNotifications().OfType<SyncNotification.RelayStateCorrupted>().ShouldHaveSingleItem();
        File.Exists(Path.Combine(_seenDirectory, RelaySeenStateStore.FileName + ".bak")).ShouldBeTrue();
        SeenEntries().ShouldHaveSingleItem().ShouldContain("alpha/fix-pack.synchr");

        var baseline = _stand.SnapshotNotifications().Count;
        await AwaitQuietAsync(baseline);

        _stand.SnapshotNotifications().OfType<SyncNotification.RelayStateCorrupted>().ShouldHaveSingleItem();
        _stand.SnapshotNotifications().OfType<SyncNotification.RelayPackageDetected>().ShouldHaveSingleItem();
    }

    private RelayPollDriver BuildDriver() => new(
        new JsonSyncConfigStore(_stand.DataDirectory),
        config => new RelayWatcher(
            config,
            new GitHubRepository(config, new CredentialStore(_stand.DataDirectory), new GitProcess()),
            new GitProcess(),
            new RelaySeenStateStore(_seenDirectory),
            _stand.Bus,
            _time),
        _stand.Bus,
        _time);

    private void Drop(string project, string packageFile, string content)
    {
        var folder = Path.Combine(_builder, project);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, packageFile), content);
        GitCli.CommitAll(_builder, $"relay {project}/{packageFile}");
        GitCli.Ensure(_builder, "push", _stand.Bare, "relay/synchr");
    }

    private IReadOnlyList<string> SeenEntries() =>
        File.ReadAllLines(Path.Combine(_seenDirectory, RelaySeenStateStore.FileName))
            .Where(line => line.TrimStart().StartsWith('"') && line.Contains('/'))
            .ToList();

    private async Task AwaitQuietAsync(int baseline)
    {
        var quietSince = DateTime.UtcNow;
        while (DateTime.UtcNow - quietSince < TimeSpan.FromMilliseconds(600))
        {
            await Task.Delay(100);
            var current = _stand.SnapshotNotifications().Count;
            if (current != baseline)
            {
                baseline = current;
                quietSince = DateTime.UtcNow;
            }
        }
    }

    public void Dispose() => _stand.Dispose();
}
