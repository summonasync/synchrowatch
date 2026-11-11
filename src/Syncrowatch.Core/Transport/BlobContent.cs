namespace Syncrowatch.Core.Transport;

public sealed record BlobContent(bool Found, byte[] Bytes)
{
    public static BlobContent Missing { get; } = new(false, []);
}
