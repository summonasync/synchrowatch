using Syncrowatch.Core.History;

namespace Syncrowatch.App.Tray;

public sealed class DetectionPromptController : IDisposable
{
    private readonly ISyncEventBus _bus;
    private readonly IDetectionPrompt _prompt;
    private readonly Func<Task> _syncNow;
    private readonly Action<Exception>? _onError;
    private readonly object _gate = new();
    private bool _promptOpen;
    private bool _pushPending;
    private int _disposed;

    public DetectionPromptController(
        ISyncEventBus bus,
        IDetectionPrompt prompt,
        Func<Task> syncNow,
        Action<Exception>? onError = null)
    {
        _bus = bus;
        _prompt = prompt;
        _syncNow = syncNow;
        _onError = onError;
        _bus.Received += OnNotification;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _bus.Received -= OnNotification;
    }

    private void OnNotification(SyncNotification notification)
    {
        switch (notification)
        {
            case SyncNotification.ChangesDetected detected:
                Consider(detected.Paths);
                break;
            case SyncNotification.PushPending:
                lock (_gate)
                {
                    _pushPending = true;
                }

                break;
            case SyncNotification.SyncCompleted:
                lock (_gate)
                {
                    _pushPending = false;
                }

                break;
        }
    }

    private void Consider(IReadOnlyList<string> paths)
    {
        lock (_gate)
        {
            if (_disposed == 1 || _promptOpen || _pushPending)
            {
                return;
            }

            _promptOpen = true;
        }

        _ = PresentAsync(paths);
    }

    private async Task PresentAsync(IReadOnlyList<string> paths)
    {
        try
        {
            if (await _prompt.RequestAsync(paths, CancellationToken.None).ConfigureAwait(false))
            {
                await _syncNow().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _onError?.Invoke(exception);
        }
        finally
        {
            lock (_gate)
            {
                _promptOpen = false;
            }
        }
    }
}
