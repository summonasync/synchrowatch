using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Synchro.Core;
using Syncrowatch.App;
using Syncrowatch.App.Notifications;
using Syncrowatch.App.Tray;
using Syncrowatch.Core;
using Syncrowatch.Core.Bootstrap;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.Engine.Conflicts;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Relay;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public class CompositionRootTests
{
    [Fact]
    public void Build_WithoutDisplay_ReturnsServiceProvider()
    {
        var provider = CompositionRoot.Build();

        provider.ShouldNotBeNull();
    }

    [Fact]
    public void Build_RepeatedCalls_ProduceIndependentContainers()
    {
        var first = CompositionRoot.Build();
        var second = CompositionRoot.Build();

        first.ShouldNotBeSameAs(second);
    }

    [Fact]
    public void Build_RegistersStoresAndSyncSeams_WithoutDirectEngine()
    {
        var provider = CompositionRoot.Build();

        provider.GetService<ISyncConfigStore>().ShouldNotBeNull();
        provider.GetService<ICredentialStore>().ShouldNotBeNull();
        provider.GetService<ISyncEventBus>().ShouldNotBeNull();
        provider.GetService<ISyncEventStore>().ShouldNotBeNull();
        provider.GetService<IHistoryView>().ShouldNotBeNull();
        provider.GetService<ISyncEngine>().ShouldBeNull();
        provider.GetService<IRemoteRepository>().ShouldBeNull();
    }

    [Fact]
    public void Build_RegistersEngineAndWatcherFactories()
    {
        var provider = CompositionRoot.Build();

        provider.GetService<Func<SyncConfig, ISyncEngine>>().ShouldNotBeNull();
        provider.GetService<Func<SyncConfig, IFileWatching>>().ShouldNotBeNull();
    }

    [Fact]
    public void Build_RegistersRepositoryFactoryInsteadOfSingleton()
    {
        var provider = CompositionRoot.Build();

        provider.GetService<Func<SyncConfig, IRemoteRepository>>().ShouldNotBeNull();
        provider.GetService<IRemoteRepository>().ShouldBeNull();
    }

    [Fact]
    public void Build_RegistersLocalGitAndBootstrapFactory()
    {
        var provider = CompositionRoot.Build();

        provider.GetService<ILocalGit>().ShouldNotBeNull();
        provider.GetService<Func<SyncConfig, IBootstrapper>>().ShouldNotBeNull();
    }

    [Fact]
    public void Build_RepositoryFactory_BuildsInstancePerConfig()
    {
        var provider = CompositionRoot.Build();
        var factory = provider.GetService<Func<SyncConfig, IRemoteRepository>>()!;

        var first = factory(SampleConfig("https://github.com/acme/first.git"));
        var second = factory(SampleConfig("https://github.com/acme/second.git"));

        first.ShouldNotBeSameAs(second);
    }

    [Fact]
    public void Build_RegistersHistoryNotificationAndRelaySeams()
    {
        var provider = CompositionRoot.Build();

        provider.GetService<ISyncEventBus>().ShouldNotBeNull();
        provider.GetService<ISyncEventStore>().ShouldNotBeNull();
        provider.GetService<IPendingConflictReader>().ShouldNotBeNull();
        provider.GetService<IRelaySeenState>().ShouldNotBeNull();
        provider.GetService<ITrayStateUpdater>().ShouldNotBeNull();
        provider.GetService<ToastService>().ShouldNotBeNull();
        provider.GetService<TrayStateWatcher>().ShouldNotBeNull();
        provider.GetService<Func<SyncConfig, IRelayWatcher>>().ShouldNotBeNull();
        provider.GetService<IRelayWatcher>().ShouldBeNull();
    }

    [Fact]
    public void Build_WiresTrayHistoryAndConflictIntegrations()
    {
        var provider = CompositionRoot.Build();

        provider.GetService<TrayStateTracker>().ShouldNotBeNull();
        provider.GetService<RelayPollDriver>().ShouldNotBeNull();
        provider.GetService<IConflictStore>().ShouldNotBeNull();
        provider.GetService<IHistoryView>().ShouldBeOfType<HistoryWindowView>();

        var updater = provider.GetService<ITrayStateUpdater>();
        updater.ShouldBeOfType<MarshalledTrayStateUpdater>();
    }

    private static SyncConfig SampleConfig(string remoteUrl) => new()
    {
        WatchedFolder = Path.GetTempPath(),
        RemoteUrl = remoteUrl,
        Branch = "main",
        Mode = SyncRunMode.Auto
    };
}
