namespace Syncrowatch.Core.Engine.Conflicts;

public sealed record ConflictResolution(
    IReadOnlyList<string> ResolvedPaths,
    string? CommitSha,
    IReadOnlyList<string> RemainingPaths)
{
    public static ConflictResolution Staged(string path, IReadOnlyList<string> remainingPaths) =>
        new([path], null, remainingPaths);
}
