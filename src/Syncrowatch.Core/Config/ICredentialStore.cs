using Synchro.Core;
using Syncrowatch.Core;

namespace Syncrowatch.Core.Config;

public interface ICredentialStore
{
    Task<Result<PersonalAccessToken>> Read(CancellationToken cancellationToken);

    Task<Result<PersonalAccessToken>> Write(PersonalAccessToken token, CancellationToken cancellationToken);

    Task<Result<Unit>> Delete(CancellationToken cancellationToken);
}
