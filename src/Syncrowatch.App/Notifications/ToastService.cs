using Syncrowatch.Core;
using Syncrowatch.Core.History;

namespace Syncrowatch.App.Notifications;

public sealed class ToastService : IDisposable
{
    public const int MaxVisible = 3;

    private readonly ISyncEventBus _bus;
    private readonly IToastWindowFactory _windows;
    private readonly object _gate = new();
    private readonly List<ToastViewModel> _active = [];

    public ToastService(ISyncEventBus bus, IToastWindowFactory windows)
    {
        _bus = bus;
        _windows = windows;
        _bus.Received += OnNotification;
    }

    public void Dispose() => _bus.Received -= OnNotification;

    public void Show(ToastContent content)
    {
        lock (_gate)
        {
            PresentLocked(content);
        }
    }

    private void OnNotification(SyncNotification notification)
    {
        var content = Describe(notification);
        if (content is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_active.Any(active => Repeats(active.Content, content)))
            {
                return;
            }

            PresentLocked(content);
        }
    }

    private void PresentLocked(ToastContent content)
    {
        if (_active.Count >= MaxVisible)
        {
            var oldest = _active[0];
            _active.RemoveAt(0);
            _windows.Dismiss(oldest);
        }

        var toast = new ToastViewModel(content);
        _active.Add(toast);
        ReassignSlots();
        _windows.Present(toast, () => Release(toast));
    }

    private void Release(ToastViewModel toast)
    {
        lock (_gate)
        {
            _active.Remove(toast);
            ReassignSlots();
        }
    }

    private void ReassignSlots()
    {
        for (var index = 0; index < _active.Count; index++)
        {
            _active[index].Slot = index;
        }
    }

    private static bool Repeats(ToastContent left, ToastContent right) =>
        left.Kind == right.Kind
        && left.Title == right.Title
        && left.Message == right.Message
        && left.Command == right.Command;

    private static ToastContent? Describe(SyncNotification notification) => notification switch
    {
        SyncNotification.AutoMerged merged => new ToastContent(
            ToastKind.AutoMerged,
            "Auto-merged",
            MergedMessage(merged)),
        SyncNotification.ExplicitSyncCompleted completed => new ToastContent(
            ToastKind.SyncCompleted,
            "Sync completed",
            CompletedMessage(completed.Report)),
        SyncNotification.MergeConflicted conflicted => new ToastContent(
            ToastKind.Conflict,
            "Conflict — action needed",
            ConflictMessage(conflicted)),
        SyncNotification.SyncFailed failed => new ToastContent(
            ToastKind.Failure,
            "Sync failed",
            failed.Error.Message),
        SyncNotification.RelayPackageDetected package => new ToastContent(
            ToastKind.RelayPackage,
            $"Package from {package.Project}",
            package.PackageFile,
            package.CliCommand),
        SyncNotification.RelayEntriesSkipped skipped => new ToastContent(
            ToastKind.RelayWarning,
            "Relay entry skipped",
            $"{skipped.Count} malformed relay entries skipped"),
        SyncNotification.RelayStateCorrupted => new ToastContent(
            ToastKind.RelayWarning,
            "Relay state reset",
            "relay seen-state file was corrupted and reset; packages may be re-detected"),
        _ => null
    };

    private static string MergedMessage(SyncNotification.AutoMerged merged) =>
        merged.Files.Count == 1
            ? $"1 file merged automatically on {merged.Branch}"
            : $"{merged.Files.Count} files merged automatically on {merged.Branch}";

    private static string CompletedMessage(SyncCycleReport report)
    {
        List<string> legs = [];
        if (report.Pulled is not null)
        {
            legs.Add("pulled");
        }

        if (report.Pushed is not null)
        {
            legs.Add("pushed");
        }

        var branch = (report.Pushed ?? report.Pulled)!.Branch;
        return $"{string.Join(" and ", legs)} on {branch}";
    }

    private static string ConflictMessage(SyncNotification.MergeConflicted conflicted)
    {
        var paths = conflicted.ConflictedPaths;
        var count = paths.Count;
        var noun = count == 1 ? "1 file" : $"{count} files";
        var verb = count == 1 ? "needs" : "need";
        var named = count <= 2
            ? string.Join(", ", paths)
            : $"{string.Join(", ", paths.Take(2))} +{count - 2} more";
        return $"{noun} {verb} a pick — action needed ({named})";
    }
}
