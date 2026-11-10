namespace Syncrowatch.Core.Config;

public sealed record CommitIdentity
{
    public required string Name { get; init; }
    public required string Email { get; init; }

    public static CommitIdentity Default { get; } = new()
    {
        Name = "Syncrowatch",
        Email = "syncrowatch@users.noreply.github.com"
    };
}
