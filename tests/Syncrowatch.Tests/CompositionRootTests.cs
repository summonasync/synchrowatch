using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Syncrowatch.App;
using Syncrowatch.Core;
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
    public void Build_BeforeImplementationTasks_SeamsUnregistered()
    {
        var provider = CompositionRoot.Build();

        provider.GetService<ISyncEngine>().ShouldBeNull();
        provider.GetService<IRemoteRepository>().ShouldBeNull();
        provider.GetService<ISyncConfigStore>().ShouldBeNull();
    }
}
