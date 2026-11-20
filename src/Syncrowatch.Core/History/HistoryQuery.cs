namespace Syncrowatch.Core.History;

public sealed record HistoryQuery
{
    public int Limit { get; init; } = 50;
    public IReadOnlyCollection<SyncEventKind>? Kinds { get; init; }
    public DateTimeOffset? Since { get; init; }
}
