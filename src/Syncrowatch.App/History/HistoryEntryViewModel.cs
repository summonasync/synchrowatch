using Syncrowatch.Core.History;

namespace Syncrowatch.App.History;

public sealed class HistoryEntryViewModel(SyncEvent @event, DateTimeOffset nowUtc)
{
    public DateTimeOffset OccurredUtc { get; } = @event.OccurredUtc;

    public string Label { get; } = @event.Kind switch
    {
        SyncEventKind.CommitIn => "Pulled",
        SyncEventKind.CommitOut => "Pushed",
        SyncEventKind.AutoMerged => "Auto-merged",
        SyncEventKind.MergeConflicted => "Conflict",
        _ => "Failed"
    };

    public string TimeText { get; } = RelativeTime.Format(@event.OccurredUtc, nowUtc);

    public string Branch { get; } = @event.Branch;

    public string Detail { get; } = @event.Detail;

    public string Files { get; } = string.Join(", ", @event.Paths);
}
