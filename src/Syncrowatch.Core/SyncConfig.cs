using Synchro.Core;

namespace Syncrowatch.Core;

public sealed record SyncConfig
{
    public required string WatchedFolder { get; init; }
    public required string RemoteUrl { get; init; }
    public required string Branch { get; init; }
    public required SyncMode Mode { get; init; }
    public required TimeSpan PollInterval { get; init; }
}
