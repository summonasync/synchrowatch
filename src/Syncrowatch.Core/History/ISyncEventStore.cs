using Synchro.Core;

namespace Syncrowatch.Core.History;

public interface ISyncEventStore
{
    Task<Result<SyncEvent>> Append(SyncEventDraft draft, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<SyncEvent>>> Query(HistoryQuery query, CancellationToken cancellationToken);
}
