using Microsoft.Extensions.DependencyInjection;
using Syncrowatch.App.History;
using Syncrowatch.App.Notifications;
using Syncrowatch.App.Tray;
using Syncrowatch.App.Wizard;
using Syncrowatch.Core;
using Syncrowatch.Core.Bootstrap;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.Engine.Conflicts;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Relay;
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
        services.AddSingleton<ISyncEventStore>(new JsonSyncEventStore(dataDirectory));
        services.AddSingleton<SyncEventBus>();
        services.AddSingleton<ISyncEventBus>(sp => sp.GetRequiredService<SyncEventBus>());
        services.AddSingleton<IConflictStore>(new JsonConflictStore(dataDirectory));
        services.AddSingleton<IPendingConflictReader>(sp => new ConflictPendingReader(
            sp.GetRequiredService<IConflictStore>()));
        services.AddSingleton<IRelaySeenState>(new RelaySeenStateStore(dataDirectory));
        services.AddSingleton<Func<SyncConfig, IRelayWatcher>>(sp => config => new RelayWatcher(
            config,
            new GitHubRepository(config, credentials, new GitProcess()),
            new GitProcess(),
            sp.GetRequiredService<IRelaySeenState>(),
            sp.GetRequiredService<ISyncEventBus>()));
        services.AddSingleton<RelayPollDriver>();
        services.AddSingleton<TrayStateTracker>();
        services.AddSingleton(sp => new ExplicitSyncFeedback(sp.GetRequiredService<ISyncEventBus>()));
        services.AddSingleton(sp => new SyncNowRunner(
            sp.GetRequiredService<TrayStateTracker>(),
            sp.GetRequiredService<ExplicitSyncFeedback>()));
        services.AddSingleton<ITrayStateUpdater>(sp => new MarshalledTrayStateUpdater(
            new TrayStateTrackerUpdater(sp.GetRequiredService<TrayStateTracker>())));
        services.AddSingleton<IToastWindowFactory>(new ToastWindowFactory());
        services.AddSingleton(sp => new ToastService(
            sp.GetRequiredService<ISyncEventBus>(), sp.GetRequiredService<IToastWindowFactory>()));
        services.AddSingleton(sp => new TrayStateWatcher(
            sp.GetRequiredService<ISyncEventBus>(), sp.GetRequiredService<ITrayStateUpdater>()));
        services.AddSingleton<IHistoryView>(sp => new HistoryWindowView(sp));
        services.AddSingleton<Func<SyncConfig, IFileWatching>>(config => new FolderWatcher(config.WatchedFolder));
        services.AddSingleton<Func<SyncConfig, ISyncEngine>>(sp => config => new SyncLoop(
            config,
            sp.GetRequiredService<Func<SyncConfig, IRemoteRepository>>()(config),
            sp.GetRequiredService<ILocalGit>(),
            sp.GetRequiredService<ISyncEventStore>(),
            sp.GetRequiredService<ISyncEventBus>()));
        services.AddTransient(sp => new SetupWizardWindow(sp));
        services.AddTransient(sp => new HistoryWindow(sp));
        return services.BuildServiceProvider();
    }
}
