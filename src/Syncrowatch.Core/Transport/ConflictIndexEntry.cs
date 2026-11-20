namespace Syncrowatch.Core.Transport;

public sealed record ConflictIndexEntry(int Stage, string BlobSha, string Path);
