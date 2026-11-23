using Syncrowatch.Core;

namespace Syncrowatch.Core.Bootstrap;

public interface IBootstrapper
{
    Task<Result<BootstrapPlan>> Assess(CancellationToken cancellationToken);

    Task<Result<BootstrapOutcome>> Execute(BootstrapPlan plan, BootstrapDirection? direction, CancellationToken cancellationToken);
}
