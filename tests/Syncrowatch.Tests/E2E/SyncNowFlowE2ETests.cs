using Shouldly;
using Syncrowatch.Core;
using Syncrowatch.App.Notifications;
using Syncrowatch.App.Tray;
using Syncrowatch.App.Wizard;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Xunit;

namespace Syncrowatch.Tests.E2E;

[Collection("git-config-sandbox")]
public sealed class SyncNowFlowE2ETests : IDisposable
{
    private const string EditedFile = "shared.txt";

    private readonly SyncE2EStand _stand = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeWatching _watching = new();
    private readonly RecordingPrompt _prompt = new();

    [Fact]
    public async Task SemiAuto_LaterLeg_StagesSilently_SyncNowPressesLandEverything()
    {
        await _stand.BindAsync(_stand.CloneB, SyncRunMode.SemiAuto);
        using var leg = await StartLegAsync();

        WriteEdit("later-path edit\n");
        leg.Watching.Emit(EditedFile);
        leg.Time.Advance(SyncConfig.DefaultDebounce + TimeSpan.FromSeconds(1));

        leg.Prompt.Requests.ShouldHaveSingleItem().ShouldBe([EditedFile]);
        _stand.SnapshotNotifications().OfType<SyncNotification.ChangesDetected>().ShouldHaveSingleItem();
        leg.Toasts.Presented.ShouldBeEmpty();
        leg.Errors.ShouldBeEmpty();

        leg.Prompt.Reply(accepted: false);
        leg.Time.Advance(SyncConfig.DefaultPollInterval);
        await AwaitCycleFinishedAsync(leg);

        RemoteTip().ShouldNotBe(GitCli.Head(_stand.CloneB));
        leg.Tracker.PushPending.ShouldBeTrue();
        leg.Tracker.ToolTipText.ShouldContain("push pending");
        leg.Prompt.Requests.Count.ShouldBe(1);
        leg.Toasts.Presented.ShouldBeEmpty();

        WriteEdit("second edit while the push waits\n");
        leg.Watching.Emit(EditedFile);
        leg.Time.Advance(SyncConfig.DefaultDebounce + TimeSpan.FromSeconds(1));
        await SyncE2EStand.AwaitAsync(
            () => Task.FromResult(_stand.SnapshotNotifications().OfType<SyncNotification.ChangesDetected>().Count() == 2),
            "the second detection event");
        leg.Prompt.Requests.Count.ShouldBe(1);

        await leg.Command.ExecuteAsync(null);
        await SyncE2EStand.AwaitAsync(
            () => Task.FromResult(RemoteTip() == GitCli.Head(_stand.CloneB)),
            "the staged push to land");

        RemoteBlobText(EditedFile).ShouldBe("later-path edit\n");
        leg.Tracker.PushPending.ShouldBeFalse();
        leg.Tracker.State.ShouldBe(TrayState.Idle);
        leg.Toasts.Presented.Select(toast => toast.Kind).ShouldBe([ToastKind.SyncCompleted]);
        GitCli.Run(_stand.CloneB, "status", "--porcelain").Output.Trim().ShouldBe("M shared.txt");
        leg.Prompt.Requests.Count.ShouldBe(1);
        leg.Errors.ShouldBeEmpty();

        leg.Time.Advance(SyncConfig.DefaultPollInterval);
        await AwaitCycleFinishedAsync(leg);
        leg.Prompt.Requests.Count.ShouldBe(1);

        await leg.Command.ExecuteAsync(null);
        await SyncE2EStand.AwaitAsync(
            () => Task.FromResult(RemoteTip() == GitCli.Head(_stand.CloneB)),
            "the follow-up commit to land");

        GitCli.Run(_stand.CloneB, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
        RemoteBlobText(EditedFile).ShouldBe("second edit while the push waits\n");
        leg.Tracker.PushPending.ShouldBeFalse();
        leg.Tracker.State.ShouldBe(TrayState.Idle);
        leg.Toasts.Presented.Select(toast => toast.Kind).ShouldBe([ToastKind.SyncCompleted]);
        _stand.SnapshotNotifications().OfType<SyncNotification.ExplicitSyncCompleted>().Count().ShouldBe(2);
        leg.Prompt.Requests.Count.ShouldBe(1);
        leg.Errors.ShouldBeEmpty();
    }

    [Fact]
    public async Task SemiAuto_NowLeg_SyncNowFromThePrompt_LandsImmediatelyWithoutFollowUps()
    {
        await _stand.BindAsync(_stand.CloneB, SyncRunMode.SemiAuto);
        using var leg = await StartLegAsync();

        WriteEdit("prompt-now edit\n");
        leg.Watching.Emit(EditedFile);
        leg.Time.Advance(SyncConfig.DefaultDebounce + TimeSpan.FromSeconds(1));
        await SyncE2EStand.AwaitAsync(
            () => Task.FromResult(leg.Prompt.Requests.Count == 1),
            "the detection prompt to appear");

        leg.Prompt.Reply(accepted: true);
        await SyncE2EStand.AwaitAsync(
            () => Task.FromResult(RemoteTip() == GitCli.Head(_stand.CloneB) && leg.Toasts.Presented.Count == 1),
            "the sync now decision to land");

        GitCli.Run(_stand.CloneB, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
        RemoteBlobText(EditedFile).ShouldBe("prompt-now edit\n");
        _stand.SnapshotNotifications().OfType<SyncNotification.PushPending>().ShouldBeEmpty();
        _stand.SnapshotNotifications().OfType<SyncNotification.ChangesDetected>().ShouldHaveSingleItem();
        leg.Prompt.Requests.Count.ShouldBe(1);
        leg.Toasts.Presented.Select(toast => toast.Kind).ShouldBe([ToastKind.SyncCompleted]);
        leg.Tracker.State.ShouldBe(TrayState.Idle);
        leg.Errors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Manual_ExplicitSyncNow_FlushesTheHeldEditInsideTheDebounceWindow()
    {
        await _stand.BindAsync(_stand.CloneB, SyncRunMode.Manual);
        using var leg = await StartLegAsync();

        WriteEdit("manual flush edit\n");
        leg.Watching.Emit(EditedFile);

        await leg.Command.ExecuteAsync(null);
        await SyncE2EStand.AwaitAsync(
            () => Task.FromResult(RemoteTip() == GitCli.Head(_stand.CloneB) && leg.Toasts.Presented.Count == 1),
            "the explicit sync to flush and land");

        GitCli.Run(_stand.CloneB, "status", "--porcelain").Output.Trim().ShouldBeEmpty();
        GitCli.Run(_stand.CloneB, "log", "-1", "--pretty=%s").Output.Trim()
            .ShouldBe($"sync: 1 file changed ({EditedFile})");
        RemoteBlobText(EditedFile).ShouldBe("manual flush edit\n");

        leg.Time.Advance(SyncConfig.DefaultPollInterval + SyncConfig.DefaultDebounce);
        await Task.Delay(50);

        RemoteTip().ShouldBe(GitCli.Head(_stand.CloneB));
        _stand.SnapshotNotifications().OfType<SyncNotification.ChangesDetected>().ShouldBeEmpty();
        leg.Prompt.Requests.ShouldBeEmpty();
        leg.Toasts.Presented.Select(toast => toast.Kind).ShouldBe([ToastKind.SyncCompleted]);
        leg.Tracker.State.ShouldBe(TrayState.Idle);
        leg.Errors.ShouldBeEmpty();
        (await _stand.EventsAsync(_stand.CloneB)).Select(@event => @event.Kind).ShouldBe([SyncEventKind.CommitOut]);
    }

    private async Task<Leg> StartLegAsync()
    {
        var loop = await _stand.LoopForAsync(_stand.CloneB, _time);
        loop.Start();
        var controller = new ModeController(
            loop,
            new JsonSyncConfigStore(_stand.DataDirectory),
            _watching,
            _stand.Bus,
            _time);
        await controller.LaunchAsync();
        controller.IsConfigured.ShouldBeTrue();

        var tracker = new TrayStateTracker(_stand.Bus);
        var toasts = new ToastWindowSpy();
        var toastService = new ToastService(_stand.Bus, toasts);
        var runner = new SyncNowRunner(tracker, new ExplicitSyncFeedback(_stand.Bus));
        var errors = new List<Exception>();
        var command = new AsyncCommand(
            () => runner.RunAsync(() => controller.SyncNowAsync(CancellationToken.None)),
            () => controller.IsConfigured && !controller.IsPaused,
            errors.Add);
        var detection = new DetectionPromptController(_stand.Bus, _prompt, () => command.ExecuteAsync(null), errors.Add);

        return new Leg(
            _time, _watching, loop, controller, tracker, toasts, toastService, command, detection, _prompt, errors);
    }

    private async Task AwaitCycleFinishedAsync(Leg leg)
    {
        await SyncE2EStand.AwaitAsync(
            () => Task.FromResult(leg.Tracker.PushPending),
            "the staged push to surface in the tray");
        await Task.Delay(50);
    }

    private void WriteEdit(string content) =>
        File.WriteAllText(Path.Combine(_stand.CloneB, EditedFile), content);

    private string RemoteTip() => GitCli.Run(_stand.Bare, "rev-parse", "main").Output.Trim();

    private string RemoteBlobText(string relativePath) =>
        GitCli.Run(_stand.Bare, "show", $"main:{relativePath}").Output;

    public void Dispose() => _stand.Dispose();

    private sealed class Leg(
        FakeTimeProvider time,
        FakeWatching watching,
        SyncLoop loop,
        ModeController controller,
        TrayStateTracker tracker,
        ToastWindowSpy toasts,
        ToastService toastService,
        AsyncCommand command,
        DetectionPromptController detection,
        RecordingPrompt prompt,
        List<Exception> errors) : IDisposable
    {
        public FakeTimeProvider Time { get; } = time;

        public FakeWatching Watching { get; } = watching;

        public SyncLoop Loop { get; } = loop;

        public ModeController Controller { get; } = controller;

        public TrayStateTracker Tracker { get; } = tracker;

        public ToastWindowSpy Toasts { get; } = toasts;

        public AsyncCommand Command { get; } = command;

        public RecordingPrompt Prompt { get; } = prompt;

        public List<Exception> Errors { get; } = errors;

        public void Dispose()
        {
            detection.Dispose();
            toastService.Dispose();
            Tracker.Dispose();
            Controller.Dispose();
        }
    }

    private sealed class RecordingPrompt : IDetectionPrompt
    {
        private readonly Queue<TaskCompletionSource<bool>> _replies = new();

        public List<IReadOnlyList<string>> Requests { get; } = [];

        public void Reply(bool accepted)
        {
            _replies.Dequeue().TrySetResult(accepted);
        }

        public Task<bool> RequestAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
        {
            Requests.Add(paths);
            var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _replies.Enqueue(reply);
            return reply.Task;
        }
    }

    private sealed class ToastWindowSpy : IToastWindowFactory
    {
        public List<ToastViewModel> Presented { get; } = [];

        public void Present(ToastViewModel toast, Action expired) => Presented.Add(toast);

        public void Dismiss(ToastViewModel toast)
        {
        }
    }
}
