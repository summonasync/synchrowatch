namespace Syncrowatch.Core.Relay;

public sealed record RelaySeenSnapshot(IReadOnlyList<RelaySeenPackage> Packages, bool RecoveredFromCorruption);
