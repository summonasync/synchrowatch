using Synchro.Core;

namespace Syncrowatch.Core;

public interface IRemoteRepository
{
    Task<Result<RemoteValidation>> ValidateRepository(CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<RemoteRef>>> Fetch(CancellationToken cancellationToken);

    Task<Result<SyncOutcome>> Pull(string branch, CancellationToken cancellationToken);

    Task<Result<SyncOutcome>> Push(string branch, CancellationToken cancellationToken);

    Task<Result<SyncOutcome>> PushForceWithLease(string branch, string expectedRemoteSha, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<RemoteRef>>> ListBranch(CancellationToken cancellationToken);
}
