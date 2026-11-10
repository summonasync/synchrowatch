using Shouldly;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public class GitFailureTests
{
    private static GitCommandResult Failing(string stderr) => new(128, string.Empty, stderr);

    [Fact]
    public void Classify_ResolveHostFailure_IsUnreachable()
    {
        var ex = GitFailure.Classify(
            Failing("fatal: unable to access 'https://github.com/acme/repo.git/': Could not resolve host: github.com"),
            "probe");

        ex.ShouldBeOfType<RemoteUnreachableException>();
    }

    [Fact]
    public void Classify_PromptDisabled_IsAuthFlavored()
    {
        GitFailure.Classify(
                Failing("fatal: could not read Username for 'https://github.com': terminal prompts disabled"),
                "probe")
            .ShouldBeOfType<GitAuthenticationException>();
    }

    [Fact]
    public void Classify_AuthenticationFailed_IsAuthFlavored()
    {
        GitFailure.Classify(
                Failing("fatal: Authentication failed for 'https://github.com/acme/repo.git/'"),
                "push")
            .ShouldBeOfType<GitAuthenticationException>();
    }

    [Fact]
    public void Classify_MissingRepository_IsNotFound()
    {
        var ex = GitFailure.Classify(Failing("fatal: repository 'https://github.com/acme/none.git/' not found"), "validate");

        ex.Error.Code.ShouldBe("not_found");
    }

    [Fact]
    public void Classify_FastForwardBlocked_IsConflict()
    {
        var ex = GitFailure.Classify(Failing("fatal: Not possible to fast-forward, aborting."), "pull");

        ex.Error.Code.ShouldBe("conflict");
    }

    [Fact]
    public void Classify_PushRejected_IsConflict()
    {
        var ex = GitFailure.Classify(
            Failing("error: failed to push some refs to 'https://github.com/acme/repo.git'"), "push");

        ex.Error.Code.ShouldBe("conflict");
    }

    [Fact]
    public void Classify_UnknownStderr_IsGitError()
    {
        var ex = GitFailure.Classify(Failing("fatal: something exploded"), "fetch");

        ex.Error.Code.ShouldBe("git_error");
        ex.Error.Message.ShouldContain("something exploded");
    }

    [Fact]
    public void Classify_EmptyStderr_FallsBackToExitCode()
    {
        var ex = GitFailure.Classify(new GitCommandResult(69, string.Empty, string.Empty), "probe");

        ex.Error.Message.ShouldContain("exited with code 69");
    }

    [Fact]
    public void EnsureSuccess_ZeroExit_DoesNotThrow()
    {
        GitFailure.EnsureSuccess(new GitCommandResult(0, "ok", string.Empty), "validate");
    }
}
