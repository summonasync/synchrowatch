namespace Syncrowatch.Core.Engine.Conflicts;

public sealed record PendingConflict(string Path, ConflictKind Kind, string? CopyPath, DateTimeOffset DetectedUtc);
