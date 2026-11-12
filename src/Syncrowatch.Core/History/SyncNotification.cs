using Synchro.Core;
using Syncrowatch.Core.Engine;

namespace Syncrowatch.Core.History;

public abstract record SyncNotification
{
    public sealed record SyncCompleted(DateTimeOffset OccurredUtc, SyncCycleReport Report) : SyncNotification;

    public sealed record AutoMerged(DateTimeOffset OccurredUtc, string Branch, IReadOnlyList<string> Files) : SyncNotification;

    public sealed record MergeConflicted(DateTimeOffset OccurredUtc, string Branch, IReadOnlyList<string> ConflictedPaths) : SyncNotification;

    public sealed record SyncFailed(DateTimeOffset OccurredUtc, SyncError Error) : SyncNotification;

    public sealed record PushPending(PendingPush Push) : SyncNotification;

    public sealed record RelayPackageDetected(
        DateTimeOffset OccurredUtc,
        string Project,
        string PackageFile,
        string CliCommand) : SyncNotification;

    public sealed record RelayEntriesSkipped(DateTimeOffset OccurredUtc, int Count) : SyncNotification;

    public sealed record RelayStateCorrupted(DateTimeOffset OccurredUtc) : SyncNotification;
}
