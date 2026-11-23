using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.Core.Engine.Conflicts;
using Syncrowatch.Core.History;
using ConflictsPending = Syncrowatch.Core.Engine.Conflicts.PendingConflict;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class ConflictServiceTests : IDisposable
{
    private const string Shared = "shared.txt";

    private readonly ConflictSandbox _sandbox = new();

    [Fact]
    public async Task MergeConflictedNotification_BecomesPendingConflictOnTheBus()
    {
        using var service = new ConflictService(
            _sandbox.ConfigFor(_sandbox.CloneB), _sandbox.LocalGit(), _sandbox.StoreFor(_sandbox.CloneB), _sandbox.Bus);
        await _sandbox.EditIntoConflict(Shared, "A side\n", "B side\n");

        var pending = await WaitForAsync(
            async () =>
            {
                var outcome = await service.Pending(CancellationToken.None);
                return outcome is Result<IReadOnlyList<ConflictsPending>>.Ok found && found.Value.Count > 0
                    ? found.Value
                    : null;
            },
            TimeSpan.FromSeconds(10));

        pending.Single().Path.ShouldBe(Shared);
        pending.Single().Kind.ShouldBe(ConflictKind.TextConflict);
        pending.Single().CopyPath.ShouldNotBeNull();
    }

    [Fact]
    public async Task Resolve_AfterStoreRestart_CompletesTheConflict()
    {
        var storeDirectory = Path.Combine(_sandbox.Root, "conflicts-clone-b");
        using var service = new ConflictService(
            _sandbox.ConfigFor(_sandbox.CloneB), _sandbox.LocalGit(),
            new JsonConflictStore(storeDirectory), _sandbox.Bus);
        await _sandbox.EditIntoConflict(Shared, "A side\n", "B side\n");
        await WaitForPendingAsync(service);

        using var restarted = new ConflictService(
            _sandbox.ConfigFor(_sandbox.CloneB), _sandbox.LocalGit(),
            new JsonConflictStore(storeDirectory), _sandbox.Bus);

        var outcome = await restarted.Resolve(Shared, ConflictOption.KeepMine, CancellationToken.None);

        outcome.ShouldBeOfType<Result<ConflictResolution>.Ok>().Value.CommitSha.ShouldNotBeNull();
        var closed = await new JsonConflictStore(storeDirectory).Pending();
        ((Result<IReadOnlyList<ConflictRecord>>.Ok)closed).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task DetectionFailure_PublishesSyncFailedAndHistoryEntry()
    {
        var history = new JsonSyncEventStore(Path.Combine(_sandbox.Root, "history-broken"));
        var brokenConfig = _sandbox.ConfigFor(_sandbox.CloneB) with
        {
            WatchedFolder = Path.Combine(_sandbox.Root, "missing-folder")
        };
        using var service = new ConflictService(
            brokenConfig, _sandbox.LocalGit(), _sandbox.StoreFor(_sandbox.CloneB), _sandbox.Bus, history);

        _sandbox.Bus.Publish(new SyncNotification.MergeConflicted(DateTimeOffset.UtcNow, "main", [Shared]));

        var events = await WaitForAsync(
            async () =>
            {
                var outcome = await history.Query(new HistoryQuery { Limit = 10 }, CancellationToken.None);
                var found = ((Result<IReadOnlyList<SyncEvent>>.Ok)outcome).Value;
                return found.Count > 0 ? found : null;
            },
            TimeSpan.FromSeconds(10));

        events.Single(@event => @event.Kind == SyncEventKind.SyncFailed).Paths.ShouldBe([Shared]);
        _sandbox.Notifications.OfType<SyncNotification.SyncFailed>()
            .Single().Error.Code.ShouldBe("conflict_detection_failed");
    }

    [Fact]
    public async Task Pending_WithoutConflicts_ReturnsEmptyList()
    {
        using var service = new ConflictService(
            _sandbox.ConfigFor(_sandbox.CloneB), _sandbox.LocalGit(), _sandbox.StoreFor(_sandbox.CloneB), _sandbox.Bus);

        var outcome = await service.Pending(CancellationToken.None);

        outcome.ShouldBeOfType<Result<IReadOnlyList<ConflictsPending>>.Ok>().Value.ShouldBeEmpty();
    }

    public void Dispose() => _sandbox.Dispose();

    private static async Task<T> WaitForAsync<T>(Func<Task<T?>> probe, TimeSpan budget) where T : class
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (await probe() is { } found)
            {
                return found;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("condition was not met in time");
    }

    private Task<IReadOnlyList<ConflictsPending>> WaitForPendingAsync(ConflictService service) =>
        WaitForAsync<IReadOnlyList<ConflictsPending>>(
            async () =>
            {
                var outcome = await service.Pending(CancellationToken.None);
                return outcome is Result<IReadOnlyList<ConflictsPending>>.Ok found && found.Value.Count > 0
                    ? found.Value
                    : null;
            },
            TimeSpan.FromSeconds(10));
}
