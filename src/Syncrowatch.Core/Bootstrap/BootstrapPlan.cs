namespace Syncrowatch.Core.Bootstrap;

public abstract record BootstrapPlan
{
    public sealed record CheckoutRemote : BootstrapPlan;

    public sealed record PublishFolder : BootstrapPlan;

    public sealed record Decide(BootstrapOptions Options) : BootstrapPlan;
}
