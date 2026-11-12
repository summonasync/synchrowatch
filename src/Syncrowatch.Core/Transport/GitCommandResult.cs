namespace Syncrowatch.Core.Transport;

public sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public byte[] OutputBytes { get; init; } = [];

    public bool Succeeded => ExitCode == 0;
}
