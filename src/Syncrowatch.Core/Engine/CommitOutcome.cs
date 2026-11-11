namespace Syncrowatch.Core.Engine;

public sealed record CommitOutcome(IReadOnlyList<string> SkippedPaths, string? CommitSha);
