namespace Syncrowatch.Core.Engine.Conflicts;

public sealed record ConflictRecord
{
    public required Guid Id { get; init; }
    public required string Path { get; init; }
    public required ConflictKind Kind { get; init; }
    public string? CopyPath { get; init; }
    public required DateTimeOffset DetectedUtc { get; init; }
    public required ConflictRecordStatus Status { get; init; }
    public ConflictOption? Option { get; init; }
    public DateTimeOffset? ResolvedUtc { get; init; }
    public string? CommitSha { get; init; }
}
