using Synchro.Core;
using Syncrowatch.Core.Config;

namespace Syncrowatch.Core.Engine;

public sealed class BatchCommitter(SyncConfig config, LocalRepoOps ops)
{
    public async Task<Result<string>> CommitBatch(IReadOnlyList<string> relativePaths, CancellationToken cancellationToken)
    {
        if (relativePaths.Count == 0)
        {
            return new Result<string>.Fail(SyncError.Invalid("commit batch is empty"));
        }

        var staged = await ops.Stage(config.WatchedFolder, relativePaths, cancellationToken).ConfigureAwait(false);
        if (staged is Result<Unit>.Fail stageFailure)
        {
            return new Result<string>.Fail(stageFailure.Error);
        }

        return await ops.Commit(config.WatchedFolder, config.Identity, SyncMessage(relativePaths), relativePaths, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string SyncMessage(IReadOnlyList<string> relativePaths) =>
        relativePaths.Count == 1
            ? $"sync: 1 file changed ({relativePaths[0]})"
            : $"sync: {relativePaths.Count} files changed";
}
