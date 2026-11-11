using NSubstitute;
using Shouldly;
using Synchro.Core;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class ModeControllerTests : IDisposable
{
    private readonly string _settingsDirectory = TempDir.Create();
    private readonly JsonSyncConfigStore _store;
    private readonly FakeSyncEngine _engine = new();
    private readonly FakeWatching _watching = new();
    private readonly FakeTimeProvider _time = new();

    public ModeControllerTests()
    {
        _store = new JsonSyncConfigStore(_settingsDirectory);
    }

    [Fact]
    public async Task Launch_WithSavedConfig_StartsEngineAndWatcherWithStoredMode()
    {
        await _store.Save(Sample(SyncRunMode.Manual), CancellationToken.None);
        using var controller = Build();

        await controller.LaunchAsync();

        controller.IsConfigured.ShouldBeTrue();
        controller.Mode.ShouldBe(SyncRunMode.Manual);
        _engine.AppliedMode.ShouldBe(SyncRunMode.Manual);
        _engine.StartCount.ShouldBe(1);
        _watching.StartCount.ShouldBe(1);
    }

    [Fact]
    public async Task Launch_WithoutSavedConfig_StandsByWithoutStartingAnything()
    {
        using var controller = Build();

        await controller.LaunchAsync();

        controller.IsConfigured.ShouldBeFalse();
        _engine.StartCount.ShouldBe(0);
        _watching.StartCount.ShouldBe(0);
    }

    [Fact]
    public async Task SetModeAsync_PersistsAndAppliesTheNewMode()
    {
        await _store.Save(Sample(SyncRunMode.Auto), CancellationToken.None);
        using var controller = Build();
        await controller.LaunchAsync();

        await controller.SetModeAsync(SyncRunMode.SemiAuto);

        controller.Mode.ShouldBe(SyncRunMode.SemiAuto);
        _engine.AppliedMode.ShouldBe(SyncRunMode.SemiAuto);
        var stored = await _store.Load(CancellationToken.None);
        stored.ShouldBeOfType<Result<SyncConfig>.Ok>().Value.Mode.ShouldBe(SyncRunMode.SemiAuto);
    }

    [Fact]
    public async Task SetModeAsync_PersistedModeAppliesOnARestartedController()
    {
        await _store.Save(Sample(SyncRunMode.Auto), CancellationToken.None);
        var first = Build();
        await first.LaunchAsync();
        await first.SetModeAsync(SyncRunMode.Manual);
        first.Dispose();

        var secondEngine = new FakeSyncEngine();
        using var second = new ModeController(secondEngine, _store, new FakeWatching(), _time);
        await second.LaunchAsync();

        second.Mode.ShouldBe(SyncRunMode.Manual);
        secondEngine.AppliedMode.ShouldBe(SyncRunMode.Manual);
    }

    [Fact]
    public async Task SetModeAsync_FailingPersist_LeavesLiveModeUntouched()
    {
        var store = Substitute.For<ISyncConfigStore>();
        store.Load(Arg.Any<CancellationToken>()).Returns(new Result<SyncConfig>.Ok(Sample(SyncRunMode.Auto)));
        store.Save(Arg.Any<SyncConfig>(), Arg.Any<CancellationToken>())
            .Returns(new Result<SyncConfig>.Fail(new SyncError("unavailable", "settings file could not be written")));
        using var controller = new ModeController(_engine, store, _watching, _time);
        await controller.LaunchAsync();

        await controller.SetModeAsync(SyncRunMode.Manual);

        controller.Mode.ShouldBe(SyncRunMode.Auto);
        _engine.AppliedMode.ShouldBe(SyncRunMode.Auto);
        controller.HasError.ShouldBeTrue();
    }

    [Fact]
    public async Task ManualMode_WatchedChangesStayQueuedAndNothingSyncsWithoutExplicitCommand()
    {
        await _store.Save(Sample(SyncRunMode.Manual), CancellationToken.None);
        using var controller = Build();
        await controller.LaunchAsync();

        _watching.Emit("notes.txt");
        _watching.Emit("docs/spec.md");
        _time.Advance(SyncConfig.DefaultDebounce + TimeSpan.FromSeconds(1));

        _engine.AcceptedBatches.ShouldHaveSingleItem().ShouldBe(["notes.txt", "docs/spec.md"]);
        _engine.SyncCount.ShouldBe(0);
        _engine.ConfirmCount.ShouldBe(0);

        await controller.SyncNowAsync();

        _engine.SyncCount.ShouldBe(1);
    }

    [Fact]
    public async Task Pause_SuspendsEngineAndWatcher_Resume_RestoresBoth()
    {
        await _store.Save(Sample(SyncRunMode.Auto), CancellationToken.None);
        using var controller = Build();
        await controller.LaunchAsync();

        controller.Pause();

        controller.IsPaused.ShouldBeTrue();
        _engine.PauseCount.ShouldBe(1);
        _watching.SuspendCount.ShouldBe(1);

        _watching.Emit("notes.txt");
        _time.Advance(SyncConfig.DefaultDebounce + TimeSpan.FromSeconds(1));
        _engine.AcceptedBatches.ShouldBeEmpty();

        controller.Resume();

        controller.IsPaused.ShouldBeFalse();
        _engine.ResumeCount.ShouldBe(1);
        _watching.ResumeCount.ShouldBe(1);

        _watching.Emit("notes.txt");
        _time.Advance(SyncConfig.DefaultDebounce + TimeSpan.FromSeconds(1));
        _engine.AcceptedBatches.ShouldHaveSingleItem().ShouldBe(["notes.txt"]);
    }

    [Fact]
    public async Task Pause_RepeatedCalls_SuspendOnceOnly()
    {
        await _store.Save(Sample(SyncRunMode.Auto), CancellationToken.None);
        using var controller = Build();
        await controller.LaunchAsync();

        controller.Pause();
        controller.Pause();

        _engine.PauseCount.ShouldBe(1);
        _watching.SuspendCount.ShouldBe(1);
    }

    [Fact]
    public async Task Dispose_TearsDownWatcherAndEngine()
    {
        await _store.Save(Sample(SyncRunMode.Auto), CancellationToken.None);
        var controller = Build();
        await controller.LaunchAsync();

        controller.Dispose();

        _watching.DisposeCount.ShouldBe(1);
        _engine.DisposeCount.ShouldBe(1);
    }

    private ModeController Build() => new(_engine, _store, _watching, _time);

    private SyncConfig Sample(SyncRunMode mode) => new()
    {
        WatchedFolder = Path.Combine(_settingsDirectory, "watched"),
        RemoteUrl = "https://github.com/acme/private.git",
        Branch = "main",
        Mode = mode
    };

    public void Dispose()
    {
        try
        {
            Directory.Delete(_settingsDirectory, true);
        }
        catch (IOException)
        {
        }
    }
}
