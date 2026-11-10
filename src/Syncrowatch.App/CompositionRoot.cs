using Microsoft.Extensions.DependencyInjection;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Transport;

namespace Syncrowatch.App;

public static class CompositionRoot
{
    public static IServiceProvider Build()
    {
        var dataDirectory = SyncrowatchPaths.UserDataDirectory();
        var credentials = new CredentialStore(dataDirectory);
        var services = new ServiceCollection();
        services.AddSingleton<ISyncConfigStore>(new JsonSyncConfigStore(dataDirectory));
        services.AddSingleton<ICredentialStore>(credentials);
        services.AddSingleton<Func<SyncConfig, IRemoteRepository>>(config =>
            new GitHubRepository(config, credentials, new GitProcess()));
        return services.BuildServiceProvider();
    }
}
