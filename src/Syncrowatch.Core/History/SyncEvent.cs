namespace Syncrowatch.Core.History;

public sealed record SyncEvent(
    long Sequence,
    DateTimeOffset OccurredUtc,
    SyncEventKind Kind,
    string Branch,
    string Detail,
    IReadOnlyList<string> Paths);
