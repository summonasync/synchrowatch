using Synchro.Core;

namespace Syncrowatch.Core.Config;

public abstract class CredentialStoreException : Exception
{
    public SyncError Error { get; }

    protected CredentialStoreException(SyncError error, Exception? innerException = null)
        : base(error.Message, innerException) => Error = error;
}

public sealed class CredentialNotFoundException : CredentialStoreException
{
    public CredentialNotFoundException(string path, Exception? innerException = null)
        : base(SyncError.NotFound($"credential file is missing or empty at {path}"), innerException)
    {
    }
}

public sealed class CredentialStoreUnavailableException : CredentialStoreException
{
    public CredentialStoreUnavailableException(string message, Exception? innerException = null)
        : base(new SyncError("unavailable", message), innerException)
    {
    }
}
