namespace Syncrowatch.Core.Engine;

public enum WatchChangeKind
{
    Created,
    Changed,
    Deleted
}

public sealed record FolderChange(string RelativePath, WatchChangeKind Kind);
