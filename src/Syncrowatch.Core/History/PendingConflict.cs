namespace Syncrowatch.Core.History;

public sealed record PendingConflict(
    string RelativePath,
    string OursPath,
    string TheirsPath,
    DateTimeOffset DetectedUtc);
