using Syncrowatch.Core;

namespace Syncrowatch.Core;

public sealed record SyncOutcome(string Branch, string FromSha, string ToSha);

public sealed record SyncCycleReport(DateTimeOffset StartedUtc, SyncOutcome? Pulled, SyncOutcome? Pushed);

public sealed record EngineStatus(bool Running, DateTimeOffset? LastCycleUtc, SyncError? LastError);
