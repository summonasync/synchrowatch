using Microsoft.Extensions.DependencyInjection;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;

namespace Syncrowatch.App;

public static class CompositionRoot
{
    public static IServiceProvider Build()
    {
        var dataDirectory = SyncrowatchPaths.UserDataDirectory();
        var services = new ServiceCollection();
        services.AddSingleton<ISyncConfigStore>(new JsonSyncConfigStore(dataDirectory));
        services.AddSingleton<ICredentialStore>(new CredentialStore(dataDirectory));
        return services.BuildServiceProvider();
    }
}
