using Syncrowatch.App.Wizard;
using Syncrowatch.Core.Engine.Conflicts;
using HistoryConflict = Syncrowatch.Core.History.PendingConflict;

namespace Syncrowatch.App.History;

public sealed class PendingConflictViewModel(HistoryConflict conflict, DateTimeOffset nowUtc, ConflictAction? resolve = null)
{
    private readonly ConflictAction? _resolve = resolve;

    public string Path { get; } = conflict.RelativePath;

    public string TimeText { get; } = RelativeTime.Format(conflict.DetectedUtc, nowUtc);

    public string Summary { get; } =
        $"{System.IO.Path.GetFileName(conflict.OursPath)} vs {System.IO.Path.GetFileName(conflict.TheirsPath)}";

    public string LocalVersion { get; } = System.IO.Path.GetFileName(conflict.OursPath);

    public string TheirsVersion { get; } = System.IO.Path.GetFileName(conflict.TheirsPath);

    public string BaseVersion { get; } = System.IO.Path.GetFileName(conflict.RelativePath);

    public bool CanResolve { get; } = resolve is not null;

    public AsyncCommand DropCommand { get; } = new(
        () => resolve!.Invoke(conflict.RelativePath, ConflictOption.KeepMine),
        () => resolve is not null);

    public AsyncCommand ReplaceCommand { get; } = new(
        () => resolve!.Invoke(conflict.RelativePath, ConflictOption.TakeTheirs),
        () => resolve is not null);

    public AsyncCommand MergeCommand { get; } = new(
        () => resolve!.Invoke(conflict.RelativePath, ConflictOption.KeepCopy),
        () => resolve is not null);
}
