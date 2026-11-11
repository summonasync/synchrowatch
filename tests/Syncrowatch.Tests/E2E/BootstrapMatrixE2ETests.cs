using Shouldly;
using Synchro.Core;
using Syncrowatch.Core.Bootstrap;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests.E2E;

[Collection("git-config-sandbox")]
public sealed class BootstrapMatrixE2ETests : IDisposable
{
    private const string StandInUrl = "https://synchr.e2e/matrix.git";
    private const string StandInToken = "ghp_e2e_matrix_1a2b3c4d5e6f";
    private static readonly CommitIdentity BindingIdentity = new()
    {
        Name = "Syncrowatch E2E",
        Email = "e2e-driver@users.noreply.github.com"
    };

    private readonly GitConfigSandbox _sandbox = new();
    private readonly string _root = TempDir.Create();
    private readonly string _data = TempDir.Create();
    private readonly string _bare;
    private readonly string _folder;

    public BootstrapMatrixE2ETests()
    {
        _bare = Path.Combine(_root, "origin.git");
        _folder = Path.Combine(_root, "watched");
        GitCli.Ensure(_root, "init", "--bare", "-b", "main", _bare);
        Directory.CreateDirectory(_folder);
        _sandbox.WriteGlobalConfig($"[url \"{_bare}\"]\n\tinsteadOf = {StandInUrl}");
    }

    [Fact]
    public async Task EmptyFolderOverSeededRepo_CheckoutFillsTheWorkingTreeWithoutTouchingGlobalConfig()
    {
        SeedBare();
        await EnsureTokenAsync();
        var bootstrapper = BootstrapperFor(_folder);

        var plan = (await bootstrapper.Assess(CancellationToken.None))
            .ShouldBeOfType<Result<BootstrapPlan>.Ok>().Value;
        plan.ShouldBeOfType<BootstrapPlan.CheckoutRemote>();

        var outcome = await bootstrapper.Execute(plan, null, CancellationToken.None);

        var completed = outcome.ShouldBeOfType<Result<BootstrapOutcome>.Ok>().Value
            .ShouldBeOfType<BootstrapOutcome.Completed>();
        completed.Flow.ShouldBe(BootstrapFlow.CheckoutRemote);
        completed.Sha.ShouldBe(GitCli.Head(_bare));
        File.ReadAllText(Path.Combine(_folder, "handbook.md")).ShouldBe("seeded handbook\n");
        File.ReadAllText(Path.Combine(_folder, "docs", "intro.txt")).ShouldBe("intro\n");
        GitCli.Run(_folder, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
        GitCli.Head(_folder).ShouldBe(GitCli.Head(_bare));
        _sandbox.AssertGlobalConfigIsolated();
    }

    [Fact]
    public async Task NonEmptyFolderOverEmptyRepo_PublishesInitialCommitHonoringSyncignoreAndBindingIdentity()
    {
        await EnsureTokenAsync();
        File.WriteAllText(Path.Combine(_folder, ".syncignore"), "drafts/\n*.key\n");
        File.WriteAllText(Path.Combine(_folder, "readme.md"), "local notes\n");
        Directory.CreateDirectory(Path.Combine(_folder, "drafts"));
        File.WriteAllText(Path.Combine(_folder, "drafts", "scratch.txt"), "wip\n");
        File.WriteAllText(Path.Combine(_folder, "api.key"), "never pushed\n");
        var bootstrapper = BootstrapperFor(_folder);

        var plan = (await bootstrapper.Assess(CancellationToken.None))
            .ShouldBeOfType<Result<BootstrapPlan>.Ok>().Value;
        plan.ShouldBeOfType<BootstrapPlan.PublishFolder>();

        var outcome = await bootstrapper.Execute(plan, null, CancellationToken.None);

        var completed = outcome.ShouldBeOfType<Result<BootstrapOutcome>.Ok>().Value
            .ShouldBeOfType<BootstrapOutcome.Completed>();
        completed.Flow.ShouldBe(BootstrapFlow.PublishFolder);
        FilesTracked(_folder).ShouldBe([".syncignore", "readme.md"]);
        GitCli.Run(_folder, "config", "user.name").Output.Trim().ShouldBe(BindingIdentity.Name);
        GitCli.Run(_folder, "config", "user.email").Output.Trim().ShouldBe(BindingIdentity.Email);
        GitCli.Run(_bare, "rev-parse", "main").Output.Trim().ShouldBe(completed.Sha);
        var publishedTree = GitCli.Run(_bare, "ls-tree", "--name-only", "-r", "main").Output;
        publishedTree.ShouldNotContain("scratch.txt");
        publishedTree.ShouldNotContain("api.key");
        _sandbox.AssertGlobalConfigIsolated();
    }

    [Fact]
    public async Task BothSidesCarryContent_ChoiceNeededAndZeroMutationUntilRepoWinsChosen()
    {
        SeedBare();
        await EnsureTokenAsync();
        File.WriteAllText(Path.Combine(_folder, "mine.txt"), "local-only\n");
        var remoteTipBefore = GitCli.Head(_bare);
        var bootstrapper = BootstrapperFor(_folder);

        var plan = (await bootstrapper.Assess(CancellationToken.None))
            .ShouldBeOfType<Result<BootstrapPlan>.Ok>().Value;
        var decide = plan.ShouldBeOfType<BootstrapPlan.Decide>();
        decide.Options.Branch.ShouldBe("main");
        decide.Options.RemoteSha.ShouldBe(remoteTipBefore);
        decide.Options.LocalFileCount.ShouldBe(1);

        var undecided = await bootstrapper.Execute(plan, null, CancellationToken.None);

        var choice = undecided.ShouldBeOfType<Result<BootstrapOutcome>.Ok>().Value
            .ShouldBeOfType<BootstrapOutcome.ChoiceNeeded>();
        choice.Options.LocalFileCount.ShouldBe(1);
        File.Exists(Path.Combine(_folder, ".git")).ShouldBeFalse();
        GitCli.Head(_bare).ShouldBe(remoteTipBefore);
        File.ReadAllText(Path.Combine(_folder, "mine.txt")).ShouldBe("local-only\n");

        var tracked = await bootstrapper.Execute(plan, BootstrapDirection.RepoWins, CancellationToken.None);

        var completed = tracked.ShouldBeOfType<Result<BootstrapOutcome>.Ok>().Value
            .ShouldBeOfType<BootstrapOutcome.Completed>();
        completed.Flow.ShouldBe(BootstrapFlow.RepoTracked);
        completed.Sha.ShouldBeEmpty();
        File.ReadAllText(Path.Combine(_folder, "mine.txt")).ShouldBe("local-only\n");
        GitCli.Head(_bare).ShouldBe(remoteTipBefore);
        GitCli.Run(_folder, "for-each-ref", "--format=%(refname)", "refs/remotes/origin/")
            .Output.ShouldContain("refs/remotes/origin/main");
        _sandbox.AssertGlobalConfigIsolated();
    }

    [Fact]
    public async Task BothSidesCarryContent_FolderWinsPublishesWithIgnoredPathsExcludedFromHistory()
    {
        SeedBare();
        await EnsureTokenAsync();
        File.WriteAllText(Path.Combine(_folder, ".syncignore"), "*.secret\n");
        File.WriteAllText(Path.Combine(_folder, "mine.txt"), "local-only\n");
        File.WriteAllText(Path.Combine(_folder, "keys.secret"), "private\n");
        var remoteTipBefore = GitCli.Head(_bare);
        var bootstrapper = BootstrapperFor(_folder);

        var plan = (await bootstrapper.Assess(CancellationToken.None))
            .ShouldBeOfType<Result<BootstrapPlan>.Ok>().Value
            .ShouldBeOfType<BootstrapPlan.Decide>();

        var published = await bootstrapper.Execute(plan, BootstrapDirection.FolderWins, CancellationToken.None);

        var completed = published.ShouldBeOfType<Result<BootstrapOutcome>.Ok>().Value
            .ShouldBeOfType<BootstrapOutcome.Completed>();
        completed.Flow.ShouldBe(BootstrapFlow.PublishFolder);
        GitCli.Head(_bare).ShouldNotBe(remoteTipBefore);
        GitCli.Run(_bare, "rev-parse", "main").Output.Trim().ShouldBe(completed.Sha);
        FilesTracked(_folder).ShouldBe([".syncignore", "mine.txt"]);
        var pushedTree = GitCli.Run(_bare, "ls-tree", "--name-only", "-r", "main").Output;
        pushedTree.ShouldNotContain("peer-file.md");
        pushedTree.ShouldNotContain("keys.secret");
        GitCli.Run(_folder, "config", "user.name").Output.Trim().ShouldBe(BindingIdentity.Name);
        _sandbox.AssertGlobalConfigIsolated();
    }

    private async Task EnsureTokenAsync()
    {
        (await new CredentialStore(_data).Write(new PersonalAccessToken(StandInToken), CancellationToken.None))
            .ShouldBeOfType<Result<PersonalAccessToken>.Ok>();
    }

    private Bootstrapper BootstrapperFor(string folder)
    {
        var config = new SyncConfig
        {
            WatchedFolder = folder,
            RemoteUrl = StandInUrl,
            Branch = "main",
            Identity = BindingIdentity
        };
        var runner = new GitProcess();
        return new Bootstrapper(
            config,
            new GitHubRepository(config, new CredentialStore(_data), runner),
            new GitLocal(runner));
    }

    private void SeedBare()
    {
        var seeder = Path.Combine(_root, "seed");
        GitCli.Ensure(_root, "clone", _bare, seeder);
        GitCli.ConfigureIdentity(seeder);
        Directory.CreateDirectory(Path.Combine(seeder, "docs"));
        File.WriteAllText(Path.Combine(seeder, "handbook.md"), "seeded handbook\n");
        File.WriteAllText(Path.Combine(seeder, "docs", "intro.txt"), "intro\n");
        GitCli.CommitAll(seeder, "seed");
        GitCli.Push(seeder, _bare, "main");
    }

    private static IReadOnlyList<string> FilesTracked(string repoFolder) =>
        GitCli.Run(repoFolder, "ls-files").Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    public void Dispose()
    {
        Directory.Delete(_root, true);
        Directory.Delete(_data, true);
        _sandbox.Dispose();
    }
}
