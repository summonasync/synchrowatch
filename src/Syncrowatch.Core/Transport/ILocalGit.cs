using Synchro.Core;
using Syncrowatch.Core.Config;

namespace Syncrowatch.Core.Transport;

public interface ILocalGit
{
    Task<Result<Unit>> Init(string folder, string branch, CommitIdentity identity, CancellationToken cancellationToken);

    Task<Result<Unit>> AddRemote(string folder, string remoteName, string url, CancellationToken cancellationToken);

    Task<Result<Unit>> ApplyExcludes(string folder, IReadOnlyList<string> patterns, CancellationToken cancellationToken);

    Task<Result<Unit>> StageAll(string folder, CancellationToken cancellationToken);

    Task<Result<Unit>> Commit(string folder, string message, CancellationToken cancellationToken);

    Task<Result<Unit>> Checkout(string folder, string branch, string startPoint, CancellationToken cancellationToken);

    Task<Result<LocalHead>> HeadInfo(string folder, CancellationToken cancellationToken);

    Task<Result<bool>> IsWorkingTreeEmpty(string folder, CancellationToken cancellationToken);
}
