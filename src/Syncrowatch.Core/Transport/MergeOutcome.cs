namespace Syncrowatch.Core.Transport;

public abstract record MergeOutcome
{
    public sealed record Merged(string FromSha, string ToSha, IReadOnlyList<string> Files) : MergeOutcome;

    public sealed record Conflicted(IReadOnlyList<string> ConflictedPaths) : MergeOutcome;
}
