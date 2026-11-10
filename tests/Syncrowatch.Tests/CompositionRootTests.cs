using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Syncrowatch.App;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
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
    public void Build_AfterConfigStoresLanded_RegistersStoresOnly()
    {
        var provider = CompositionRoot.Build();

        provider.GetService<ISyncConfigStore>().ShouldNotBeNull();
        provider.GetService<ICredentialStore>().ShouldNotBeNull();
        provider.GetService<ISyncEngine>().ShouldBeNull();
        provider.GetService<IRemoteRepository>().ShouldBeNull();
    }
}
