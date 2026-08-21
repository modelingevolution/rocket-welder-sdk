using Microsoft.Extensions.Logging;

namespace RocketWelder.SDK.Devices.Motion.Delta;

/// <summary>
/// FR-11's client half of the dead-commander watchdog: a <b>connection-lifetime</b> timer that
/// writes a changing value into D130 at ≥ 5 Hz, plus the advisory lease that decides whether this
/// instance may attach at all.
///
/// <para>
/// <b>Connection-lifetime, not motion-lifetime.</b> The timer starts at
/// <c>ConnectAsync</c> and stops only on a deliberate disconnect. A heartbeat that beat only during
/// moves would stall the instant a move succeeded and trip the watchdog after every single one.
/// </para>
///
/// <para>
/// <b>Never write 0.</b> The drive's network arms on the first CHANGE of D130 and D130 powers up at
/// 0, so a commander whose first beat is literally 0 writes no change and leaves the watchdog
/// disarmed — protection that silently is not there. Arming is the first NON-ZERO write: this
/// counter starts at 1 and skips 0 on wrap.
/// </para>
///
/// <para>
/// <b>The beat's paired read doubles as the status poll.</b> Each tick reads D132/D133 back in one
/// transaction — which is how a trip is noticed — and raises <see cref="Ticked"/>, giving an idle
/// axis a <c>StatusChanged</c> cadence it would otherwise not have (this narrows OQ-2).
/// </para>
///
/// <para>
/// All of its traffic travels in <see cref="ChannelPriority.Heartbeat"/>, which yields to the move
/// loop but only up to <see cref="PriorityGate.HeartbeatDeferralBound"/> — an unbounded yield would
/// let a long move starve the beat and self-trip the watchdog (AC-24).
/// </para>
/// </summary>
internal sealed class DeltaHeartbeat : IAsyncDisposable
{
    private readonly IModbusChannel _channel;
    private readonly ILogger? _logger;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _axis;

    private Task? _loop;
    private ushort _beat;
    private bool _leaseHeld;
    private ushort _lastFault;

    public DeltaHeartbeat(string axis, IModbusChannel channel, ushort ownerId, TimeSpan interval,
        TimeSpan expiry, ILogger? logger = null, TimeProvider? time = null)
    {
        if (ownerId == AdvisoryLease.Unowned)
            throw new ArgumentOutOfRangeException(nameof(ownerId),
                "0 is the unowned marker in D131; an owner id must be a non-zero station-unique 16-bit value");

        _axis = axis;
        _channel = channel;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        OwnerId = ownerId;
        Interval = interval;
        Expiry = expiry;
    }

    /// <summary>This instance's station-unique 16-bit owner id, written into D131 once granted.</summary>
    public ushort OwnerId { get; }

    /// <summary>How often the beat is written. FR-11 (D-f): ≥ 5 Hz.</summary>
    public TimeSpan Interval { get; }

    /// <summary>Lease expiry, equal to the drive-side stall window. FR-11 (D-f): 1 s.</summary>
    public TimeSpan Expiry { get; }

    /// <summary>The last value written into D130. Never 0 once the beat has started.</summary>
    public ushort LastBeat => Volatile.Read(ref _beat);

    /// <summary>Raised after every beat and its paired read, carrying the watchdog registers.</summary>
    public event EventHandler<HeartbeatTick>? Ticked;

    /// <summary>
    /// Raised once on the rising edge of a latched watchdog fault (D132 leaving 0). The axis turns
    /// this into <see cref="AxisState.ErrorStop"/> with <see cref="MotionError.WatchdogTripped"/>;
    /// recovery is reset + re-command, never a re-home.
    /// </summary>
    public event EventHandler<ushort>? WatchdogTripped;

    // ═══════════════════════ the advisory lease ═══════════════════════

    /// <summary>
    /// Evaluates the lease <b>once</b>, doing whatever reading the rule needs.
    ///
    /// <para>
    /// An unowned register, or one already carrying our own id, is decided from that read alone —
    /// the incumbent's heartbeat age does not enter those two rows of the rule. Only a
    /// <i>foreign</i> owner costs the sampling wait: D130 is read, the expiry window is waited out,
    /// and D130 is read again. Unchanged means the incumbent is at least <see cref="Expiry"/> stale;
    /// changed means it is alive. Skipping that wait and trusting one stale read is the mistake
    /// vector row 2 exists to catch.
    /// </para>
    /// </summary>
    public async Task<LeaseDecision> TryAcquireAsync(CancellationToken ct)
    {
        var owner = await ReadRegisterAsync(DeltaRegisters.D131_OwnerId, "read lease owner", ct);

        var age = owner != AdvisoryLease.Unowned && owner != OwnerId
            ? await SampleHeartbeatAgeAsync(ct)
            : TimeSpan.Zero;

        var decision = AdvisoryLease.Evaluate(owner, age, Expiry, OwnerId);
        if (!decision.Granted)
        {
            _logger?.LogWarning("{Axis}: refusing to attach to {Host} — {Reason}",
                _axis, _channel.Host, decision.Reason);
            return decision;
        }

        await WriteRegisterAsync(DeltaRegisters.D131_OwnerId, OwnerId, "claim lease", ct);
        _leaseHeld = true;
        _logger?.LogInformation("{Axis}: lease on {Host} taken by owner {Owner} — {Reason}",
            _axis, _channel.Host, OwnerId, decision.Reason);
        return decision;
    }

    /// <summary>
    /// How long D130 has been unchanged, obtained the only way it can be: by sampling the register
    /// across the expiry window. Unchanged for the whole window means at least <see cref="Expiry"/>
    /// old; a change at any point means the incumbent is alive and the age is zero.
    ///
    /// <para>
    /// The wait is re-checked against a <b>measured</b> elapsed time rather than trusted, because a
    /// platform timer asked for 1 s can fire at 985 ms. Reporting that as the age would refuse a
    /// genuinely dead lease on a rounding error and cost a whole retry second; reporting the full
    /// window instead would claim an observation that was never made. Waiting out the remainder is
    /// the only answer that is both correct and honest.
    /// </para>
    /// </summary>
    private async Task<TimeSpan> SampleHeartbeatAgeAsync(CancellationToken ct)
    {
        var before = await ReadRegisterAsync(DeltaRegisters.D130_Heartbeat, "sample heartbeat", ct);
        var start = _time.GetTimestamp();

        while (true)
        {
            var remaining = Expiry - _time.GetElapsedTime(start);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, _time, ct);

            var after = await ReadRegisterAsync(DeltaRegisters.D130_Heartbeat, "sample heartbeat", ct);
            if (after != before) return TimeSpan.Zero;

            var elapsed = _time.GetElapsedTime(start);
            if (elapsed >= Expiry) return elapsed;
        }
    }

    /// <summary>
    /// Takes the lease, retrying at <see cref="AdvisoryLease.RetryInterval"/> until it is granted —
    /// which is what lets a rolling deploy attach as soon as the outgoing instance stops beating.
    /// </summary>
    /// <param name="timeout">How long to keep retrying, or <see langword="null"/> to retry until
    /// <paramref name="ct"/> fires.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="MotionException"><see cref="MotionError.LeaseHeld"/> — the timeout elapsed
    /// with a live foreign heartbeat still on the drive. The message names the owner that was seen.</exception>
    public async Task AcquireAsync(TimeSpan? timeout, CancellationToken ct)
    {
        var deadline = timeout is { } t ? _time.GetUtcNow() + t : (DateTimeOffset?)null;
        LeaseDecision decision;

        while (true)
        {
            decision = await TryAcquireAsync(ct);
            if (decision.Granted) return;
            if (deadline is { } d && _time.GetUtcNow() >= d) break;

            await Task.Delay(AdvisoryLease.RetryInterval, _time, ct);
        }

        throw new MotionException(MotionError.LeaseHeld,
            $"{_axis}: cannot attach to {_channel.Host} — {decision.Reason}", _axis);
    }

    // ═══════════════════════ the beat ═══════════════════════

    /// <summary>Starts beating. Idempotent.</summary>
    public void Start()
    {
        if (_loop is not null) return;
        _loop = RunAsync(_stop.Token);
        _logger?.LogInformation("{Axis}: heartbeat started on {Host} at {Hz:F1} Hz (owner {Owner}, stall window {Stall:F1} s)",
            _axis, _channel.Host, 1.0 / Interval.TotalSeconds, OwnerId, Expiry.TotalSeconds);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    await BeatOnceAsync(ct);
                }
                catch (MotionException ex)
                {
                    // A missed beat is not fatal to the process — the drive's own watchdog is the
                    // backstop, and the next tick retries. Losing the channel entirely surfaces
                    // through the axis's own reads.
                    _logger?.LogWarning("{Axis}: heartbeat beat failed — {Message}", _axis, ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Asked to stop.
        }
    }

    /// <summary>One beat and its paired read. Internal so a test can step it deterministically.</summary>
    internal async Task BeatOnceAsync(CancellationToken ct)
    {
        var next = NextBeat(_beat);
        await WriteRegisterAsync(DeltaRegisters.D130_Heartbeat, next, "heartbeat", ct);
        Volatile.Write(ref _beat, next);

        // Paired read: D132 and D133 in one transaction. This is where a trip is noticed, and its
        // cadence is what gives an idle axis a StatusChanged rhythm.
        var words = await _channel.ReadHoldingAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D132_WatchdogFault,
            2, "read watchdog", ChannelPriority.Heartbeat, ct);

        var tick = new HeartbeatTick(next, words[0], words[1]);
        Ticked?.Invoke(this, tick);

        if (words[0] != DeltaRegisters.WatchdogHealthy && _lastFault == DeltaRegisters.WatchdogHealthy)
        {
            _logger?.LogError(
                "{Axis}: WATCHDOG TRIPPED on {Host} — D132 = {Fault}, {Trips} trip(s) since power-up. "
                + "Run state dropped and limit functions re-asserted by the ladder; the home latch is "
                + "untouched, so recovery is reset + re-command without re-homing",
                _axis, _channel.Host, words[0], words[1]);
            WatchdogTripped?.Invoke(this, words[0]);
        }

        _lastFault = words[0];
    }

    /// <summary>
    /// The next beat value, skipping 0 on wrap. 0 is not a heartbeat — it is the value the register
    /// powers up at, and writing it can leave the drive's network unarmed.
    /// </summary>
    internal static ushort NextBeat(ushort current)
    {
        var next = (ushort)(current + 1);
        return next == 0 ? (ushort)1 : next;
    }

    /// <summary>
    /// Clears a latched watchdog fault by writing 0 into D132 — the only thing that clears it. The
    /// drive's network then disarms until a new beat arrives with D132 already clear.
    /// </summary>
    public async Task ClearWatchdogFaultAsync(CancellationToken ct)
    {
        await WriteRegisterAsync(DeltaRegisters.D132_WatchdogFault, DeltaRegisters.WatchdogHealthy,
            "clear watchdog fault", ct);
        _lastFault = DeltaRegisters.WatchdogHealthy;
        _logger?.LogInformation("{Axis}: watchdog fault cleared on {Host}", _axis, _channel.Host);
    }

    /// <summary>
    /// Stops beating and releases the lease, so a successor attaches at once instead of waiting out
    /// the expiry window. A process that is killed rather than stopped cannot do this — which is
    /// precisely the case the drive-side watchdog exists for.
    /// </summary>
    public async Task StopAsync()
    {
        if (_loop is null) return;

        await _stop.CancelAsync();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        _loop = null;

        if (!_leaseHeld) return;
        try
        {
            var owner = await ReadRegisterAsync(DeltaRegisters.D131_OwnerId, "read lease owner",
                CancellationToken.None);
            if (owner == OwnerId)
                await WriteRegisterAsync(DeltaRegisters.D131_OwnerId, AdvisoryLease.Unowned,
                    "release lease", CancellationToken.None);
        }
        catch (MotionException ex)
        {
            // The drive is unreachable; the lease expires on its own one stall window later.
            _logger?.LogDebug(ex, "{Axis}: could not release the lease on {Host}", _axis, _channel.Host);
        }

        _leaseHeld = false;
    }

    /// <remarks>
    /// A plain await, deliberately. The inherited <c>ContinueWith(…, OnlyOnRanToCompletion)</c> shape
    /// turns a FAULTED read into a CANCELLED task, so a drive that had gone away surfaced as
    /// <see cref="TaskCanceledException"/> — "somebody cancelled" rather than "the transport is
    /// dead", past every <c>catch (MotionException)</c> written to handle it.
    /// </remarks>
    /// <summary>
    /// Stops beating <b>without</b> the round-trip that releases the lease — the teardown a
    /// synchronous <c>Dispose</c> can honestly perform.
    ///
    /// <para>
    /// A synchronous dispose cannot do network I/O without blocking a thread on a socket, so it does
    /// not try. The lease is simply left to expire on its own one stall window later, which is
    /// exactly the situation FR-11's watchdog exists to bound. Prefer <see cref="StopAsync"/> or
    /// <see cref="DisposeAsync"/>, which release it immediately.
    /// </para>
    /// </summary>
    public void Abandon()
    {
        if (_loop is null) return;

        try { _stop.Cancel(); } catch (ObjectDisposedException) { /* already torn down */ }
        _loop = null;
        _logger?.LogDebug("{Axis}: heartbeat abandoned without releasing the lease on {Host}; it "
            + "expires in {Expiry:0.##} s", _axis, _channel.Host, Expiry.TotalSeconds);
    }

    private async Task<ushort> ReadRegisterAsync(ushort address, string what, CancellationToken ct)
    {
        var words = await _channel.ReadHoldingAsync(DeltaRegisters.PlcUnit, address, 1, what,
            ChannelPriority.Heartbeat, ct);
        return words[0];
    }

    private Task WriteRegisterAsync(ushort address, ushort value, string what, CancellationToken ct) =>
        _channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, address, value, what,
            ChannelPriority.Heartbeat, ct);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stop.Dispose();
    }
}

/// <summary>One beat and the watchdog registers read back with it.</summary>
/// <param name="Beat">The value just written into D130 — never 0.</param>
/// <param name="WatchdogFault">D132: 0 healthy, 1 heartbeat stall.</param>
/// <param name="TripCount">D133: trips since power-up.</param>
internal readonly record struct HeartbeatTick(ushort Beat, ushort WatchdogFault, ushort TripCount);
