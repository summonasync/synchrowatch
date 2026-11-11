using Syncrowatch.Core.History;

namespace Syncrowatch.App.History;

public sealed class PendingConflictViewModel(PendingConflict conflict, DateTimeOffset nowUtc)
{
    public string Path { get; } = conflict.RelativePath;

    public string TimeText { get; } = RelativeTime.Format(conflict.DetectedUtc, nowUtc);

    public string Summary { get; } =
        $"{System.IO.Path.GetFileName(conflict.OursPath)} vs {System.IO.Path.GetFileName(conflict.TheirsPath)}";
}
