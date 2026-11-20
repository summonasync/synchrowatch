namespace Syncrowatch.Core.History;

public sealed record SyncEventDraft(SyncEventKind Kind, string Branch, string Detail, IReadOnlyList<string> Paths);
