namespace Syncrowatch.Core.Config;

public sealed record SyncConfig
{
    public static readonly TimeSpan MinDebounce = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan MaxDebounce = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(60);

    public required string WatchedFolder { get; init; }
    public required string RemoteUrl { get; init; }
    public required string Branch { get; init; }
    public SyncRunMode Mode { get; init; } = SyncRunMode.Auto;
    public TimeSpan Debounce { get; init; } = DefaultDebounce;
    public TimeSpan PollInterval { get; init; } = DefaultPollInterval;
    public CommitIdentity Identity { get; init; } = CommitIdentity.Default;
}
