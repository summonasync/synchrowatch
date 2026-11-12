using Synchro.Core;

namespace Syncrowatch.Core.Transport;

public abstract class GitTransportException : Exception
{
    public SyncError Error { get; }

    protected GitTransportException(SyncError error, Exception? innerException = null)
        : base(error.Message, innerException) => Error = error;
}

public sealed class GitMissingException : GitTransportException
{
    public GitMissingException(Exception? innerException = null)
        : base(new SyncError("git_missing", "git executable was not found"), innerException)
    {
    }
}

public sealed class GitTimeoutException : GitTransportException
{
    public GitTimeoutException(TimeSpan timeout, Exception? innerException = null)
        : base(new SyncError("timeout", $"git command did not finish within {timeout.TotalSeconds:0} seconds"), innerException)
    {
    }
}

public sealed class PublicRepositoryException : GitTransportException
{
    public PublicRepositoryException(string url)
        : base(new SyncError("public_repo", $"repository {url} is publicly readable; sync only accepts private repositories"))
    {
    }
}

public sealed class RemoteUnreachableException : GitTransportException
{
    public RemoteUnreachableException(string message, Exception? innerException = null)
        : base(new SyncError("unreachable", message), innerException)
    {
    }
}

public sealed class GitAuthenticationException : GitTransportException
{
    public GitAuthenticationException(string message, Exception? innerException = null)
        : base(new SyncError("auth", message), innerException)
    {
    }
}

public sealed class GitCredentialUnavailableException : GitTransportException
{
    public GitCredentialUnavailableException(SyncError error)
        : base(error)
    {
    }
}

public sealed class GitOperationException : GitTransportException
{
    public GitOperationException(SyncError error, Exception? innerException = null)
        : base(error, innerException)
    {
    }
}
