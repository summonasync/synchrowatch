using Synchro.Core;

namespace Syncrowatch.Core.Transport;

public static class GitFailure
{
    private static readonly string[] UnreachableMarkers =
    [
        "could not resolve host",
        "failed to connect",
        "connection refused",
        "connection timed out",
        "network is unreachable"
    ];

    private static readonly string[] AuthMarkers =
    [
        "authentication failed",
        "could not read username",
        "could not read password",
        "invalid username or password",
        "terminal prompts disabled",
        "http 401",
        "http 403"
    ];

    public static GitTransportException Classify(GitCommandResult result, string operation)
    {
        var stderr = result.StandardError.Trim();
        var message = stderr.Length > 0
            ? stderr
            : $"git {operation} exited with code {result.ExitCode}";

        if (ContainsAny(message, UnreachableMarkers))
        {
            return new RemoteUnreachableException(message);
        }

        if (ContainsAny(message, AuthMarkers))
        {
            return new GitAuthenticationException(message);
        }

        if (message.Contains("fast-forward", StringComparison.OrdinalIgnoreCase)
            || message.Contains("failed to push some refs", StringComparison.OrdinalIgnoreCase))
        {
            return new GitOperationException(SyncError.Conflict(message));
        }

        if (message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return new GitOperationException(SyncError.NotFound(message));
        }

        return new GitOperationException(new SyncError("git_error", message));
    }

    public static void EnsureSuccess(GitCommandResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw Classify(result, operation);
        }
    }

    private static bool ContainsAny(string text, string[] markers) =>
        markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
