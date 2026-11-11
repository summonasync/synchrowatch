namespace Syncrowatch.Core.Engine;

public interface IPushConfirmation
{
    Task<bool> RequestAsync(PendingPush push, CancellationToken cancellationToken);
}
