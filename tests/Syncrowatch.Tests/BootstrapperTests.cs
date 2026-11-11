using NSubstitute;
using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Bootstrap;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

[Collection("git-config-sandbox")]
public class BootstrapperTests
{
    private const string StandInUrl = "https://syncrowatch.test/stand-in.git";
    private const string DummyToken = "ghp_roundtrip_dummy_5f4e3d2c";

    [Theory]
    [InlineData("file:///tmp/repo.git")]
    [InlineData("-oProxyCommand=evil")]
    [InlineData("ftp://example.com/repo.git")]
    [InlineData("https://localhost")]
    [InlineData("")]
    public async Task Assess_UnacceptableRemoteUrl_FailsBeforeAnyGitCall(string remoteUrl)
    {
        using var harness = SubstituteHarness.Create(remoteUrl);

        var outcome = await harness.Bootstrapper.Assess(CancellationToken.None);

        outcome.ShouldBeOfType<Result<BootstrapPlan>.Fail>().Error.Code.ShouldBe("invalid");
        harness.Remote.ReceivedCalls().ShouldBeEmpty();
        harness.Local.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("file:///tmp/repo.git")]
    [InlineData("-oProxyCommand=evil")]
    public async Task Execute_UnacceptableRemoteUrl_FailsBeforeAnyGitCall(string remoteUrl)
    {
        using var harness = SubstituteHarness.Create(remoteUrl);

        var outcome = await harness.Bootstrapper.Execute(new BootstrapPlan.CheckoutRemote(), null, CancellationToken.None);

        outcome.ShouldBeOfType<Result<BootstrapOutcome>.Fail>().Error.Code.ShouldBe("invalid");
        harness.Remote.ReceivedCalls().ShouldBeEmpty();
        harness.Local.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Assess_MissingWatchedFolder_FailsAsNotFound()
    {
        using var harness = SubstituteHarness.Create(StandInUrl);
        Directory.Delete(harness.FolderPath);

        var outcome = await harness.Bootstrapper.Assess(CancellationToken.None);

        outcome.ShouldBeOfType<Result<BootstrapPlan>.Fail>().Error.Code.ShouldBe("not_found");
        harness.Remote.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Assess_EmptyFolderAndSeededRepo_PicksCheckoutRemoteSilently()
    {
        using var standIn = new StandIn(seededRepo: true);

        var outcome = await standIn.Bootstrapper.Assess(CancellationToken.None);

        outcome.ShouldBeOfType<Result<BootstrapPlan>.Ok>().Value
            .ShouldBeOfType<BootstrapPlan.CheckoutRemote>();
    }

    [Fact]
    public async Task Assess_EmptyFolderAndEmptyRepo_FailsAsDegenerate()
    {
        using var standIn = new StandIn(seededRepo: false);

        var outcome = await standIn.Bootstrapper.Assess(CancellationToken.None);

        outcome.ShouldBeOfType<Result<BootstrapPlan>.Fail>().Error.Code.ShouldBe("invalid");
    }

    [Fact]
    public async Task Assess_NonEmptyFolderAndEmptyRepo_PicksPublishFolder()
    {
        using var standIn = new StandIn(seededRepo: false);
        File.WriteAllText(Path.Combine(standIn.FolderPath, "notes.txt"), "local work");

        var outcome = await standIn.Bootstrapper.Assess(CancellationToken.None);

        outcome.ShouldBeOfType<Result<BootstrapPlan>.Ok>().Value
            .ShouldBeOfType<BootstrapPlan.PublishFolder>();
    }

    [Fact]
    public async Task Execute_CheckoutRemote_WorkingTreeEqualsRepoContent()
    {
        using var standIn = new StandIn(seededRepo: true);
        var remoteTip = GitCli.Head(standIn.BarePath);

        var plan = Ok(await standIn.Bootstrapper.Assess(CancellationToken.None));
        var outcome = await standIn.Bootstrapper.Execute(plan, null, CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<BootstrapOutcome>.Ok>();
        var completed = ok.Value.ShouldBeOfType<BootstrapOutcome.Completed>();
        completed.Flow.ShouldBe(BootstrapFlow.CheckoutRemote);
        completed.Branch.ShouldBe("main");
        completed.Sha.ShouldBe(remoteTip);
        File.ReadAllText(Path.Combine(standIn.FolderPath, "notes.txt")).ShouldBe("seed from peer");
        GitCli.Head(standIn.FolderPath).ShouldBe(remoteTip);
        GitCli.Run(standIn.FolderPath, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
        standIn.Environment.AssertGlobalConfigIsolated();
    }

    [Fact]
    public async Task Execute_PublishFolder_InitialCommitHonorsSyncignore()
    {
        using var standIn = new StandIn(seededRepo: false);
        WriteMixedFolder(standIn.FolderPath);

        var plan = Ok(await standIn.Bootstrapper.Assess(CancellationToken.None));
        var outcome = await standIn.Bootstrapper.Execute(plan, null, CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<BootstrapOutcome>.Ok>();
        ok.Value.ShouldBeOfType<BootstrapOutcome.Completed>().Flow.ShouldBe(BootstrapFlow.PublishFolder);
        var tracked = TrackedFiles(standIn.FolderPath);
        tracked.ShouldContain("keep.txt");
        tracked.ShouldContain(".syncignore");
        tracked.ShouldNotContain(name => name.StartsWith("logs/") || name.StartsWith("bin/"));
        tracked.ShouldNotContain(name => name == ".DS_Store" || name.EndsWith(".tmp"));
        GitCli.Head(standIn.BarePath).ShouldBe(GitCli.Head(standIn.FolderPath));
        standIn.Environment.AssertGlobalConfigIsolated();
    }

    [Fact]
    public async Task Execute_BothNonEmptyWithoutDirection_ReportsChoiceAndMutatesNothing()
    {
        using var standIn = new StandIn(seededRepo: true);
        File.WriteAllText(Path.Combine(standIn.FolderPath, "local.txt"), "local content");
        var bareTipBefore = GitCli.Head(standIn.BarePath);
        var snapshotBefore = Snapshot(standIn.FolderPath);

        var plan = Ok(await standIn.Bootstrapper.Assess(CancellationToken.None));
        var decide = plan.ShouldBeOfType<BootstrapPlan.Decide>();
        var outcome = await standIn.Bootstrapper.Execute(plan, null, CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<BootstrapOutcome>.Ok>();
        var choice = ok.Value.ShouldBeOfType<BootstrapOutcome.ChoiceNeeded>();
        choice.Options.Branch.ShouldBe("main");
        choice.Options.RemoteSha.ShouldBe(bareTipBefore);
        choice.Options.LocalFileCount.ShouldBe(1);
        decide.Options.ShouldBe(choice.Options);
        Snapshot(standIn.FolderPath).ShouldBe(snapshotBefore);
        Directory.Exists(Path.Combine(standIn.FolderPath, ".git")).ShouldBeFalse();
        GitCli.Head(standIn.BarePath).ShouldBe(bareTipBefore);
    }

    [Fact]
    public async Task Execute_BothNonEmptyFolderWins_ForcePublishesOverRemote()
    {
        using var standIn = new StandIn(seededRepo: true);
        File.WriteAllText(Path.Combine(standIn.FolderPath, "local.txt"), "local content");
        File.WriteAllText(Path.Combine(standIn.FolderPath, ".syncignore"), "scratch/\n");
        var bareTipBefore = GitCli.Head(standIn.BarePath);

        var plan = Ok(await standIn.Bootstrapper.Assess(CancellationToken.None));
        var outcome = await standIn.Bootstrapper.Execute(plan, BootstrapDirection.FolderWins, CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<BootstrapOutcome>.Ok>();
        ok.Value.ShouldBeOfType<BootstrapOutcome.Completed>().Flow.ShouldBe(BootstrapFlow.PublishFolder);
        GitCli.Head(standIn.BarePath).ShouldNotBe(bareTipBefore);
        GitCli.Head(standIn.BarePath).ShouldBe(GitCli.Head(standIn.FolderPath));
        TrackedFiles(standIn.FolderPath).ShouldNotContain(name => name.StartsWith("scratch/"));
        standIn.Environment.AssertGlobalConfigIsolated();
    }

    [Fact]
    public async Task Execute_BothNonEmptyRepoWins_TracksRemoteWithoutTouchingFiles()
    {
        using var standIn = new StandIn(seededRepo: true);
        File.WriteAllText(Path.Combine(standIn.FolderPath, "local.txt"), "local content");
        var remoteTip = GitCli.Head(standIn.BarePath);
        var snapshotBefore = Snapshot(standIn.FolderPath);

        var plan = Ok(await standIn.Bootstrapper.Assess(CancellationToken.None));
        var outcome = await standIn.Bootstrapper.Execute(plan, BootstrapDirection.RepoWins, CancellationToken.None);

        var ok = outcome.ShouldBeOfType<Result<BootstrapOutcome>.Ok>();
        var completed = ok.Value.ShouldBeOfType<BootstrapOutcome.Completed>();
        completed.Flow.ShouldBe(BootstrapFlow.RepoTracked);
        completed.Sha.ShouldBeEmpty();
        Snapshot(standIn.FolderPath).ShouldBe(snapshotBefore);
        GitCli.Head(standIn.BarePath).ShouldBe(remoteTip);
        GitCli.Run(standIn.FolderPath, "for-each-ref", "--format=%(refname)", "refs/remotes/origin/")
            .Output.ShouldContain("refs/remotes/origin/main");
        var head = Ok(await standIn.Local.HeadInfo(standIn.FolderPath, CancellationToken.None));
        head.Branch.ShouldBe("main");
        head.HasCommits.ShouldBeFalse();
        standIn.Environment.AssertGlobalConfigIsolated();
    }

    [Fact]
    public async Task Execute_DirectPlanWithChosenDirection_IsRefused()
    {
        using var harness = SubstituteHarness.Create(StandInUrl);

        var outcome = await harness.Bootstrapper.Execute(
            new BootstrapPlan.PublishFolder(), BootstrapDirection.FolderWins, CancellationToken.None);

        outcome.ShouldBeOfType<Result<BootstrapOutcome>.Fail>().Error.Code.ShouldBe("invalid");
        harness.Remote.ReceivedCalls().ShouldBeEmpty();
        harness.Local.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Assess_ListBranchFailure_PropagatesAsFailure()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.ListBranch(Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<RemoteRef>>.Fail(SyncError.NotFound("repository is gone")));
        using var harness = SubstituteHarness.Create(StandInUrl, remote);

        var outcome = await harness.Bootstrapper.Assess(CancellationToken.None);

        outcome.ShouldBeOfType<Result<BootstrapPlan>.Fail>().Error.Code.ShouldBe("not_found");
    }

    [Fact]
    public async Task Execute_PublishFolder_OnlyIgnoredContent_FailsWithoutCommitOrPush()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.ListBranch(Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<RemoteRef>>.Ok([]));
        var local = PreparedLocal();
        local.IsWorkingTreeEmpty(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<bool>.Ok(true));
        using var harness = SubstituteHarness.Create(StandInUrl, remote, local);
        File.WriteAllText(Path.Combine(harness.FolderPath, "Thumbs.db"), "junk");

        var plan = Ok(await harness.Bootstrapper.Assess(CancellationToken.None));
        var outcome = await harness.Bootstrapper.Execute(plan, null, CancellationToken.None);

        outcome.ShouldBeOfType<Result<BootstrapOutcome>.Fail>().Error.Code.ShouldBe("invalid");
        await local.DidNotReceive().Commit(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await remote.DidNotReceive().Push(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await remote.DidNotReceive().PushForceWithLease(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_CheckoutRemote_WhenFetchFails_StopsBeforeCheckout()
    {
        var remote = Substitute.For<IRemoteRepository>();
        remote.ListBranch(Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<RemoteRef>>.Ok([new RemoteRef("main", "abc123")]));
        remote.Fetch(Arg.Any<CancellationToken>())
            .Returns(new Result<IReadOnlyList<RemoteRef>>.Fail(SyncError.Conflict("fetch hit a wall")));
        var local = PreparedLocal();
        using var harness = SubstituteHarness.Create(StandInUrl, remote, local);

        var plan = Ok(await harness.Bootstrapper.Assess(CancellationToken.None));
        var outcome = await harness.Bootstrapper.Execute(plan, null, CancellationToken.None);

        outcome.ShouldBeOfType<Result<BootstrapOutcome>.Fail>().Error.Code.ShouldBe("conflict");
        await local.DidNotReceive().Checkout(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static ILocalGit PreparedLocal()
    {
        var local = Substitute.For<ILocalGit>();
        local.Init(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CommitIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new Result<Unit>.Ok(Unit.Value));
        local.AddRemote(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<Unit>.Ok(Unit.Value));
        local.ApplyExcludes(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Result<Unit>.Ok(Unit.Value));
        local.StageAll(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Result<Unit>.Ok(Unit.Value));
        return local;
    }

    private static BootstrapPlan Ok(Result<BootstrapPlan> outcome) =>
        outcome switch
        {
            Result<BootstrapPlan>.Ok ok => ok.Value,
            Result<BootstrapPlan>.Fail failure => throw new InvalidOperationException($"assessment failed: {failure.Error.Message}"),
            _ => throw new InvalidOperationException("assessment outcome is neither ok nor fail")
        };

    private static LocalHead Ok(Result<LocalHead> outcome) =>
        outcome switch
        {
            Result<LocalHead>.Ok ok => ok.Value,
            Result<LocalHead>.Fail failure => throw new InvalidOperationException($"head read failed: {failure.Error.Message}"),
            _ => throw new InvalidOperationException("head outcome is neither ok nor fail")
        };

    private static void WriteMixedFolder(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "logs"));
        Directory.CreateDirectory(Path.Combine(folder, "bin"));
        File.WriteAllText(Path.Combine(folder, "keep.txt"), "committed");
        File.WriteAllText(Path.Combine(folder, ".syncignore"), "logs/\n");
        File.WriteAllText(Path.Combine(folder, "logs", "debug.log"), "ignored by .syncignore");
        File.WriteAllText(Path.Combine(folder, "bin", "tool.dll"), "ignored by defaults");
        File.WriteAllText(Path.Combine(folder, ".DS_Store"), "ignored junk");
        File.WriteAllText(Path.Combine(folder, "swap.tmp"), "ignored junk");
    }

    private static string[] TrackedFiles(string folder) =>
        GitCli.Run(folder, "ls-files").Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static Dictionary<string, string> Snapshot(string folder)
    {
        var files = new Dictionary<string, string>();
        Walk(folder, folder, files);
        return files;
    }

    private static void Walk(string root, string current, Dictionary<string, string> files)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(current))
        {
            if (Path.GetFileName(entry) == ".git")
            {
                continue;
            }

            if (File.Exists(entry))
            {
                files[Path.GetRelativePath(root, entry)] = File.ReadAllText(entry);
            }
            else
            {
                Walk(root, entry, files);
            }
        }
    }

    private sealed class SubstituteHarness : IDisposable
    {
        private SubstituteHarness(string folder, string remoteUrl, IRemoteRepository remote, ILocalGit local)
        {
            FolderPath = folder;
            Remote = remote;
            Local = local;
            Bootstrapper = new Bootstrapper(
                new SyncConfig
                {
                    WatchedFolder = folder,
                    RemoteUrl = remoteUrl,
                    Branch = "main",
                    Mode = SyncRunMode.Auto,
                    Identity = CommitIdentity.Default
                },
                remote,
                local);
        }

        public string FolderPath { get; }

        public IRemoteRepository Remote { get; }

        public ILocalGit Local { get; }

        public Bootstrapper Bootstrapper { get; }

        public static SubstituteHarness Create(
            string remoteUrl,
            IRemoteRepository? remote = null,
            ILocalGit? local = null) =>
            new(TempDir.Create(), remoteUrl, remote ?? Substitute.For<IRemoteRepository>(), local ?? Substitute.For<ILocalGit>());

        public void Dispose()
        {
            if (Directory.Exists(FolderPath))
            {
                Directory.Delete(FolderPath, true);
            }
        }
    }

    private sealed class StandIn : IDisposable
    {
        public StandIn(bool seededRepo)
        {
            Environment = new GitConfigSandbox();
            Root = TempDir.Create();
            BarePath = Path.Combine(Root, "origin.git");
            PeerPath = Path.Combine(Root, "peer");
            FolderPath = Path.Combine(Root, "watched");
            Environment.WriteGlobalConfig($"""
                [url "{BarePath}"]
                	insteadOf = {StandInUrl}
                """);
            GitCli.Ensure(Root, "init", "--bare", "-b", "main", BarePath);
            if (seededRepo)
            {
                GitCli.Ensure(Root, "clone", BarePath, PeerPath);
                GitCli.ConfigureIdentity(PeerPath);
                File.WriteAllText(Path.Combine(PeerPath, "notes.txt"), "seed from peer");
                GitCli.CommitAll(PeerPath, "seed");
                GitCli.Push(PeerPath, "origin", "main");
            }

            Directory.CreateDirectory(FolderPath);
            var config = new SyncConfig
            {
                WatchedFolder = FolderPath,
                RemoteUrl = StandInUrl,
                Branch = "main",
                Mode = SyncRunMode.Auto,
                Identity = CommitIdentity.Default
            };
            var credentials = Substitute.For<ICredentialStore>();
            credentials.Read(Arg.Any<CancellationToken>())
                .Returns(new Result<PersonalAccessToken>.Ok(new PersonalAccessToken(DummyToken)));
            Local = new GitLocal(new GitProcess());
            Bootstrapper = new Bootstrapper(config, new GitHubRepository(config, credentials, new GitProcess()), Local);
        }

        public GitConfigSandbox Environment { get; }

        public string Root { get; }

        public string BarePath { get; }

        public string PeerPath { get; }

        public string FolderPath { get; }

        public GitLocal Local { get; }

        public Bootstrapper Bootstrapper { get; }

        public void Dispose()
        {
            Environment.Dispose();
            Directory.Delete(Root, true);
        }
    }
}
