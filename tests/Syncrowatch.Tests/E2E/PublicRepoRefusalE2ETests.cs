using NSubstitute;
using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.App.Wizard;
using Syncrowatch.Core.Bootstrap;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests.E2E;

[Collection("git-config-sandbox")]
public sealed class PublicRepoRefusalE2ETests : IDisposable
{
    private const string StandInUrl = "https://synchr.e2e/public.git";
    private const string StandInToken = "ghp_e2e_public_7c6b5a4d3e2f";

    private readonly GitConfigSandbox _sandbox = new();
    private readonly string _root = TempDir.Create();
    private readonly string _data = TempDir.Create();
    private readonly string _bare;
    private readonly string _folder;

    public PublicRepoRefusalE2ETests()
    {
        _bare = Path.Combine(_root, "origin.git");
        _folder = TempDir.Create();
        GitCli.Ensure(_root, "init", "--bare", "-b", "main", _bare);
        _sandbox.WriteGlobalConfig($"[url \"{_bare}\"]\n\tinsteadOf = {StandInUrl}");
    }

    [Fact]
    public async Task ValidationRefusesAPubliclyReadableRemote_EvenWithCredentials_BeforeAnyAuthenticatedCall()
    {
        (await new CredentialStore(_data).Write(new PersonalAccessToken(StandInToken), CancellationToken.None))
            .ShouldBeOfType<Result<PersonalAccessToken>.Ok>();
        var runner = new CountingRunner();
        var remote = RemoteFor(runner);

        var outcome = await remote.ValidateRepository(CancellationToken.None);

        outcome.ShouldBeOfType<Result<RemoteValidation>.Fail>().Error.Code.ShouldBe("public_repo");
        runner.Invocations.ShouldNotBeEmpty();
        runner.Invocations.ShouldNotContain(invocation => invocation.Environment.ContainsKey(CredentialInjector.PasswordVariable));
    }

    [Fact]
    public async Task WizardGateStopsTheBindingWithAnExplanationAndPersistsNothing()
    {
        var runner = new CountingRunner();
        var picker = Substitute.For<IFolderPicker>();
        picker.PickAsync().Returns(_folder);
        var viewModel = new SetupWizardViewModel(
            config => RemoteFor(runner),
            config => BootstrapperFor(config, runner),
            new JsonSyncConfigStore(_data),
            new CredentialStore(_data),
            picker);
        viewModel.AccessToken = StandInToken;
        await viewModel.ContinueAsync();
        viewModel.RemoteUrl = StandInUrl;

        await viewModel.ValidateRepositoryAsync();

        viewModel.ErrorMessage.ShouldNotBeNull().ShouldContain("publicly readable");
        viewModel.ContinueCommand.CanExecute(null).ShouldBeFalse();
        File.Exists(SyncrowatchPaths.SettingsFile(_data)).ShouldBeFalse();
        File.Exists(SyncrowatchPaths.CredentialFile(_data)).ShouldBeTrue();
        Directory.Exists(Path.Combine(_folder, ".git")).ShouldBeFalse();
        runner.Invocations.ShouldNotContain(invocation => invocation.Environment.ContainsKey(CredentialInjector.PasswordVariable));
    }

    [Fact]
    public async Task BootstrapEntryWithoutCredentials_RefusesBeforeRunningAnyGitCommand()
    {
        var runner = new CountingRunner();
        File.WriteAllText(Path.Combine(_folder, "pending.txt"), "local work\n");
        var config = new SyncConfig
        {
            WatchedFolder = _folder,
            RemoteUrl = StandInUrl,
            Branch = "main"
        };
        var bootstrapper = new Bootstrapper(config, new GitHubRepository(config, new CredentialStore(_data), runner), new GitLocal(runner));

        var outcome = await bootstrapper.Assess(CancellationToken.None);

        outcome.ShouldBeOfType<Result<BootstrapPlan>.Fail>().Error.Code.ShouldBe("not_found");
        runner.Invocations.ShouldBeEmpty();
        Directory.Exists(Path.Combine(_folder, ".git")).ShouldBeFalse();
        File.ReadAllText(Path.Combine(_folder, "pending.txt")).ShouldBe("local work\n");
    }

    private GitHubRepository RemoteFor(IGitRunner runner) =>
        new(
            new SyncConfig { WatchedFolder = _folder, RemoteUrl = StandInUrl, Branch = "main" },
            new CredentialStore(_data),
            runner);

    private Bootstrapper BootstrapperFor(SyncConfig config, IGitRunner runner) =>
        new(config, new GitHubRepository(config, new CredentialStore(_data), runner), new GitLocal(runner));

    private sealed class CountingRunner : IGitRunner
    {
        public List<GitInvocation> Invocations { get; } = [];

        public Task<GitCommandResult> RunAsync(GitInvocation invocation, CancellationToken cancellationToken)
        {
            Invocations.Add(invocation);
            return new GitProcess().RunAsync(invocation, cancellationToken);
        }
    }

    public void Dispose()
    {
        Directory.Delete(_root, true);
        Directory.Delete(_data, true);
        Directory.Delete(_folder, true);
        _sandbox.Dispose();
    }
}
