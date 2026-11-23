using Syncrowatch.Core;

namespace Syncrowatch.Core.Transport;

public static class TransportResult
{
    public static async Task<Result<T>> Capture<T>(Func<Task<Result<T>>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (GitTransportException ex)
        {
            return new Result<T>.Fail(ex.Error);
        }
    }
}
