namespace Syncrowatch.Core.Transport;

public sealed record StageOutcome(IReadOnlyList<string> SkippedPaths);
