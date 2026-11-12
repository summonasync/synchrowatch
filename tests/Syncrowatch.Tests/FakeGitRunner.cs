using Syncrowatch.Core.Transport;

namespace Syncrowatch.Tests;

public sealed class FakeGitRunner(Func<GitInvocation, GitCommandResult>? responder = null) : IGitRunner
{
    private readonly Func<GitInvocation, GitCommandResult> _responder =
        responder ?? (_ => new GitCommandResult(0, string.Empty, string.Empty));

    public List<GitInvocation> Invocations { get; } = [];

    public Task<GitCommandResult> RunAsync(GitInvocation invocation, CancellationToken cancellationToken)
    {
        Invocations.Add(invocation);
        return Task.FromResult(_responder(invocation));
    }

    public bool SawAuthenticatedCall() =>
        Invocations.Any(invocation => invocation.Environment.ContainsKey(CredentialInjector.PasswordVariable));
}
