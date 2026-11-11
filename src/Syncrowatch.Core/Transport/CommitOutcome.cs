namespace Syncrowatch.Core.Transport;

public sealed record CommitOutcome(IReadOnlyList<string> SkippedPaths, string? CommitSha);
