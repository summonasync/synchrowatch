using NSubstitute;
using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Relay;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class RelayWatcherTests : IDisposable
{
    private readonly string _root = TempDir.Create();

    [Fact]
    public async Task Poll_DroppedPackage_PublishesDetectionWithCliCommand()
    {
        var harness = CreateHarness();
        harness.DropPackage("alpha", "fix-pack.synchr", "sealed bytes");

        var outcome = await harness.Watcher(harness.Bus, "Pavel").Poll(CancellationToken.None);

        var packages = outcome.ShouldBeOfType<Result<IReadOnlyList<RelayPackage>>.Ok>().Value;
        packages.Count.ShouldBe(1);
        var package = packages[0];
        package.Project.ShouldBe("alpha");
        package.PackageFile.ShouldBe("fix-pack.synchr");
        package.RelayPath.ShouldBe("alpha/fix-pack.synchr");
        package.BlobSha.ShouldNotBeEmpty();
        package.CliCommand.ShouldBe("synchro apply \"alpha/fix-pack.synchr\" --peer \"Pavel\"");

        var detection = harness.Notifications.OfType<SyncNotification.RelayPackageDetected>().ShouldHaveSingleItem();
        detection.Project.ShouldBe("alpha");
        detection.PackageFile.ShouldBe("fix-pack.synchr");
        detection.CliCommand.ShouldBe(package.CliCommand);
    }

    [Fact]
    public async Task Poll_SameWatcherTwice_DoesNotRepublishSeenPackages()
    {
        var harness = CreateHarness();
        harness.DropPackage("alpha", "fix-pack.synchr", "sealed bytes");
        var watcher = harness.Watcher(harness.Bus, "Pavel");

        (await watcher.Poll(CancellationToken.None)).ShouldBeOfType<Result<IReadOnlyList<RelayPackage>>.Ok>();
        harness.Notifications.Clear();

        var second = await watcher.Poll(CancellationToken.None);

        second.ShouldBeOfType<Result<IReadOnlyList<RelayPackage>>.Ok>().Value.ShouldBeEmpty();
        harness.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Poll_FreshWatcherAfterRestart_SeenStateSurvivesInStore()
    {
        var harness = CreateHarness();
        harness.DropPackage("alpha", "fix-pack.synchr", "sealed bytes");
        (await harness.Watcher(harness.Bus, "Pavel").Poll(CancellationToken.None))
            .ShouldBeOfType<Result<IReadOnlyList<RelayPackage>>.Ok>();
        harness.Notifications.Clear();

        var outcome = await harness.Watcher(harness.Bus, "Pavel").Poll(CancellationToken.None);

        outcome.ShouldBeOfType<Result<IReadOnlyList<RelayPackage>>.Ok>().Value.ShouldBeEmpty();
        harness.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Poll_AdditionalPackage_DetectsOnlyTheNewBlob()
    {
        var harness = CreateHarness();
        harness.DropPackage("alpha", "fix-pack.synchr", "sealed bytes");
        var watcher = harness.Watcher(harness.Bus, "Pavel");
        (await watcher.Poll(CancellationToken.None)).ShouldBeOfType<Result<IReadOnlyList<RelayPackage>>.Ok>();
        harness.Notifications.Clear();
        harness.DropPackage("alpha", "second-pack.synchr", "more sealed bytes");

        var outcome = await watcher.Poll(CancellationToken.None);

        var packages = outcome.ShouldBeOfType<Result<IReadOnlyList<RelayPackage>>.Ok>().Value;
        packages.Select(package => package.PackageFile).ShouldBe(["second-pack.synchr"]);
        harness.Notifications.OfType<SyncNotification.RelayPackageDetected>().Select(detection => detection.PackageFile)
            .ShouldBe(["second-pack.synchr"]);
    }

    [Fact]
    public async Task Poll_RepackedPackage_NewBlobShaTriggersFreshDetection()
    {
        var harness = CreateHarness();
        harness.DropPackage("alpha", "fix-pack.synchr", "sealed bytes");
        var watcher = harness.Watcher(harness.Bus, "Pavel");
        (await watcher.Poll(CancellationToken.None)).ShouldBeOfType<Result<IReadOnlyList<RelayPackage>>.Ok>();
        harness.DropPackage("alpha", "fix-pack.synchr", "resealed bytes");

        var outcome = await watcher.Poll(CancellationToken.None);

        outcome.ShouldBeOfType<Result<IReadOnlyList<RelayPackage>>.Ok>().Value
            .Select(package => package.PackageFile).ShouldBe(["fix-pack.synchr"]);
    }

    [Fact]
    public async Task Poll_WithoutRelayBranch_ReturnsEmptyWithoutFetchOrEvents()
    {
        var harness = CreateHarness();

        var outcome = await harness.Watcher(harness.Bus, "Pavel").Poll(CancellationToken.None);

        outcome.ShouldBeOfType<Result<IReadOnlyList<RelayPackage>>.Ok>().Value.ShouldBeEmpty();
        harness.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Poll_UnreachableRemote_ReturnsFailure()
    {
        var harness = CreateHarness();
        var unreachable = Path.Combine(_root, "origin-vanished.git");

        var outcome = await harness.Watcher(harness.Bus, "Pavel", unreachable).Poll(CancellationToken.None);

        outcome.ShouldBeOfType<Result<IReadOnlyList<RelayPackage>>.Fail>().Error.Code.ShouldNotBeEmpty();
    }

    [Fact]
    public void RelayBranchName_MatchesImplementedBranch()
    {
        IRelayWatcher.RelayBranchName.ShouldBe("relay/synchr");
    }

    private Harness CreateHarness() => new(_root, "origin.git");

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class Harness
    {
        public Harness(string root, string bareName)
        {
            BarePath = Path.Combine(root, bareName);
            Clone = Path.Combine(root, "clone");
            Builder = Path.Combine(root, "relay-drop");
            SeenDirectory = Path.Combine(root, "seen");

            GitCli.Ensure(root, "init", "--bare", "-b", "main", BarePath);
            GitCli.Ensure(root, "clone", BarePath, Clone);
            GitCli.ConfigureIdentity(Clone);
            File.WriteAllText(Path.Combine(Clone, "shared.txt"), "alpha\nmiddle\nomega\n");
            GitCli.CommitAll(Clone, "base");
            GitCli.Push(Clone, BarePath, "main");
            GitCli.Ensure(root, "init", "-b", IRelayWatcher.RelayBranchName, Builder);
            GitCli.ConfigureIdentity(Builder);

            Bus.Received += Notifications.Add;
        }

        public SyncEventBus Bus { get; } = new();

        public List<SyncNotification> Notifications { get; } = [];

        public string BarePath { get; }

        public string Clone { get; }

        public string Builder { get; }

        public string SeenDirectory { get; }

        public void DropPackage(string project, string packageFile, string content)
        {
            var folder = Path.Combine(Builder, project);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, packageFile), content);
            GitCli.CommitAll(Builder, $"relay {project}/{packageFile}");
            GitCli.Ensure(Builder, "push", BarePath, IRelayWatcher.RelayBranchName);
        }

        public RelayWatcher Watcher(SyncEventBus bus, string peerName, string? remoteUrl = null)
        {
            var config = new SyncConfig
            {
                WatchedFolder = Clone,
                RemoteUrl = remoteUrl ?? BarePath,
                Branch = "main",
                Identity = new CommitIdentity { Name = peerName, Email = $"{peerName.ToLowerInvariant()}@test.local" }
            };
            var remote = new GitHubRepository(config, Substitute.For<ICredentialStore>(), new GitProcess());
            return new RelayWatcher(
                config, remote, new GitProcess(), new RelaySeenStateStore(SeenDirectory), bus);
        }
    }
}
