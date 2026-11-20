namespace Syncrowatch.Core.Engine.Conflicts;

public sealed record ConflictItem(string Path, ConflictKind Kind, string? CopyPath);
