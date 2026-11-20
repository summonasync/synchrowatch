using Synchro.Core;

namespace Syncrowatch.Core.Config;

public interface IAppPreferencesStore
{
    Task<Result<AppPreferences>> Load(CancellationToken cancellationToken);

    Task<Result<AppPreferences>> Save(AppPreferences preferences, CancellationToken cancellationToken);
}
