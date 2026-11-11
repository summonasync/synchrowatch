using Synchro.Core;
using Syncrowatch.Core.Config;

namespace Syncrowatch.Core.Engine;

public sealed class BatchCommitter(SyncConfig config, LocalRepoOps ops)
{
    public async Task<Result<CommitOutcome>> CommitBatch(IReadOnlyList<string> relativePaths, CancellationToken cancellationToken)
    {
        if (relativePaths.Count == 0)
        {
            return new Result<CommitOutcome>.Fail(SyncError.Invalid("commit batch is empty"));
        }

        var staged = await ops.Stage(config.WatchedFolder, relativePaths, cancellationToken).ConfigureAwait(false);
        if (staged is Result<StageOutcome>.Fail stageFailure)
        {
            return new Result<CommitOutcome>.Fail(stageFailure.Error);
        }

        var skipped = ((Result<StageOutcome>.Ok)staged).Value.SkippedPaths;
        var batched = relativePaths.Where(path => !skipped.Contains(path)).ToArray();
        return await ops.Commit(config.WatchedFolder, config.Identity, SyncMessage(batched), relativePaths, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string SyncMessage(IReadOnlyList<string> relativePaths) =>
        relativePaths.Count == 1
            ? $"sync: 1 file changed ({relativePaths[0]})"
            : $"sync: {relativePaths.Count} files changed";
}
