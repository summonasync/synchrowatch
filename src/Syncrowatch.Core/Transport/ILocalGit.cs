using Syncrowatch.Core;
using Syncrowatch.Core.Config;

namespace Syncrowatch.Core.Transport;

public interface ILocalGit
{
    Task<Result<Unit>> Init(string folder, string branch, CommitIdentity identity, CancellationToken cancellationToken);

    Task<Result<Unit>> AddRemote(string folder, string remoteName, string url, CancellationToken cancellationToken);

    Task<Result<Unit>> ApplyExcludes(string folder, IReadOnlyList<string> patterns, CancellationToken cancellationToken);

    Task<Result<Unit>> StageAll(string folder, CancellationToken cancellationToken);

    Task<Result<StageOutcome>> StagePaths(string folder, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken);

    Task<Result<Unit>> Commit(string folder, string message, CancellationToken cancellationToken);

    Task<Result<Unit>> CommitEmpty(string folder, string message, CancellationToken cancellationToken);

    Task<Result<CommitOutcome>> CommitPaths(
        string folder, CommitIdentity identity, string message, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken);

    Task<Result<Unit>> Checkout(string folder, string branch, string startPoint, CancellationToken cancellationToken);

    Task<Result<LocalHead>> HeadInfo(string folder, CancellationToken cancellationToken);

    Task<Result<bool>> IsWorkingTreeEmpty(string folder, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<string>>> DirtyPaths(string folder, CancellationToken cancellationToken);

    Task<Result<BlobContent>> ReadBlob(string folder, string revision, string relativePath, CancellationToken cancellationToken);

    Task<Result<BlobContent>> ReadBlobBySha(string folder, string objectSha, CancellationToken cancellationToken);

    Task<Result<bool>> HasMergeInProgress(string folder, CancellationToken cancellationToken);

    Task<Result<MergeOutcome>> MergeNoCommit(string folder, string revision, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ConflictIndexEntry>>> ReadConflictedIndex(string folder, CancellationToken cancellationToken);

    Task<Result<string>> CommitMerge(string folder, CommitIdentity identity, string message, CancellationToken cancellationToken);

    Task<Result<StageOutcome>> RemovePaths(string folder, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<string>>> ListTrackedFiles(string folder, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<GitLogEntry>>> LogPath(
        string folder, string relativePath, int limit, int skip, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<string>>> ChangedPaths(
        string folder, string fromRevision, string toRevision, CancellationToken cancellationToken);
}
