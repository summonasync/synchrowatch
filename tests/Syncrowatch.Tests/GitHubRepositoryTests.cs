using NSubstitute;
using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

[Collection("git-config-sandbox")]
public class GitHubRepositoryTests
{
    private const string DummyToken = "ghp_roundtrip_dummy_5f4e3d2c";
    private const string PrivateRepoUrl = "https://github.com/acme/private.git";

    [Fact]
    public async Task ValidateRepository_PublicProbeSucceeds_RefusesWithoutAnyAuthenticatedCall()
    {
        var runner = new FakeGitRunner(_ => new GitCommandResult(0, string.Empty, string.Empty));
        var repository = BuildRepository(runner, credentials: StoreWith(DummyToken));

        var outcome = await repository.ValidateRepository(CancellationToken.None);

        var fail = outcome.ShouldBeOfType<Result<RemoteValidation>.Fail>();
        fail.Error.Code.ShouldBe("public_repo");
        runner.Invocations.Count.ShouldBe(1);
        runner.SawAuthenticatedCall().ShouldBeFalse();
    }

    [Fact]
    public async Task ValidateRepository_UnreachableRemote_StopsBeforeAuthentication()
    {
        var runner = new FakeGitRunner(_ => new GitCommandResult(
            128, string.Empty, "fatal: unable to access 'https://github.com/acme/private.git/': Could not resolve host: github.com"));
        var repository = BuildRepository(runner, credentials: StoreWith(DummyToken));

        var outcome = await repository.ValidateRepository(CancellationToken.None);

        outcome.ShouldBeOfType<Result<RemoteValidation>.Fail>().Error.Code.ShouldBe("unreachable");
        runner.Invocations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ValidateRepository_PrivateRepo_AuthenticatesAndReadsDefaultBranch()
    {
        var runner = new FakeGitRunner(invocation => invocation.Environment.ContainsKey(CredentialInjector.PasswordVariable)
            ? new GitCommandResult(0, "ref: refs/heads/main\tHEAD\nabc123\tHEAD\n", string.Empty)
            : new GitCommandResult(128, string.Empty,
                "fatal: could not read Username for 'https://github.com': terminal prompts disabled"));
        var repository = BuildRepository(runner, credentials: StoreWith(DummyToken));

        var outcome = await repository.ValidateRepository(CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<RemoteValidation>.Ok>();
        ok.Value.Reachable.ShouldBeTrue();
        ok.Value.DefaultBranch.ShouldBe("main");
        runner.SawAuthenticatedCall().ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateRepository_AuthRejected_MapsAuthError()
    {
        var runner = new FakeGitRunner(invocation => invocation.Environment.ContainsKey(CredentialInjector.PasswordVariable)
            ? new GitCommandResult(128, string.Empty, "fatal: Authentication failed for 'https://github.com/acme/private.git/'")
            : new GitCommandResult(128, string.Empty,
                "fatal: could not read Username for 'https://github.com': terminal prompts disabled"));
        var repository = BuildRepository(runner, credentials: StoreWith(DummyToken));

        var outcome = await repository.ValidateRepository(CancellationToken.None);

        outcome.ShouldBeOfType<Result<RemoteValidation>.Fail>().Error.Code.ShouldBe("auth");
    }

    [Fact]
    public async Task ListBranch_HttpsRemote_TokenReachesEnvButNeverArgv()
    {
        string? askpassDuringRun = null;
        bool shimExistedDuringRun = false;
        var runner = new FakeGitRunner(invocation =>
        {
            askpassDuringRun = invocation.Environment.GetValueOrDefault("GIT_ASKPASS");
            shimExistedDuringRun = File.Exists(askpassDuringRun);
            return new GitCommandResult(0, "feedc0de\trefs/heads/main\n", string.Empty);
        });
        var repository = BuildRepository(runner, credentials: StoreWith(DummyToken));

        var outcome = await repository.ListBranch(CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<IReadOnlyList<RemoteRef>>.Ok>();
        ok.Value.Single().ShouldBe(new RemoteRef("main", "feedc0de"));
        askpassDuringRun.ShouldNotBeNull();
        shimExistedDuringRun.ShouldBeTrue();
        runner.Invocations.Single().Arguments.ShouldNotContain(argument => argument.Contains(DummyToken));
        Directory.Exists(Path.GetDirectoryName(askpassDuringRun)).ShouldBeFalse();
    }

    [Fact]
    public async Task Push_HttpsRemote_CarriesInjectedIdentityPerInvocation()
    {
        var runner = new FakeGitRunner(invocation => invocation.Arguments.Contains("rev-parse")
            ? new GitCommandResult(0, "def456\n", string.Empty)
            : new GitCommandResult(0, string.Empty, string.Empty));
        var repository = BuildRepository(runner, credentials: StoreWith(DummyToken));

        var outcome = await repository.Push("main", CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<SyncOutcome>.Ok>();
        ok.Value.ToSha.ShouldBe("def456");
        var push = runner.Invocations.Single(invocation => invocation.Arguments.Contains("push"));
        push.Arguments.ShouldNotContain(argument => argument.Contains(DummyToken));
        push.Arguments.ShouldContain("user.name=Syncrowatch");
        push.Arguments.ShouldContain("user.email=syncrowatch@users.noreply.github.com");
    }

    [Fact]
    public async Task RemoteCall_WithoutStoredToken_FailsBeforeAnyGitProcess()
    {
        var runner = new FakeGitRunner();
        var credentials = Substitute.For<ICredentialStore>();
        credentials.Read(Arg.Any<CancellationToken>())
            .Returns(new Result<PersonalAccessToken>.Fail(SyncError.NotFound("credential file is missing")));
        var repository = BuildRepository(runner, credentials: credentials);

        var outcome = await repository.ListBranch(CancellationToken.None);

        outcome.ShouldBeOfType<Result<IReadOnlyList<RemoteRef>>.Fail>().Error.Code.ShouldBe("not_found");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListBranch_ListsBareStandInBranches()
    {
        using var environment = new GitConfigSandbox();
        using var standIn = new BareStandIn();
        GitCli.Ensure(standIn.BarePath, "update-ref", "refs/heads/archive", GitCli.Head(standIn.PeerPath));

        var outcome = await standIn.Repository.ListBranch(CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<IReadOnlyList<RemoteRef>>.Ok>();
        ok.Value.Select(reference => reference.Name).ShouldBe(["archive", "main"]);
        ok.Value.Single(reference => reference.Name == "main").Sha.ShouldBe(GitCli.Head(standIn.PeerPath));
        environment.AssertRealGlobalConfigUnchanged();
    }

    [Fact]
    public async Task Fetch_UpdatesTrackingRefsWithoutMergingWorkRepo()
    {
        using var environment = new GitConfigSandbox();
        using var standIn = new BareStandIn();
        var workHeadBefore = GitCli.Head(standIn.WorkPath);
        File.WriteAllText(Path.Combine(standIn.PeerPath, "peer.txt"), "peer change");
        GitCli.CommitAll(standIn.PeerPath, "peer edit");
        GitCli.Push(standIn.PeerPath, "origin", "main");

        var outcome = await standIn.Repository.Fetch(CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<IReadOnlyList<RemoteRef>>.Ok>();
        ok.Value.Single(reference => reference.Name == "main").Sha.ShouldBe(GitCli.Head(standIn.BarePath));
        GitCli.Head(standIn.WorkPath).ShouldBe(workHeadBefore);
        environment.AssertRealGlobalConfigUnchanged();
    }

    [Fact]
    public async Task Pull_AfterPeerPush_FastForwardsWorkRepo()
    {
        using var environment = new GitConfigSandbox();
        using var standIn = new BareStandIn();
        var workHeadBefore = GitCli.Head(standIn.WorkPath);
        File.WriteAllText(Path.Combine(standIn.PeerPath, "peer.txt"), "peer change");
        GitCli.CommitAll(standIn.PeerPath, "peer edit");
        GitCli.Push(standIn.PeerPath, "origin", "main");
        var remoteTip = GitCli.Head(standIn.BarePath);

        var outcome = await standIn.Repository.Pull("main", CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<SyncOutcome>.Ok>();
        ok.Value.Branch.ShouldBe("main");
        ok.Value.FromSha.ShouldBe(workHeadBefore);
        ok.Value.ToSha.ShouldBe(remoteTip);
        GitCli.Head(standIn.WorkPath).ShouldBe(remoteTip);
        File.ReadAllText(Path.Combine(standIn.WorkPath, "peer.txt")).ShouldBe("peer change");
        environment.AssertRealGlobalConfigUnchanged();
    }

    [Fact]
    public async Task Pull_DirtyWorkTreeWithUntrackedConflict_MapsToConflict()
    {
        using var environment = new GitConfigSandbox();
        using var standIn = new BareStandIn();
        var workHeadBefore = GitCli.Head(standIn.WorkPath);
        File.WriteAllText(Path.Combine(standIn.WorkPath, "peer.txt"), "local unsynced copy");
        File.WriteAllText(Path.Combine(standIn.PeerPath, "peer.txt"), "peer edit");
        GitCli.CommitAll(standIn.PeerPath, "peer edit");
        GitCli.Push(standIn.PeerPath, "origin", "main");

        var outcome = await standIn.Repository.Pull("main", CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncOutcome>.Fail>().Error.Code.ShouldBe("conflict");
        GitCli.Head(standIn.WorkPath).ShouldBe(workHeadBefore);
        File.ReadAllText(Path.Combine(standIn.WorkPath, "peer.txt")).ShouldBe("local unsynced copy");
        environment.AssertRealGlobalConfigUnchanged();
    }

    [Fact]
    public async Task MergeFetchedHead_AfterDivergedPull_AutoMergesCleanTextAndListsFiles()
    {
        using var environment = new GitConfigSandbox();
        using var standIn = new BareStandIn();
        File.WriteAllText(Path.Combine(standIn.PeerPath, "notes.txt"), "alpha\nmiddle\nomega\n");
        GitCli.CommitAll(standIn.PeerPath, "normalize base");
        GitCli.Push(standIn.PeerPath, "origin", "main");
        (await standIn.Repository.Pull("main", CancellationToken.None))
            .ShouldBeOfType<Result<SyncOutcome>.Ok>();
        File.WriteAllText(Path.Combine(standIn.WorkPath, "notes.txt"), "ALPHA changed by work\nmiddle\nomega\n");
        GitCli.CommitAll(standIn.WorkPath, "work rewrites first line");
        var workHead = GitCli.Head(standIn.WorkPath);
        File.WriteAllText(Path.Combine(standIn.PeerPath, "notes.txt"), "alpha\nmiddle\nOMEGA appended by peer\n");
        GitCli.CommitAll(standIn.PeerPath, "peer appends last line");
        GitCli.Push(standIn.PeerPath, "origin", "main");
        (await standIn.Repository.Pull("main", CancellationToken.None))
            .ShouldBeOfType<Result<SyncOutcome>.Fail>().Error.Code.ShouldBe("conflict");

        var outcome = await standIn.Repository.MergeFetchedHead(CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<MergeOutcome>.Ok>();
        var merged = ok.Value.ShouldBeOfType<MergeOutcome.Merged>();
        merged.FromSha.ShouldBe(workHead);
        merged.ToSha.ShouldBe(GitCli.Head(standIn.WorkPath));
        merged.Files.ShouldBe(["notes.txt"]);
        File.ReadAllText(Path.Combine(standIn.WorkPath, "notes.txt"))
            .ShouldBe("ALPHA changed by work\nmiddle\nOMEGA appended by peer\n");
        int.Parse(GitCli.Run(standIn.WorkPath, "rev-list", "--count", "--merges", "HEAD").Output.Trim())
            .ShouldBe(1);
        environment.AssertRealGlobalConfigUnchanged();
    }

    [Fact]
    public async Task MergeFetchedHead_ConflictingLines_AbortsAndReturnsConflictedPaths()
    {
        using var environment = new GitConfigSandbox();
        using var standIn = new BareStandIn();
        File.WriteAllText(Path.Combine(standIn.PeerPath, "notes.txt"), "peer rewrote the seed\n");
        GitCli.CommitAll(standIn.PeerPath, "peer rewrites");
        GitCli.Push(standIn.PeerPath, "origin", "main");
        File.WriteAllText(Path.Combine(standIn.WorkPath, "notes.txt"), "work rewrote the seed\n");
        GitCli.CommitAll(standIn.WorkPath, "work rewrites");
        var workHead = GitCli.Head(standIn.WorkPath);
        (await standIn.Repository.Pull("main", CancellationToken.None))
            .ShouldBeOfType<Result<SyncOutcome>.Fail>().Error.Code.ShouldBe("conflict");

        var outcome = await standIn.Repository.MergeFetchedHead(CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<MergeOutcome>.Ok>();
        ok.Value.ShouldBeOfType<MergeOutcome.Conflicted>().ConflictedPaths.ShouldBe(["notes.txt"]);
        GitCli.Run(standIn.WorkPath, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
        GitCli.Head(standIn.WorkPath).ShouldBe(workHead);
        File.ReadAllText(Path.Combine(standIn.WorkPath, "notes.txt")).ShouldBe("work rewrote the seed\n");
        environment.AssertRealGlobalConfigUnchanged();
    }

    [Fact]
    public async Task MergeFetchedHead_CancelledMidMerge_AbortsAndRethrows()
    {
        var step = -1;
        var runner = new FakeGitRunner(_ =>
        {
            step++;
            return step switch
            {
                0 => new GitCommandResult(0, "shaBefore\n", string.Empty),
                1 => throw new OperationCanceledException(),
                _ => new GitCommandResult(0, string.Empty, string.Empty)
            };
        });
        var repository = BuildRepository(runner);

        await Should.ThrowAsync<OperationCanceledException>(
            () => repository.MergeFetchedHead(CancellationToken.None));

        runner.Invocations.ShouldContain(invocation => invocation.Arguments.Contains("--abort"));
    }

    [Fact]
    public async Task MergeFetchedHead_UnrelatedFailure_ClassifiesWithoutAborting()
    {
        var step = -1;
        var runner = new FakeGitRunner(_ =>
        {
            step++;
            return step switch
            {
                0 => new GitCommandResult(0, "shaBefore\n", string.Empty),
                1 => new GitCommandResult(128, string.Empty, "fatal: refusing to merge unrelated histories"),
                2 => new GitCommandResult(0, string.Empty, string.Empty),
                _ => new GitCommandResult(0, string.Empty, string.Empty)
            };
        });
        var repository = BuildRepository(runner);

        var outcome = await repository.MergeFetchedHead(CancellationToken.None);

        outcome.ShouldBeOfType<Result<MergeOutcome>.Fail>().Error.Code.ShouldBe("git_error");
        runner.Invocations.ShouldNotContain(invocation => invocation.Arguments.Contains("--abort"));
    }

    [Fact]
    public async Task Push_ToBareStandIn_MovesRemoteTipWithoutCredentialMaterial()
    {
        using var environment = new GitConfigSandbox();
        using var standIn = new BareStandIn();
        var remoteBefore = GitCli.Head(standIn.BarePath);
        File.WriteAllText(Path.Combine(standIn.WorkPath, "work.txt"), "work change");
        GitCli.CommitAll(standIn.WorkPath, "work edit");

        var outcome = await standIn.Repository.Push("main", CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<SyncOutcome>.Ok>();
        ok.Value.FromSha.ShouldBe(remoteBefore);
        ok.Value.ToSha.ShouldBe(GitCli.Head(standIn.WorkPath));
        GitCli.Head(standIn.BarePath).ShouldBe(GitCli.Head(standIn.WorkPath));

        var workConfig = File.ReadAllText(Path.Combine(standIn.WorkPath, ".git", "config"));
        workConfig.ShouldNotContain(DummyToken);
        workConfig.ShouldNotContain("x-access-token");
        workConfig.ShouldNotContain("credential");
        environment.AssertRealGlobalConfigUnchanged();
        await standIn.Credentials.DidNotReceive().Read(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PushForceWithLease_StaleExpectedSha_IsRefusedAndRemoteUntouched()
    {
        using var environment = new GitConfigSandbox();
        using var standIn = new BareStandIn();
        var staleSha = GitCli.Head(standIn.WorkPath);
        File.WriteAllText(Path.Combine(standIn.PeerPath, "peer.txt"), "peer advanced");
        GitCli.CommitAll(standIn.PeerPath, "peer edit");
        GitCli.Push(standIn.PeerPath, "origin", "main");
        var remoteTip = GitCli.Head(standIn.BarePath);
        File.WriteAllText(Path.Combine(standIn.WorkPath, "work.txt"), "local rewrite");
        GitCli.CommitAll(standIn.WorkPath, "rewrite");

        var outcome = await standIn.Repository.PushForceWithLease("main", staleSha, CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncOutcome>.Fail>().Error.Code.ShouldBe("conflict");
        GitCli.Head(standIn.BarePath).ShouldBe(remoteTip);
        environment.AssertRealGlobalConfigUnchanged();
    }

    public static TheoryData<string> Operations => new() { "validate", "fetch", "pull", "push", "force", "list" };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task DashLeadingRemoteUrl_RejectedBeforeAnyGitCall(string operation)
    {
        var runner = new FakeGitRunner();
        var repository = BuildRepository(runner, remoteUrl: "-smuggled/upload-pack-args");

        var code = await FailureCode(repository, operation);

        code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Push_LeadingDashBranch_RejectedAsInvalidRefName()
    {
        var runner = new FakeGitRunner();
        var repository = BuildRepository(runner);

        var outcome = await repository.Push("-upstream", CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncOutcome>.Fail>().Error.Code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Push_RefspecColonBranch_RejectedAsInvalidRefName()
    {
        var runner = new FakeGitRunner();
        var repository = BuildRepository(runner);

        var outcome = await repository.Push("main:secret", CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncOutcome>.Fail>().Error.Code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task PushForceWithLease_NonHexExpectedSha_RejectedAsInvalid()
    {
        var runner = new FakeGitRunner();
        var repository = BuildRepository(runner);

        var outcome = await repository.PushForceWithLease("main", "../escape", CancellationToken.None);

        outcome.ShouldBeOfType<Result<SyncOutcome>.Fail>().Error.Code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task PushForceWithLease_MatchingRemoteSha_MovesRemoteTip()
    {
        using var environment = new GitConfigSandbox();
        using var standIn = new BareStandIn();
        File.WriteAllText(Path.Combine(standIn.WorkPath, "work.txt"), "work change");
        GitCli.CommitAll(standIn.WorkPath, "work edit");
        var leaseSha = GitCli.Head(standIn.BarePath);

        var outcome = await standIn.Repository.PushForceWithLease("main", leaseSha, CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<SyncOutcome>.Ok>();
        ok.Value.FromSha.ShouldBe(leaseSha);
        ok.Value.ToSha.ShouldBe(GitCli.Head(standIn.WorkPath));
        GitCli.Head(standIn.BarePath).ShouldBe(GitCli.Head(standIn.WorkPath));
        environment.AssertRealGlobalConfigUnchanged();
    }

    private static async Task<string> FailureCode(GitHubRepository repository, string operation)
    {
        switch (operation)
        {
            case "validate":
                return CodeOf(await repository.ValidateRepository(CancellationToken.None));
            case "fetch":
                return CodeOf(await repository.Fetch(CancellationToken.None));
            case "pull":
                return CodeOf(await repository.Pull("main", CancellationToken.None));
            case "push":
                return CodeOf(await repository.Push("main", CancellationToken.None));
            case "force":
                return CodeOf(await repository.PushForceWithLease("main", "abc123", CancellationToken.None));
            default:
                return CodeOf(await repository.ListBranch(CancellationToken.None));
        }
    }

    private static string CodeOf<T>(Result<T> outcome) =>
        outcome is Result<T>.Fail failure ? failure.Error.Code : throw new InvalidOperationException("expected a failure outcome");

    private static GitHubRepository BuildRepository(
        IGitRunner runner,
        string? watchedFolder = null,
        ICredentialStore? credentials = null,
        string? remoteUrl = null) =>
        new(new SyncConfig
        {
            WatchedFolder = watchedFolder ?? Path.GetTempPath(),
            RemoteUrl = remoteUrl ?? PrivateRepoUrl,
            Branch = "main",
            Mode = SyncRunMode.Auto,
            Identity = CommitIdentity.Default
        },
            credentials ?? StoreWith(DummyToken),
            runner);

    private static ICredentialStore StoreWith(string token)
    {
        var store = Substitute.For<ICredentialStore>();
        store.Read(Arg.Any<CancellationToken>())
            .Returns(new Result<PersonalAccessToken>.Ok(new PersonalAccessToken(token)));
        return store;
    }

    private sealed class BareStandIn : IDisposable
    {
        public string Root { get; }
        public string BarePath { get; }
        public string PeerPath { get; }
        public string WorkPath { get; }
        public ICredentialStore Credentials { get; }
        public GitHubRepository Repository { get; }

        public BareStandIn()
        {
            Root = TempDir.Create();
            BarePath = Path.Combine(Root, "origin.git");
            PeerPath = Path.Combine(Root, "peer");
            WorkPath = Path.Combine(Root, "work");

            GitCli.Ensure(Root, "init", "--bare", "-b", "main", BarePath);
            GitCli.Ensure(Root, "clone", BarePath, "peer");
            GitCli.ConfigureIdentity(PeerPath);
            File.WriteAllText(Path.Combine(PeerPath, "notes.txt"), "seed from peer");
            GitCli.CommitAll(PeerPath, "seed");
            GitCli.Push(PeerPath, "origin", "main");
            GitCli.Ensure(Root, "clone", BarePath, "work");
            GitCli.ConfigureIdentity(WorkPath);

            Credentials = Substitute.For<ICredentialStore>();
            Credentials.Read(Arg.Any<CancellationToken>())
                .Returns(new Result<PersonalAccessToken>.Ok(new PersonalAccessToken(DummyToken)));
            Repository = new GitHubRepository(new SyncConfig
            {
                WatchedFolder = WorkPath,
                RemoteUrl = BarePath,
                Branch = "main",
                Mode = SyncRunMode.Auto,
                Identity = CommitIdentity.Default
            },
                Credentials,
                new GitProcess());
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
