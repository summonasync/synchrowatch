using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.Core.Bootstrap;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.Engine.Conflicts;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests.E2E;

[Collection("git-config-sandbox")]
public sealed class IgnoreIntegrityE2ETests : IDisposable
{
    private const string StandInUrl = "https://synchr.e2e/ignore.git";
    private const string StandInToken = "ghp_e2e_ignore_5e4d3c2b1a0f";

    private readonly GitConfigSandbox _sandbox = new();
    private readonly string _root = TempDir.Create();
    private readonly string _data = TempDir.Create();
    private readonly string _bare;

    public IgnoreIntegrityE2ETests()
    {
        _bare = Path.Combine(_root, "origin.git");
        GitCli.Ensure(_root, "init", "--bare", "-b", "main", _bare);
        _sandbox.WriteGlobalConfig($"[url \"{_bare}\"]\n\tinsteadOf = {StandInUrl}");
    }

    [Fact]
    public async Task SyncignoreAndDefaults_StayOutOfBootstrapCommitAndDeliveredBatches()
    {
        var folder = await PublishAsync(populate =>
        {
            File.WriteAllText(Path.Combine(populate, ".syncignore"), "drafts/\n*.key\n");
            File.WriteAllText(Path.Combine(populate, "readme.md"), "clean\n");
            Directory.CreateDirectory(Path.Combine(populate, "drafts"));
            File.WriteAllText(Path.Combine(populate, "drafts", "scratch.txt"), "wip\n");
            File.WriteAllText(Path.Combine(populate, "api.key"), "private\n");
            Directory.CreateDirectory(Path.Combine(populate, "bin"));
            File.WriteAllText(Path.Combine(populate, "bin", "app.dll"), "binary-ish\n");
            File.WriteAllText(Path.Combine(populate, ".DS_Store"), "junk\n");
            File.WriteAllText(Path.Combine(populate, "Thumbs.db"), "junk\n");
        });

        FilesTracked(_bare).ShouldBe([".syncignore", "readme.md"]);

        await BindAsync(folder);
        var time = new FakeTimeProvider();
        using var loop = await LoopForAsync(folder);
        var watcher = new FolderWatcher(folder);
        var batcher = new ChangeBatcher(folder, TimeSpan.FromSeconds(20), SyncIgnoreRules.ForFolder(folder), time);
        var delivered = new List<IReadOnlyList<string>>();
        batcher.BatchReady += deliveredBatch =>
        {
            delivered.Add(deliveredBatch);
            loop.AcceptBatch(deliveredBatch);
        };
        watcher.ChangeDetected += batcher.Accept;
        watcher.Start(new CancellationTokenSource().Token);
        try
        {
            File.WriteAllText(Path.Combine(folder, "notes.md"), "tracked edit\n");
            File.WriteAllText(Path.Combine(folder, "drafts", "more.txt"), "ignored\n");
            File.WriteAllText(Path.Combine(folder, "vault.key"), "ignored\n");
            File.WriteAllText(Path.Combine(folder, ".DS_Store"), "ignored\n");
            await SyncE2EStand.SettleAsync(batcher, minimum: 1);
            time.Advance(TimeSpan.FromSeconds(20));

            delivered.ShouldHaveSingleItem().ShouldBe(["notes.md"]);
            (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();
        }
        finally
        {
            batcher.Dispose();
            watcher.Dispose();
        }

        FilesTracked(_bare).ShouldBe([".syncignore", "notes.md", "readme.md"]);
        GitCli.Run(_bare, "log", "-1", "--pretty=%s", "main").Output.Trim()
            .ShouldBe("sync: 1 file changed (notes.md)");
    }

    [Fact]
    public async Task IgnoredPathLeakedIntoABatch_IsRefusedByTheCommitGateWithoutLanding()
    {
        var folder = await PublishAsync(populate =>
        {
            File.WriteAllText(Path.Combine(populate, ".syncignore"), "*.key\n");
            File.WriteAllText(Path.Combine(populate, "readme.md"), "clean\n");
        });

        await BindAsync(folder);
        using var loop = await LoopForAsync(folder);

        File.WriteAllText(Path.Combine(folder, "leak.key"), "should never land\n");
        loop.AcceptBatch(["leak.key"]);

        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Fail>();
        FilesTracked(_bare).ShouldBe([".syncignore", "readme.md"]);
        GitCli.Run(_bare, "rev-list", "--count", "main").Output.Trim().ShouldBe("1");
        GitCli.Run(folder, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
    }

    [Fact]
    public async Task ConflictCopyStaysOnDisk_OutOfGitStatusAndOutOfPushedHistory()
    {
        var folder = await PublishAsync(populate =>
        {
            File.WriteAllText(Path.Combine(populate, "readme.md"), "clean\n");
        });
        var peer = Path.Combine(_root, "peer");
        GitCli.Ensure(_root, "clone", _bare, peer);
        GitCli.ConfigureIdentity(peer);
        File.WriteAllText(Path.Combine(peer, "readme.md"), "peer rewrote the readme\n");
        GitCli.CommitAll(peer, "peer rewrite");
        GitCli.Push(peer, _bare, "main");

        await BindAsync(folder);
        var config = new SyncConfig
        {
            WatchedFolder = folder,
            RemoteUrl = StandInUrl,
            Branch = "main"
        };
        var bus = new SyncEventBus();
        using var loop = new SyncLoop(
            config,
            new GitHubRepository(config, new CredentialStore(_data), new GitProcess()),
            new GitLocal(new GitProcess()),
            new JsonSyncEventStore(Path.Combine(_root, "history")),
            bus);
        using var conflicts = new ConflictService(
            config,
            new GitLocal(new GitProcess()),
            new JsonConflictStore(Path.Combine(_root, "conflicts")),
            bus);

        File.WriteAllText(Path.Combine(folder, "readme.md"), "mine rewrote the readme\n");
        loop.AcceptBatch(["readme.md"]);
        (await loop.SyncOnce(CancellationToken.None))
            .ShouldBeOfType<Result<SyncCycleReport>.Fail>().Error.Code.ShouldBe("conflict");

        await SyncE2EStand.AwaitAsync(async () =>
        {
            var pending = await conflicts.Pending(CancellationToken.None);
            return pending is Result<IReadOnlyList<Syncrowatch.Core.Engine.Conflicts.PendingConflict>>.Ok ok
                && ok.Value.Count == 1;
        }, "conflict copy to settle");

        var copyFile = Directory.GetFiles(folder, "readme (theirs *").ShouldHaveSingleItem();
        GitCli.Run(folder, "status", "--porcelain").Output.Trim().ShouldBeEmpty();

        var resolved = await conflicts.Resolve("readme.md", ConflictOption.KeepCopy, CancellationToken.None);
        resolved.ShouldBeOfType<Result<ConflictResolution>.Ok>().Value.CommitSha.ShouldNotBeNull();

        (await loop.SyncOnce(CancellationToken.None)).ShouldBeOfType<Result<SyncCycleReport>.Ok>();

        GitCli.Run(_bare, "log", "--all", "--name-only", "--format=", "main").Output
            .ShouldNotContain(" (theirs ");
        FilesTracked(_bare).ShouldBe(["readme.md"]);
        File.ReadAllText(copyFile).ShouldBe("peer rewrote the readme\n");
        GitCli.Run(folder, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
    }

    private async Task<string> PublishAsync(Action<string> populate)
    {
        var folder = Path.Combine(_root, "watched-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        populate(folder);
        (await new CredentialStore(_data).Write(new PersonalAccessToken(StandInToken), CancellationToken.None))
            .ShouldBeOfType<Result<PersonalAccessToken>.Ok>();

        var config = new SyncConfig
        {
            WatchedFolder = folder,
            RemoteUrl = StandInUrl,
            Branch = "main"
        };
        var runner = new GitProcess();
        var bootstrapper = new Bootstrapper(
            config,
            new GitHubRepository(config, new CredentialStore(_data), runner),
            new GitLocal(runner));
        var plan = (await bootstrapper.Assess(CancellationToken.None))
            .ShouldBeOfType<Result<BootstrapPlan>.Ok>().Value;
        (await bootstrapper.Execute(plan, null, CancellationToken.None))
            .ShouldBeOfType<Result<BootstrapOutcome>.Ok>();
        return folder;
    }

    private async Task BindAsync(string folder)
    {
        var saved = await new JsonSyncConfigStore(_data).Save(new SyncConfig
        {
            WatchedFolder = folder,
            RemoteUrl = StandInUrl,
            Branch = "main"
        }, CancellationToken.None);
        saved.ShouldBeOfType<Result<SyncConfig>.Ok>();
    }

    private async Task<SyncLoop> LoopForAsync(string folder)
    {
        var loaded = await new JsonSyncConfigStore(_data).Load(CancellationToken.None);
        var config = loaded.ShouldBeOfType<Result<SyncConfig>.Ok>().Value;
        return new SyncLoop(
            config,
            new GitHubRepository(config, new CredentialStore(_data), new GitProcess()),
            new GitLocal(new GitProcess()),
            new JsonSyncEventStore(Path.Combine(_root, "history")),
            new SyncEventBus());
    }

    private static IReadOnlyList<string> FilesTracked(string bare) =>
        GitCli.Run(bare, "ls-tree", "--name-only", "-r", "main").Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    public void Dispose()
    {
        Directory.Delete(_root, true);
        Directory.Delete(_data, true);
        _sandbox.Dispose();
    }
}
