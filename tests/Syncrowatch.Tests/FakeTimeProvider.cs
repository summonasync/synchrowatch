namespace Syncrowatch.Tests;

public sealed class FakeTimeProvider : TimeProvider
{
    private const int MaxFiresPerAdvance = 1000;

    private readonly List<FakeTimer> _timers = [];

    public TimeSpan Now { get; private set; }

    public List<TimeSpan> RequestedDueTimes { get; } = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state);
        _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan delta)
    {
        var target = Now + delta;
        for (var fires = 0; fires < MaxFiresPerAdvance; fires++)
        {
            var due = _timers
                .Where(timer => timer.Deadline is { } deadline && deadline <= target)
                .OrderBy(timer => timer.Deadline)
                .FirstOrDefault();
            if (due is null)
            {
                break;
            }

            Now = due.Deadline!.Value;
            due.Fire();
        }

        Now = target;
    }

    public void FireQueuedCallbacks()
    {
        foreach (var timer in _timers)
        {
            timer.FireIfQueued();
        }
    }

    private sealed class FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public TimeSpan? Deadline { get; private set; }

        public bool Disposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            owner.RequestedDueTimes.Add(dueTime);
            Deadline = dueTime == Timeout.InfiniteTimeSpan ? null : owner.Now + dueTime;
            return true;
        }

        public void Fire()
        {
            if (Disposed || Deadline is null)
            {
                return;
            }

            Deadline = null;
            callback(state);
        }

        public void FireIfQueued()
        {
            if (!Disposed)
            {
                callback(state);
            }
        }

        public void Dispose()
        {
            Disposed = true;
            Deadline = null;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
