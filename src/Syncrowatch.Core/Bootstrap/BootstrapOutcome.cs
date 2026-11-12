namespace Syncrowatch.Core.Bootstrap;

public abstract record BootstrapOutcome
{
    public sealed record Completed(BootstrapFlow Flow, string Branch, string Sha) : BootstrapOutcome;

    public sealed record ChoiceNeeded(BootstrapOptions Options) : BootstrapOutcome;
}
