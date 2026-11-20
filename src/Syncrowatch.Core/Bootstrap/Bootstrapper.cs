using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Transport;

namespace Syncrowatch.Core.Bootstrap;

public sealed class Bootstrapper(SyncConfig config, IRemoteRepository remote, ILocalGit local) : IBootstrapper
{
    private const string OriginName = "origin";
    private const string InitialCommitMessage = "Initial Syncrowatch commit";
    private const string VcsDirectoryName = ".git";
    private const string StateDirectoryName = ".synchro";

    public Task<Result<BootstrapPlan>> Assess(CancellationToken cancellationToken) =>
        TransportResult.Capture<BootstrapPlan>(async () =>
        {
            GitArgumentGuards.AcceptableRemoteUrl(config.RemoteUrl);
            var folderHasContent = FolderHasContent();
            var heads = await remote.ListBranch(cancellationToken).ConfigureAwait(false);
            var branchRef = heads switch
            {
                Result<IReadOnlyList<RemoteRef>>.Ok listing => listing.Value.FirstOrDefault(reference => reference.Name == config.Branch),
                Result<IReadOnlyList<RemoteRef>>.Fail failure => throw new GitOperationException(failure.Error),
                _ => throw new InvalidOperationException("branch listing is neither ok nor fail")
            };

            return (folderHasContent, branchRef) switch
            {
                (false, null) => new Result<BootstrapPlan>.Ok(new BootstrapPlan.WatchEmpty()),
                (false, _) => new Result<BootstrapPlan>.Ok(new BootstrapPlan.CheckoutRemote()),
                (true, null) => new Result<BootstrapPlan>.Ok(new BootstrapPlan.PublishFolder()),
                (true, { } branch) => new Result<BootstrapPlan>.Ok(new BootstrapPlan.Decide(
                    new BootstrapOptions(config.Branch, branch.Sha, CountLocalFiles(config.WatchedFolder))))
            };
        });

    public Task<Result<BootstrapOutcome>> Execute(BootstrapPlan plan, BootstrapDirection? direction, CancellationToken cancellationToken) =>
        TransportResult.Capture<BootstrapOutcome>(async () =>
        {
            GitArgumentGuards.AcceptableRemoteUrl(config.RemoteUrl);
            if (plan is BootstrapPlan.Decide decide)
            {
                return direction switch
                {
                    null => new Result<BootstrapOutcome>.Ok(new BootstrapOutcome.ChoiceNeeded(decide.Options)),
                    BootstrapDirection.RepoWins => await TrackRemoteAsync(cancellationToken).ConfigureAwait(false),
                    BootstrapDirection.FolderWins => await PublishFolderAsync(decide.Options.RemoteSha, cancellationToken).ConfigureAwait(false),
                    _ => throw new InvalidOperationException("bootstrap direction is neither repo wins nor folder wins")
                };
            }

            RequireUndecided(direction);
            return plan switch
            {
                BootstrapPlan.CheckoutRemote => await CheckoutRemoteAsync(cancellationToken).ConfigureAwait(false),
                BootstrapPlan.PublishFolder => await PublishFolderAsync(null, cancellationToken).ConfigureAwait(false),
                BootstrapPlan.WatchEmpty => await WatchEmptyAsync(cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("bootstrap plan is neither checkout nor publish")
            };
        });

    private async Task<Result<BootstrapOutcome>> CheckoutRemoteAsync(CancellationToken cancellationToken)
    {
        await PrepareRepositoryAsync(cancellationToken).ConfigureAwait(false);
        RequireOk(await remote.Fetch(cancellationToken).ConfigureAwait(false));
        RequireOk(await local.Checkout(config.WatchedFolder, config.Branch, $"{OriginName}/{config.Branch}", cancellationToken).ConfigureAwait(false));
        return await FinishAsync(BootstrapFlow.CheckoutRemote, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<BootstrapOutcome>> WatchEmptyAsync(CancellationToken cancellationToken)
    {
        await PrepareRepositoryAsync(cancellationToken).ConfigureAwait(false);
        var rules = LoadRules();
        RequireOk(await local.ApplyExcludes(config.WatchedFolder, rules.Patterns, cancellationToken).ConfigureAwait(false));
        RequireOk(await local.CommitEmpty(config.WatchedFolder, InitialCommitMessage, cancellationToken).ConfigureAwait(false));
        RequireOk(await remote.Push(config.Branch, cancellationToken).ConfigureAwait(false));
        return await FinishAsync(BootstrapFlow.WatchEmpty, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<BootstrapOutcome>> TrackRemoteAsync(CancellationToken cancellationToken)
    {
        await PrepareRepositoryAsync(cancellationToken).ConfigureAwait(false);
        RequireOk(await remote.Fetch(cancellationToken).ConfigureAwait(false));
        return await FinishAsync(BootstrapFlow.RepoTracked, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<BootstrapOutcome>> PublishFolderAsync(string? expectedRemoteSha, CancellationToken cancellationToken)
    {
        await PrepareRepositoryAsync(cancellationToken).ConfigureAwait(false);
        var rules = LoadRules();
        RequireOk(await local.ApplyExcludes(config.WatchedFolder, rules.Patterns, cancellationToken).ConfigureAwait(false));
        RequireOk(await local.StageAll(config.WatchedFolder, cancellationToken).ConfigureAwait(false));
        var emptiness = await local.IsWorkingTreeEmpty(config.WatchedFolder, cancellationToken).ConfigureAwait(false);
        if (emptiness is not Result<bool>.Ok probe)
        {
            throw new GitOperationException(((Result<bool>.Fail)emptiness).Error);
        }

        if (probe.Value)
        {
            if (expectedRemoteSha is null)
            {
                return new Result<BootstrapOutcome>.Fail(
                    SyncError.Invalid("watched folder has no committable files after ignore rules"));
            }

            return await FinishAsync(BootstrapFlow.FolderInSync, cancellationToken).ConfigureAwait(false);
        }

        RequireOk(await local.Commit(config.WatchedFolder, InitialCommitMessage, cancellationToken).ConfigureAwait(false));
        if (expectedRemoteSha is { } lease)
        {
            RequireOk(await remote.PushForceWithLease(config.Branch, lease, cancellationToken).ConfigureAwait(false));
        }
        else
        {
            RequireOk(await remote.Push(config.Branch, cancellationToken).ConfigureAwait(false));
        }

        return await FinishAsync(BootstrapFlow.PublishFolder, cancellationToken).ConfigureAwait(false);
    }

    private async Task PrepareRepositoryAsync(CancellationToken cancellationToken)
    {
        RequireOk(await local.Init(config.WatchedFolder, config.Branch, config.Identity, cancellationToken).ConfigureAwait(false));
        RequireOk(await local.AddRemote(config.WatchedFolder, OriginName, config.RemoteUrl, cancellationToken).ConfigureAwait(false));
    }

    private async Task<Result<BootstrapOutcome>> FinishAsync(BootstrapFlow flow, CancellationToken cancellationToken)
    {
        var head = await local.HeadInfo(config.WatchedFolder, cancellationToken).ConfigureAwait(false);
        return head switch
        {
            Result<LocalHead>.Ok info => new Result<BootstrapOutcome>.Ok(
                new BootstrapOutcome.Completed(flow, config.Branch, info.Value.Sha)),
            Result<LocalHead>.Fail failure => new Result<BootstrapOutcome>.Fail(failure.Error),
            _ => throw new InvalidOperationException("head outcome is neither ok nor fail")
        };
    }

    private IgnoreRules LoadRules()
    {
        try
        {
            return IgnoreRules.Load(config.WatchedFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GitOperationException(new SyncError("unavailable", "syncignore file could not be read"));
        }
    }

    private bool FolderHasContent()
    {
        if (!Directory.Exists(config.WatchedFolder))
        {
            throw new GitOperationException(SyncError.NotFound("watched folder does not exist"));
        }

        return Directory.EnumerateFileSystemEntries(config.WatchedFolder)
            .Any(entry => Path.GetFileName(entry) is not (VcsDirectoryName or StateDirectoryName));
    }

    private static int CountLocalFiles(string folder)
    {
        var count = 0;
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                if (Path.GetFileName(entry) is VcsDirectoryName or StateDirectoryName)
                {
                    continue;
                }

                if (File.Exists(entry))
                {
                    count++;
                    continue;
                }

                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) == 0)
                {
                    count += CountLocalFiles(entry);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GitOperationException(new SyncError("unavailable", $"watched folder contents could not be counted in '{folder}'"));
        }

        return count;
    }

    private static void RequireOk<T>(Result<T> outcome)
    {
        if (outcome is Result<T>.Fail failure)
        {
            throw new GitOperationException(failure.Error);
        }
    }

    private static void RequireUndecided(BootstrapDirection? direction)
    {
        if (direction is not null)
        {
            throw new GitOperationException(SyncError.Invalid("a chosen direction applies only when both sides carry content"));
        }
    }
}
