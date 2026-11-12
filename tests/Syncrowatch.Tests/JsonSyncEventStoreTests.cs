using Shouldly;
using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.History;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class JsonSyncEventStoreTests : IDisposable
{
    private readonly string _directory = TempDir.Create();

    [Fact]
    public async Task Append_PersistsEventsForFreshStoreInstance()
    {
        var first = new JsonSyncEventStore(_directory);
        await first.Append(new SyncEventDraft(SyncEventKind.CommitIn, "main", "a..b", []), CancellationToken.None);
        await first.Append(
            new SyncEventDraft(SyncEventKind.CommitOut, "main", "pushed", ["notes.txt"]), CancellationToken.None);

        var second = new JsonSyncEventStore(_directory);
        var outcome = await second.Query(new HistoryQuery { Limit = 100 }, CancellationToken.None);

        var events = outcome.ShouldBeOfType<Result<IReadOnlyList<SyncEvent>>.Ok>().Value;
        events.Count.ShouldBe(2);
        events[0].Sequence.ShouldBe(2);
        events[0].Kind.ShouldBe(SyncEventKind.CommitOut);
        events[0].Paths.ShouldBe(["notes.txt"]);
        events[1].Sequence.ShouldBe(1);
    }

    [Fact]
    public async Task Query_AppliesKindSinceAndLimitFiltersNewestFirst()
    {
        var store = new JsonSyncEventStore(_directory);
        await store.Append(new SyncEventDraft(SyncEventKind.CommitIn, "main", "first", []), CancellationToken.None);
        await store.Append(
            new SyncEventDraft(SyncEventKind.AutoMerged, "main", "second", ["notes.txt"]), CancellationToken.None);
        await store.Append(new SyncEventDraft(SyncEventKind.SyncFailed, "main", "third", []), CancellationToken.None);

        var merged = await store.Query(
            new HistoryQuery { Limit = 10, Kinds = [SyncEventKind.AutoMerged] }, CancellationToken.None);
        merged.ShouldBeOfType<Result<IReadOnlyList<SyncEvent>>.Ok>().Value.Single()
            .Detail.ShouldBe("second");

        var limited = await store.Query(new HistoryQuery { Limit = 2 }, CancellationToken.None);
        limited.ShouldBeOfType<Result<IReadOnlyList<SyncEvent>>.Ok>().Value
            .Select(@event => @event.Detail).ShouldBe(["third", "second"]);

        var future = await store.Query(
            new HistoryQuery { Limit = 10, Since = DateTimeOffset.UtcNow.AddMinutes(5) }, CancellationToken.None);
        future.ShouldBeOfType<Result<IReadOnlyList<SyncEvent>>.Ok>().Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task CorruptHistoryFile_StartsEmptyWithoutThrowing()
    {
        File.WriteAllText(SyncrowatchPaths.HistoryFile(_directory), "{ definitely not json");

        var store = new JsonSyncEventStore(_directory);

        var outcome = await store.Query(new HistoryQuery(), CancellationToken.None);
        outcome.ShouldBeOfType<Result<IReadOnlyList<SyncEvent>>.Ok>().Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task AppendBeyondRetention_DropsOldestEvents()
    {
        var store = new JsonSyncEventStore(_directory);
        var draft = new SyncEventDraft(SyncEventKind.CommitOut, "main", "cycle", []);

        for (var count = 0; count < JsonSyncEventStore.MaxRetained + 5; count++)
        {
            await store.Append(draft, CancellationToken.None);
        }

        var outcome = await store.Query(new HistoryQuery { Limit = 10_000 }, CancellationToken.None);
        var events = outcome.ShouldBeOfType<Result<IReadOnlyList<SyncEvent>>.Ok>().Value;
        events.Count.ShouldBe(JsonSyncEventStore.MaxRetained);
        events[0].Sequence.ShouldBe(JsonSyncEventStore.MaxRetained + 5);
        events[^1].Sequence.ShouldBe(6);
    }

    [Fact]
    public async Task Append_WhenPersistenceFails_ReturnsFailButKeepsEventQueryable()
    {
        var store = new JsonSyncEventStore(_directory);
        Directory.CreateDirectory(SyncrowatchPaths.HistoryFile(_directory));

        var outcome = await store.Append(
            new SyncEventDraft(SyncEventKind.SyncFailed, "main", "cycle blew up", []), CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncEvent>.Fail>().Error.Code.ShouldBe("unavailable");
        var events = (await store.Query(new HistoryQuery { Limit = 10 }, CancellationToken.None))
            .ShouldBeOfType<Result<IReadOnlyList<SyncEvent>>.Ok>().Value;
        events.Single().Detail.ShouldBe("cycle blew up");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
        }
    }
}
