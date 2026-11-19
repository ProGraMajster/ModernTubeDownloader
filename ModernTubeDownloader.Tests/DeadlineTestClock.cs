namespace ModernTubeDownloader.Tests;

/// <summary>A deterministic one-shot clock for CancellationTokenSource deadlines.
/// Real watchdogs remain on awaited work; advancing time never runs application work.</summary>
internal sealed class DeadlineTestClock : TimeProvider
{
    private readonly object sync = new();
    private readonly List<DeadlineTimer> timers = [];
    private TimeSpan elapsed;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new DeadlineTimer(this, callback, state);
        lock (sync) { timers.Add(timer); timer.Change(dueTime, period); }
        return timer;
    }

    public void Advance(TimeSpan duration)
    {
        DeadlineTimer[] ready;
        lock (sync)
        {
            elapsed += duration;
            ready = timers.Where(timer => timer.Due is { } due && due <= elapsed).ToArray();
            foreach (var timer in ready) timer.Due = null;
        }
        foreach (var timer in ready) timer.Fire();
    }

    private sealed class DeadlineTimer(DeadlineTestClock clock, TimerCallback callback, object? state) : ITimer
    {
        private bool disposed;
        internal TimeSpan? Due { get; set; }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan) throw new NotSupportedException("Only one-shot deadlines are supported.");
            lock (clock.sync)
            {
                if (disposed) return false;
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.elapsed + dueTime;
                return true;
            }
        }
        internal void Fire() { lock (clock.sync) { if (!disposed) callback(state); } }
        public void Dispose() { lock (clock.sync) { disposed = true; Due = null; clock.timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
