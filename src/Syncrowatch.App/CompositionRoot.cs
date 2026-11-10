using Microsoft.Extensions.DependencyInjection;

namespace Syncrowatch.App;

public static class CompositionRoot
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();
        return services.BuildServiceProvider();
    }
}
