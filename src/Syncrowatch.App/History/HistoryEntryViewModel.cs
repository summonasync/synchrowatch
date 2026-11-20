using Syncrowatch.Core.History;

namespace Syncrowatch.App.History;

public sealed class HistoryEntryViewModel(SyncEvent @event, DateTimeOffset nowUtc, bool isLatest = false)
{
    public DateTimeOffset OccurredUtc { get; } = @event.OccurredUtc;

    public bool IsLatest { get; } = isLatest;

    public string Label { get; } = @event.Kind switch
    {
        SyncEventKind.CommitIn => "Pulled",
        SyncEventKind.CommitOut => "Pushed",
        SyncEventKind.AutoMerged => "Auto-merged",
        SyncEventKind.MergeConflicted => "Conflict",
        SyncEventKind.SyncFailed => "Failed",
        _ => "Unknown"
    };

    public string Glyph { get; } = @event.Kind switch
    {
        SyncEventKind.CommitIn => "=>",
        SyncEventKind.CommitOut => "()",
        SyncEventKind.AutoMerged => "{}",
        SyncEventKind.MergeConflicted => "!=",
        SyncEventKind.SyncFailed => ";",
        _ => "?"
    };

    public bool IsAttention { get; } =
        @event.Kind is SyncEventKind.MergeConflicted or SyncEventKind.SyncFailed;

    public bool HasFiles { get; } = @event.Paths.Count > 0;

    public string TimeText { get; } = RelativeTime.Format(@event.OccurredUtc, nowUtc);

    public string Branch { get; } = @event.Branch;

    public string Detail { get; } = @event.Detail;

    public string Files { get; } = string.Join(", ", @event.Paths);
}
