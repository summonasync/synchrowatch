namespace Syncrowatch.Core.Transport;

public sealed record GitLogEntry(string Sha, string AuthorName, string AuthorEmail, DateTimeOffset WhenUtc, string Subject);
