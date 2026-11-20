using Synchro.Core;

namespace Syncrowatch.Core.Autostart;

public interface IAutostartManager
{
    Result<Unit> Enable(string exePath);

    Result<Unit> Disable();

    Result<bool> IsApplied();
}
