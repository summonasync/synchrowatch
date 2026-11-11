namespace Syncrowatch.Core.Engine;

public sealed record PendingPush(string Branch, string Sha, DateTimeOffset StagedUtc);
