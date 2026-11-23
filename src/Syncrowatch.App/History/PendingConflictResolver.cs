using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine.Conflicts;
using Syncrowatch.Core.Transport;

namespace Syncrowatch.App.History;

public sealed class PendingConflictResolver(ISyncConfigStore configStore, ILocalGit local, IConflictStore store) : IConflictResolver
{
    public async Task<Result<ConflictResolution>> Resolve(
        string relativePath, ConflictOption option, CancellationToken cancellationToken)
    {
        var loaded = await configStore.Load(cancellationToken).ConfigureAwait(false);
        if (loaded is not Result<SyncConfig>.Ok config)
        {
            return new Result<ConflictResolution>.Fail(((Result<SyncConfig>.Fail)loaded).Error);
        }

        using var executor = new ResolutionExecutor(config.Value, local, store);
        return await executor.Resolve(relativePath, option, cancellationToken).ConfigureAwait(false);
    }
}
