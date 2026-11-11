using Shouldly;
using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.Engine.Conflicts;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests.E2E;

public sealed class SyncE2EStand : IDisposable
{
    public const string StandInUrl = "https://synchr.e2e/stand.git";
    public const string SharedFile = "shared.txt";
    private const string StandInToken = "ghp_e2e_stand_9f8a7b6c5d4e";

    private readonly GitConfigSandbox _sandbox;
    private readonly Dictionary<string, ISyncEventStore> _history = new(StringComparer.Ordinal);
    private readonly List<SyncLoop> _loops = [];
    private readonly List<ConflictService> _conflicts = [];
    private readonly object _gate = new();

    public SyncE2EStand()
    {
        _sandbox = new GitConfigSandbox();
        Root = TempDir.Create();
        Bare = Path.Combine(Root, "origin.git");
        CloneA = Path.Combine(Root, "clone-a");
        CloneB = Path.Combine(Root, "clone-b");
        DataDirectory = Path.Combine(Root, "data");
        GitCli.Ensure(Root, "init", "--bare", "-b", "main", Bare);
        GitCli.Ensure(Root, "clone", Bare, CloneA);
        GitCli.ConfigureIdentity(CloneA);
        File.WriteAllText(Path.Combine(CloneA, SharedFile), "alpha\nmiddle\nomega\n");
        File.WriteAllText(Path.Combine(CloneA, "gone.txt"), "doomed and gone\n");
        GitCli.CommitAll(CloneA, "base");
        GitCli.Push(CloneA, Bare, "main");
        GitCli.Ensure(Root, "clone", Bare, CloneB);
        GitCli.ConfigureIdentity(CloneB);
        _sandbox.WriteGlobalConfig($"[url \"{Bare}\"]\n\tinsteadOf = {StandInUrl}");

        Bus.Received += notification =>
        {
            lock (_gate)
            {
                Notifications.Add(notification);
            }
        };
    }

    public string Root { get; }
    public string Bare { get; }
    public string CloneA { get; }
    public string CloneB { get; }
    public string DataDirectory { get; }
    public SyncEventBus Bus { get; } = new();
    public List<SyncNotification> Notifications { get; } = [];

    public SyncConfig ConfigFor(string clone, SyncRunMode mode = SyncRunMode.Auto) => new()
    {
        WatchedFolder = clone,
        RemoteUrl = StandInUrl,
        Branch = "main",
        Mode = mode
    };

    public async Task BindAsync(string clone, SyncRunMode mode = SyncRunMode.Auto)
    {
        var credentials = new CredentialStore(DataDirectory);
        if (await credentials.Read(CancellationToken.None) is not Result<PersonalAccessToken>.Ok)
        {
            (await credentials.Write(new PersonalAccessToken(StandInToken), CancellationToken.None))
                .ShouldBeOfType<Result<PersonalAccessToken>.Ok>();
        }

        var saved = await new JsonSyncConfigStore(DataDirectory).Save(ConfigFor(clone, mode), CancellationToken.None);
        saved.ShouldBeOfType<Result<SyncConfig>.Ok>();
    }

    public async Task<SyncLoop> LoopForAsync(string clone)
    {
        var config = await LoadBindingAsync(clone);
        var loop = new SyncLoop(
            config,
            new GitHubRepository(config, new CredentialStore(DataDirectory), new GitProcess()),
            new GitLocal(new GitProcess()),
            HistoryFor(clone),
            Bus);
        _loops.Add(loop);
        return loop;
    }

    public async Task<ConflictService> ConflictsForAsync(string clone)
    {
        var config = await LoadBindingAsync(clone);
        var service = new ConflictService(
            config,
            new GitLocal(new GitProcess()),
            new JsonConflictStore(Path.Combine(Root, "conflicts-" + Path.GetFileName(clone))),
            Bus,
            HistoryFor(clone));
        _conflicts.Add(service);
        return service;
    }

    public async Task<IReadOnlyList<SyncEvent>> EventsAsync(string clone)
    {
        var outcome = await HistoryFor(clone).Query(new HistoryQuery { Limit = 200 }, CancellationToken.None);
        return outcome.ShouldBeOfType<Result<IReadOnlyList<SyncEvent>>.Ok>().Value;
    }

    public IReadOnlyList<SyncNotification> SnapshotNotifications()
    {
        lock (_gate)
        {
            return [.. Notifications];
        }
    }

    public Task AwaitConflictsAsync(ConflictService service, int expected) =>
        AwaitAsync(async () =>
        {
            var pending = await service.Pending(CancellationToken.None);
            return pending is Result<IReadOnlyList<Syncrowatch.Core.Engine.Conflicts.PendingConflict>>.Ok ok
                && ok.Value.Count == expected;
        }, $"{expected} conflict records to settle");

    public static async Task AwaitAsync(Func<Task<bool>> ready, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        var satisfied = false;
        while (DateTime.UtcNow < deadline)
        {
            if (await ready().ConfigureAwait(false))
            {
                satisfied = true;
                break;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        satisfied.ShouldBeTrue(what);
    }

    private async Task<SyncConfig> LoadBindingAsync(string clone)
    {
        var loaded = await new JsonSyncConfigStore(DataDirectory).Load(CancellationToken.None);
        var config = loaded.ShouldBeOfType<Result<SyncConfig>.Ok>().Value;
        config.WatchedFolder.ShouldBe(clone, "stored binding must watch the requested clone");
        return config;
    }

    private ISyncEventStore HistoryFor(string clone) =>
        _history.TryGetValue(clone, out var store)
            ? store
            : _history[clone] = new JsonSyncEventStore(Path.Combine(Root, "history-" + Path.GetFileName(clone)));

    public void Dispose()
    {
        foreach (var service in _conflicts)
        {
            service.Dispose();
        }

        foreach (var loop in _loops)
        {
            loop.Dispose();
        }

        Directory.Delete(Root, true);
        _sandbox.Dispose();
    }
}
