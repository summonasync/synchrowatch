using Microsoft.Extensions.DependencyInjection;
using Syncrowatch.Core;
using Syncrowatch.Core.Bootstrap;
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
        services.AddSingleton<ILocalGit>(new GitLocal(new GitProcess()));
        services.AddSingleton<Func<SyncConfig, IBootstrapper>>(config =>
            new Bootstrapper(
                config,
                new GitHubRepository(config, credentials, new GitProcess()),
                new GitLocal(new GitProcess())));
        return services.BuildServiceProvider();
    }
}
