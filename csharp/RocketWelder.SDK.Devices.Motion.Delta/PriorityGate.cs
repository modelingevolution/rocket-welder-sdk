namespace RocketWelder.SDK.Devices.Motion.Delta;

/// <summary>
/// Which lane a Modbus transaction travels in. The drive's EtherNet/IP card serves one request at a
/// time, so the move loop, the status poll and the heartbeat all share one channel — and a plain
/// FIFO lock gives the wrong answer twice over (FR-11, NFR-5).
/// </summary>
internal enum ChannelPriority
{
    /// <summary>
    /// Stop and cancellation traffic. Preempts everything queued, because a 26 s homing hold would
    /// otherwise make NFR-5's 200 ms impossible (AC-23).
    /// </summary>
    Stop = 0,

    /// <summary>Ordinary move-loop and status traffic.</summary>
    Move = 1,

    /// <summary>
    /// The FR-11 heartbeat. Yields to the move loop, but only up to
    /// <see cref="PriorityGate.HeartbeatDeferralBound"/> — an unbounded hold would let a long move
    /// starve the beat and self-trip the watchdog (AC-24).
    /// </summary>
    Heartbeat = 2,
}

/// <summary>
/// The channel's mutual exclusion, with the two exceptions FR-11 and NFR-5 require: a stop lane that
/// preempts queued traffic, and a heartbeat whose deferral is <b>bounded</b> rather than merely
/// deprioritised.
///
/// <para>
/// Within a lane, waiters are served first-come-first-served. Between lanes the order on each
/// release is: any waiting stop; then the heartbeat <i>if it has already waited past
/// <see cref="HeartbeatDeferralBound"/></i> — its one reserved slot per poll cycle; then the move
/// loop; then the heartbeat.
/// </para>
///
/// <para>
/// Preemption is of the <b>queue</b>, not of the transaction in flight. One Modbus transaction is a
/// few milliseconds, and the move loop releases the gate across its own polling delays, so a stop
/// reaches the wire well inside NFR-5's budget without anything being aborted mid-frame.
/// </para>
/// </summary>
internal sealed class PriorityGate : IDisposable
{
    /// <summary>
    /// The longest the heartbeat may be held behind move traffic before it takes the next slot.
    /// FR-11 pins it at 200 ms: four beats of slack remain inside the 1 s stall window.
    /// </summary>
    public static readonly TimeSpan HeartbeatDeferralBound = TimeSpan.FromMilliseconds(200);

    private readonly Lock _sync = new();
    private readonly Queue<Waiter> _stop = new();
    private readonly Queue<Waiter> _move = new();
    private readonly Queue<Waiter> _heartbeat = new();
    private readonly TimeProvider _time;
    private readonly TimeSpan _deferralBound;
    private bool _held;
    private bool _disposed;

    public PriorityGate(TimeProvider? time = null, TimeSpan? heartbeatDeferralBound = null)
    {
        _time = time ?? TimeProvider.System;
        _deferralBound = heartbeatDeferralBound ?? HeartbeatDeferralBound;
    }

    /// <summary>Takes the channel, returning the release handle.</summary>
    public ValueTask<IDisposable> AcquireAsync(ChannelPriority priority, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Waiter waiter;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_held)
            {
                _held = true;
                return new ValueTask<IDisposable>(new Release(this));
            }

            waiter = new Waiter(priority, _time.GetUtcNow());
            QueueFor(priority).Enqueue(waiter);
        }

        return new ValueTask<IDisposable>(waiter.WaitAsync(ct));
    }

    private Queue<Waiter> QueueFor(ChannelPriority priority) => priority switch
    {
        ChannelPriority.Stop => _stop,
        ChannelPriority.Heartbeat => _heartbeat,
        _ => _move,
    };

    private void ReleaseOne()
    {
        Waiter? next;
        lock (_sync)
        {
            next = TakeNext();
            if (next is null) _held = false;
        }

        // Outside the lock: the waiter's continuation must not run under it.
        next?.Grant(this);
    }

    /// <summary>The lane order, evaluated once per release. Cancelled waiters are skipped.</summary>
    private Waiter? TakeNext()
    {
        while (true)
        {
            if (Dequeue(_stop) is { } stop) return stop;

            var now = _time.GetUtcNow();
            if (Peek(_heartbeat) is { } beat && now - beat.Enqueued >= _deferralBound)
            {
                _heartbeat.Dequeue();
                if (beat.TryClaim()) return beat;
                continue;
            }

            if (Dequeue(_move) is { } move) return move;
            if (Dequeue(_heartbeat) is { } late) return late;
            return null;
        }
    }

    private static Waiter? Peek(Queue<Waiter> queue)
    {
        while (queue.Count > 0)
        {
            var head = queue.Peek();
            if (!head.IsCancelled) return head;
            queue.Dequeue();
        }

        return null;
    }

    private static Waiter? Dequeue(Queue<Waiter> queue)
    {
        while (queue.Count > 0)
        {
            var candidate = queue.Dequeue();
            if (candidate.TryClaim()) return candidate;
        }

        return null;
    }

    public void Dispose()
    {
        lock (_sync) _disposed = true;
    }

    private sealed class Waiter(ChannelPriority priority, DateTimeOffset enqueued)
    {
        private readonly TaskCompletionSource<IDisposable> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _claimed;

        public ChannelPriority Priority { get; } = priority;

        public DateTimeOffset Enqueued { get; } = enqueued;

        public bool IsCancelled => Volatile.Read(ref _claimed) == 2;

        /// <summary>Claims this waiter for a grant, unless cancellation already claimed it.</summary>
        public bool TryClaim() => Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;

        public void Grant(PriorityGate gate) => _tcs.TrySetResult(new Release(gate));

        public async Task<IDisposable> WaitAsync(CancellationToken ct)
        {
            if (!ct.CanBeCanceled) return await _tcs.Task.ConfigureAwait(false);

            await using var registration = ct.Register(static state =>
            {
                var self = (Waiter)state!;
                // Only cancel if no release has already claimed us; otherwise the grant is in
                // flight and dropping it would leak the gate.
                if (Interlocked.CompareExchange(ref self._claimed, 2, 0) == 0)
                    self._tcs.TrySetCanceled();
            }, this);

            return await _tcs.Task.ConfigureAwait(false);
        }
    }

    private sealed class Release(PriorityGate gate) : IDisposable
    {
        private PriorityGate? _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.ReleaseOne();
    }
}
