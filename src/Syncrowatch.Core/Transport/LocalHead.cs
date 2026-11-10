namespace Syncrowatch.Core.Transport;

public sealed record LocalHead(string? Branch, string Sha)
{
    public bool HasCommits => Sha.Length > 0;
}
