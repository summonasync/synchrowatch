namespace Syncrowatch.Core.Transport;

public sealed record GitInvocation
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public required IReadOnlyList<string> Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string> Environment { get; init; }
        = new Dictionary<string, string>();

    public TimeSpan Timeout { get; init; } = DefaultTimeout;
}
