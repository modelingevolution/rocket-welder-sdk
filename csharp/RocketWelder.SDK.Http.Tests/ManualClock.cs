using System.Threading.Channels;

namespace RocketWelder.SDK.Http.Tests;

/// <summary>
/// A <see cref="TimeProvider"/> whose time moves only on <see cref="Advance"/>. Every timer it creates is announced, so a
/// test can wait until the code under test is sleeping (<see cref="TimerCreatedAsync"/>) and then move time — no real
/// sleeps and no wall-clock windows.
/// </summary>
internal sealed class ManualClock : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly Channel<(int Index, TimeSpan Due)> _created = Channel.CreateUnbounded<(int, TimeSpan)>();
    private int _timersCreated;
    private DateTimeOffset _now = new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        _created.Writer.TryWrite((Interlocked.Increment(ref _timersCreated), dueTime));
        return timer;
    }

    /// <summary>How many timers have been created so far; pass it to <see cref="TimerCreatedAsync"/> as the mark.</summary>
    public int TimersCreated => Volatile.Read(ref _timersCreated);

    /// <summary>Completes when a timer with <paramref name="dueTime"/> is created after the first <paramref name="after"/>
    /// timers (skipping every other timer).</summary>
    public async Task TimerCreatedAsync(TimeSpan dueTime, int after = 0)
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var (index, due) = await _created.Reader.ReadAsync(guard.Token).ConfigureAwait(false);
            if (index > after && due == dueTime)
                return;
        }
    }

    /// <summary>Moves time forward and fires every timer that has come due.</summary>
    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            _now += by;
            due = _timers.Where(t => t.Due <= _now).ToList();
            foreach (var t in due)
                _timers.Remove(t);
        }
        foreach (var t in due)
            t.Fire();
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                clock._timers.Remove(this);
                if (dueTime == Timeout.InfiniteTimeSpan)
                    return true;
                Due = clock._now + dueTime;
                clock._timers.Add(this);
            }
            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (clock._gate)
                clock._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
