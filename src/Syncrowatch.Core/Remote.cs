namespace Syncrowatch.Core;

public sealed record RemoteRef(string Name, string Sha);

public sealed record RemoteValidation(bool Reachable, string? DefaultBranch);
