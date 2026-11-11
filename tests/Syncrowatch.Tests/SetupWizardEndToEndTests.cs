using NSubstitute;
using Shouldly;
using Synchro.Core;
using Syncrowatch.App.Wizard;
using Syncrowatch.Core;
using Syncrowatch.Core.Bootstrap;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

[Collection("git-config-sandbox")]
public class SetupWizardEndToEndTests
{
    private const string StandInUrl = "https://syncrowatch.test/wizard-e2e.git";
    private const string Token = "ghp_wizarde2e_44c3b2a19f8e";

    [Fact]
    public async Task PublicRepository_RefusedInPlace_AndSetupStaysUnfinished()
    {
        using var fixture = WizardFixture.ReadableAnonymously();
        var viewModel = fixture.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();
        viewModel.RemoteUrl = StandInUrl;

        await viewModel.ValidateRepositoryAsync();

        viewModel.ErrorMessage.ShouldNotBeNull().ShouldContain("publicly readable");
        viewModel.ContinueCommand.CanExecute(null).ShouldBeFalse();
        File.Exists(fixture.SettingsPath).ShouldBeFalse();
        File.Exists(fixture.CredentialPath).ShouldBeTrue();
    }

    [Fact]
    public async Task Cancel_BothSidesNonEmpty_LeavesFolderRepoAndDiskUntouched()
    {
        using var fixture = WizardFixture.WithScriptedRemote("1a2b3c4d5e6f708192a3b4c5d6e7f809", seededFolder: true);
        await DriveToBootstrapAsync(fixture);

        await fixture.ViewModel.CancelAsync();

        File.Exists(fixture.SettingsPath).ShouldBeFalse();
        File.Exists(fixture.CredentialPath).ShouldBeFalse();
        Directory.Exists(Path.Combine(fixture.WatchedFolder, ".git")).ShouldBeFalse();
        Directory.EnumerateFileSystemEntries(fixture.WatchedFolder).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task FolderWins_Completes_PersistsConfigAndBootstrapsRepository()
    {
        using var fixture = WizardFixture.WithScriptedRemote("9f8e7d6c5b4a39281706f5e4d3c2b1a0", seededFolder: true);
        await DriveToBootstrapAsync(fixture);

        await fixture.ViewModel.ChooseAsync(BootstrapDirection.FolderWins);

        fixture.ViewModel.ErrorMessage.ShouldBe((string?)null);
        fixture.ViewModel.Step.ShouldBe(WizardStep.Completed);
        fixture.ViewModel.NeedsChoice.ShouldBeFalse();
        var config = (await fixture.LoadConfigAsync()).ShouldBeOfType<Result<SyncConfig>.Ok>().Value;
        config.WatchedFolder.ShouldBe(fixture.WatchedFolder);
        config.RemoteUrl.ShouldBe(StandInUrl);
        config.Branch.ShouldBe("main");
        config.Mode.ShouldBe(SyncRunMode.Auto);
        config.Identity.Name.ShouldBe(CommitIdentity.Default.Name);
        config.Identity.Email.ShouldBe(CommitIdentity.Default.Email);
        config.Debounce.ShouldBe(SyncConfig.DefaultDebounce);
        config.PollInterval.ShouldBe(SyncConfig.DefaultPollInterval);
        var token = (await fixture.ReadTokenAsync()).ShouldBeOfType<Result<PersonalAccessToken>.Ok>().Value;
        token.Value.ShouldBe(Token);
        Directory.Exists(Path.Combine(fixture.WatchedFolder, ".git")).ShouldBeTrue();
    }

    private static async Task DriveToBootstrapAsync(WizardFixture fixture)
    {
        var viewModel = fixture.ViewModel;
        viewModel.AccessToken = Token;
        await viewModel.ContinueAsync();
        viewModel.RemoteUrl = StandInUrl;
        await viewModel.ValidateRepositoryAsync();
        await viewModel.ContinueAsync();
        await viewModel.BrowseFolderAsync();
        viewModel.FolderPath.ShouldBe(fixture.WatchedFolder);
        await viewModel.ContinueAsync();
        await viewModel.ContinueAsync();
        viewModel.Step.ShouldBe(WizardStep.Bootstrap);
    }

    private sealed class WizardFixture : IDisposable
    {
        private readonly GitConfigSandbox _sandbox;
        private readonly string? _gitRoot;

        private WizardFixture(GitConfigSandbox sandbox, string dataDirectory, string watchedFolder,
            SetupWizardViewModel viewModel, string? gitRoot = null)
        {
            _sandbox = sandbox;
            DataDirectory = dataDirectory;
            WatchedFolder = watchedFolder;
            ViewModel = viewModel;
            _gitRoot = gitRoot;
        }

        public string DataDirectory { get; }

        public string WatchedFolder { get; }

        public string SettingsPath => SyncrowatchPaths.SettingsFile(DataDirectory);

        public string CredentialPath => SyncrowatchPaths.CredentialFile(DataDirectory);

        public SetupWizardViewModel ViewModel { get; }

        public static WizardFixture WithScriptedRemote(string remoteSha, bool seededFolder)
        {
            var sandbox = new GitConfigSandbox();
            var dataDirectory = TempDir.Create();
            var watchedFolder = TempDir.Create();
            if (seededFolder)
            {
                File.WriteAllText(Path.Combine(watchedFolder, "stand-in.txt"), "wizard fixture content");
            }

            return new WizardFixture(sandbox, dataDirectory, watchedFolder, BuildViewModel(
                dataDirectory,
                watchedFolder,
                config => new GitHubRepository(config, new CredentialStore(dataDirectory), ScriptedRunner(remoteSha)),
                config => new Bootstrapper(
                    config,
                    new GitHubRepository(config, new CredentialStore(dataDirectory), ScriptedRunner(remoteSha)),
                    new GitLocal(new GitProcess()))));
        }

        public static WizardFixture ReadableAnonymously()
        {
            var sandbox = new GitConfigSandbox();
            var gitRoot = TempDir.Create();
            var barePath = Path.Combine(gitRoot, "origin.git");
            sandbox.WriteGlobalConfig($"[url \"{barePath}\"]\n\tinsteadOf = {StandInUrl}");
            GitCli.Ensure(gitRoot, "init", "--bare", "-b", "main", barePath);
            var dataDirectory = TempDir.Create();
            var watchedFolder = TempDir.Create();
            return new WizardFixture(sandbox, dataDirectory, watchedFolder, BuildViewModel(
                dataDirectory,
                watchedFolder,
                config => new GitHubRepository(config, new CredentialStore(dataDirectory), new GitProcess()),
                config => new Bootstrapper(
                    config,
                    new GitHubRepository(config, new CredentialStore(dataDirectory), new GitProcess()),
                    new GitLocal(new GitProcess()))), gitRoot);
        }

        public async Task<Result<SyncConfig>> LoadConfigAsync() =>
            await new JsonSyncConfigStore(DataDirectory).Load(CancellationToken.None);

        public async Task<Result<PersonalAccessToken>> ReadTokenAsync() =>
            await new CredentialStore(DataDirectory).Read(CancellationToken.None);

        public void Dispose()
        {
            if (Directory.Exists(DataDirectory))
            {
                Directory.Delete(DataDirectory, true);
            }

            if (Directory.Exists(WatchedFolder))
            {
                Directory.Delete(WatchedFolder, true);
            }

            if (_gitRoot is { } root && Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }

            _sandbox.Dispose();
        }

        private static FakeGitRunner ScriptedRunner(string remoteSha) => new(invocation =>
        {
            var arguments = invocation.Arguments;
            if (arguments.Any(argument => argument is "ls-remote" or "fetch" or "push"))
            {
                return RemoteAnswer(arguments, remoteSha, invocation);
            }

            return arguments.Contains("rev-parse")
                ? new GitCommandResult(0, $"{remoteSha}\n", string.Empty)
                : new GitCommandResult(0, string.Empty, string.Empty);
        });

        private static GitCommandResult RemoteAnswer(IReadOnlyList<string> arguments, string remoteSha, GitInvocation invocation)
        {
            if (!invocation.Environment.ContainsKey(CredentialInjector.PasswordVariable))
            {
                return new GitCommandResult(128, string.Empty,
                    "fatal: could not read Username for 'https://syncrowatch.test': terminal prompts disabled");
            }

            if (arguments.Contains("--symref"))
            {
                return new GitCommandResult(0, $"ref: refs/heads/main\tHEAD\n{remoteSha}\tHEAD\n", string.Empty);
            }

            if (arguments.Contains("--heads"))
            {
                return new GitCommandResult(0, $"{remoteSha}\trefs/heads/main\n", string.Empty);
            }

            return new GitCommandResult(0, string.Empty, string.Empty);
        }

        private static SetupWizardViewModel BuildViewModel(
            string dataDirectory,
            string watchedFolder,
            Func<SyncConfig, IRemoteRepository> remoteFactory,
            Func<SyncConfig, IBootstrapper> bootstrapFactory)
        {
            var picker = Substitute.For<IFolderPicker>();
            picker.PickAsync().Returns(watchedFolder);
            return new SetupWizardViewModel(
                remoteFactory,
                bootstrapFactory,
                new JsonSyncConfigStore(dataDirectory),
                new CredentialStore(dataDirectory),
                picker);
        }
    }
}
