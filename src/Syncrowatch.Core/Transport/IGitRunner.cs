namespace Syncrowatch.Core.Transport;

public interface IGitRunner
{
    Task<GitCommandResult> RunAsync(GitInvocation invocation, CancellationToken cancellationToken);
}
