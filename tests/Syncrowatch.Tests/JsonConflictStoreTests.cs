using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.Core.Engine.Conflicts;
using Xunit;

namespace Syncrowatch.Tests;

public class JsonConflictStoreTests : IDisposable
{
    private readonly string _directory = TempDir.Create();

    [Fact]
    public async Task Save_AndPending_ReturnTrackedConflict()
    {
        var store = new JsonConflictStore(_directory);
        var record = new ConflictRecord
        {
            Id = Guid.NewGuid(),
            Path = "docs/spec.md",
            Kind = ConflictKind.TextConflict,
            CopyPath = "docs/spec (theirs 2026-11-11 101010).md",
            DetectedUtc = new DateTimeOffset(2026, 11, 11, 10, 10, 10, TimeSpan.Zero),
            Status = ConflictRecordStatus.Pending
        };

        (await store.Save(record)).ShouldBeOfType<Result<Unit>.Ok>();
        var pending = await store.Pending();
        ((Result<IReadOnlyList<ConflictRecord>>.Ok)pending).Value
            .Single().Path.ShouldBe("docs/spec.md");
    }

    [Fact]
    public async Task ResolvedRecords_LeavePendingAndStayInAll()
    {
        var store = new JsonConflictStore(_directory);
        var record = MakeRecord("notes.txt", ConflictRecordStatus.Pending);
        await store.Save(record);

        await store.Save(record with
        {
            Status = ConflictRecordStatus.Resolved,
            Option = ConflictOption.KeepMine,
            ResolvedUtc = record.DetectedUtc.AddMinutes(2)
        });

        (await store.Pending()).ShouldBeOfType<Result<IReadOnlyList<ConflictRecord>>.Ok>()
            .Value.ShouldBeEmpty();
        var all = await store.All();
        ((Result<IReadOnlyList<ConflictRecord>>.Ok)all).Value
            .Single().Option.ShouldBe(ConflictOption.KeepMine);
    }

    [Fact]
    public async Task SaveTwice_SamePath_ReplacesInsteadOfDuplicating()
    {
        var store = new JsonConflictStore(_directory);
        await store.Save(MakeRecord("dup.txt", ConflictRecordStatus.Pending));
        await store.Save(MakeRecord("dup.txt", ConflictRecordStatus.Pending));

        var all = await store.All();
        ((Result<IReadOnlyList<ConflictRecord>>.Ok)all).Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Restart_NewInstance_LoadsPendingState()
    {
        var first = new JsonConflictStore(_directory);
        await first.Save(MakeRecord("survivor.txt", ConflictRecordStatus.Pending));

        var restarted = new JsonConflictStore(_directory);
        var pending = await restarted.Pending();
        ((Result<IReadOnlyList<ConflictRecord>>.Ok)pending).Value
            .Single().Kind.ShouldBe(ConflictKind.BinaryConflict);
    }

    [Fact]
    public async Task ConcurrentSaves_SerializePersistAndKeepFileParsable()
    {
        var store = new JsonConflictStore(_directory);
        var saves = Enumerable.Range(0, 24)
            .Select(index => store.Save(MakeRecord($"parallel-{index}.txt", ConflictRecordStatus.Pending)));
        await Task.WhenAll(saves);

        var all = await store.All();
        ((Result<IReadOnlyList<ConflictRecord>>.Ok)all).Value.Count.ShouldBe(24);
        Directory.GetFiles(_directory, "*.tmp").ShouldBeEmpty();

        var restarted = new JsonConflictStore(_directory);
        var reloaded = await restarted.All();
        ((Result<IReadOnlyList<ConflictRecord>>.Ok)reloaded).Value.Count.ShouldBe(24);
    }

    private static ConflictRecord MakeRecord(string path, ConflictRecordStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Path = path,
        Kind = ConflictKind.BinaryConflict,
        DetectedUtc = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero),
        Status = status
    };

    public void Dispose() => Directory.Delete(_directory, true);
}
