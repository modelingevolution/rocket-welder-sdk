using Microsoft.Extensions.Logging;
using ModelingEvolution.Drawing;
using ModelingEvolution.Drawing.Units;

namespace RocketWelder.SDK.Devices.Motion.Delta;

/// <summary>
/// One rotary axis of a Delta VFD-C2000 positioner, on the epic-065 motion contract.
///
/// <para>
/// The drive runs in SPEED mode and this class derives position itself, because the encoders sit
/// behind the gearboxes and the drive's own positioning modes are unusable there. Everything below —
/// homing, the staged or continuous approach, the micro-pulse endgame — exists for that reason. The
/// approach machinery is Daniel's bring-up port, moved onto the contract rather than rewritten: its
/// lead distances, braking margins and handover point are <b>bench-measured</b> and are not
/// re-tuned here, and never against the simulator.
/// </para>
///
/// <para>
/// <b>State is single and explicit</b> (FR-1). <see cref="State"/> is the one truth; there is no
/// separate "busy" flag, because "not <see cref="AxisState.Standstill"/>" <i>is</i> busy. A motion
/// command from any other state is rejected with <see cref="MotionError.Busy"/> and leaves the state
/// intact — a deliberate departure from PLCopen, which would drive the axis to
/// <see cref="AxisState.ErrorStop"/> for a caller's mistiming.
/// </para>
/// </summary>
public sealed class DeltaAxis : IRotaryAxis, ISelfCheckingAxis, IDisposable
{
    private static readonly ushort JogRampRaw = 200;      // 2.00 s per 50 Hz
    private static readonly ushort NudgeRampRaw = 20;     // 0.20 s per 50 Hz
    private static readonly TimeSpan JogStopTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MoveTimeout = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ApproachPoll = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan SmoothPoll = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan JogStopPoll = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan IdleStatusPoll = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Written into the latch register before arming the home latch. The PLC overwrites it with the
    /// captured position, so an unchanged value proves the ladder did not run.
    ///
    /// <para>
    /// This is the only reliable test. Comparing against the PREVIOUS latch value cannot work: the
    /// ladder's <c>DSUB</c> runs BEFORE its <c>DMOV</c>, the axis returns to the same cam edge every
    /// time and the encoder is not reset between runs, so a correctly working latch stores the same
    /// number again — and the better the machine's repeatability, the more often that happens.
    /// </para>
    ///
    /// <para>Chosen well outside encoder range but small enough that the ladder's DSUB against it
    /// cannot overflow a signed 32-bit result.</para>
    /// </summary>
    private const int LatchSentinel = 1_000_000_000;

    private readonly DeltaAxisConfig _cfg;
    private readonly IModbusChannel _channel;
    private readonly IAxisStateStore _store;
    private readonly ILogger? _logger;
    private readonly Lock _sync = new();

    private long _zeroOffset;
    private bool _homed;
    private Frequency<double> _moveHz;
    private AxisState _state = AxisState.Disabled;
    private MotionError? _error;
    private CancellationTokenSource? _abort;
    private Task? _velocitySupervisor;
    private AxisStatus _status;
    private DateTimeOffset _lastIdleStatus = DateTimeOffset.MinValue;
    private string? _setupError;

    internal DeltaAxis(DeltaAxisConfig cfg, IModbusChannel channel, IAxisStateStore store, ILogger? logger)
    {
        _cfg = cfg;
        _channel = channel;
        _store = store;
        _logger = logger;
        _moveHz = cfg.MoveHz;
        _status = new AxisStatus(AxisState.Disabled, null, 0.0, LimitSwitchState.None, null);
    }

    /// <summary>Configuration this axis was built from.</summary>
    public DeltaAxisConfig Config => _cfg;

    // ═══════════════════════ identity and capability ═══════════════════════

    /// <inheritdoc/>
    public string Name => _cfg.Name;

    /// <inheritdoc/>
    public string DisplayName => _cfg.DisplayName;

    /// <inheritdoc/>
    public AxisState State
    {
        get { lock (_sync) return _state; }
    }

    /// <inheritdoc/>
    public AxisCapabilities Capabilities =>
        AxisCapabilities.Homing | (_cfg.Continuous ? AxisCapabilities.ContinuousRotation : AxisCapabilities.None);
    // Synchronised is deliberately absent: this epic delivers no coordinated motion, and the flag
    // exists so a caller asking gets a truthful "no" rather than a silently degraded move (AC-18).

    /// <summary>
    /// The axis is powered and will accept a motion command. <b>Derived</b> from
    /// <see cref="State"/> — never stored, so it cannot contradict it (FR-1 / AC-1).
    /// </summary>
    public bool IsReady => State == AxisState.Standstill;

    /// <summary>Motion is in progress. <b>Derived</b> from <see cref="State"/>.</summary>
    public bool IsMoving => State is AxisState.Homing or AxisState.DiscreteMotion
        or AxisState.ContinuousMotion or AxisState.Stopping;

    /// <summary>
    /// The zero is known. Deliberately NOT derived from <see cref="State"/>: "where zero is" is not
    /// a PLCopen state, it is a fact that outlives the process (see <see cref="IAxisStateStore"/>).
    /// It is single-valued and cannot contradict the state — which is what AC-1 forbids.
    /// </summary>
    public bool IsHomed
    {
        get { lock (_sync) return _homed; }
    }

    // ═══════════════════════ typed reads and bounds ═══════════════════════

    /// <inheritdoc/>
    public Degree<double>? Angle => Status.Position is { } p ? Degree<double>.Create(p) : null;

    /// <inheritdoc/>
    public Degree<double> Min => _cfg.Min;

    /// <inheritdoc/>
    public Degree<double> Max => _cfg.Max;

    /// <inheritdoc/>
    public Degree<double> Tolerance => _cfg.Tolerance;

    /// <inheritdoc/>
    public AngularSpeed<double, DegreePerSecond<double>> MinSpeed =>
        _cfg.SpeedCalibration.ToAngularSpeed(_cfg.MinJogHz);

    /// <inheritdoc/>
    public AngularSpeed<double, DegreePerSecond<double>> MaxSpeed =>
        _cfg.SpeedCalibration.ToAngularSpeed(_cfg.MaxMoveHz);

    /// <inheritdoc/>
    public AxisStatus Status
    {
        get { lock (_sync) return _status; }
    }

    /// <inheritdoc/>
    public event EventHandler<AxisStatus>? StatusChanged;

    // ═══════════════════════ lifecycle ═══════════════════════

    internal async Task InitialiseAsync(CancellationToken ct)
    {
        if (await _store.LoadAsync(_cfg.Name, ct) is { } saved)
        {
            lock (_sync)
            {
                _zeroOffset = saved.ZeroOffset;
                _homed = saved.Homed;
            }

            _moveHz = Frequency<double>.FromHertz(_cfg.SpeedCalibration.ToHz(saved.SpeedDegPerSecond));
        }

        await ApplyDriveSetupAsync(ct);
    }

    /// <summary>
    /// Forces the drive parameters this adapter depends on, so behaviour does not depend on what was
    /// last typed into the keypad. Writes only what differs — parameter writes need a stopped drive,
    /// so this runs before any motion.
    /// </summary>
    private async Task ApplyDriveSetupAsync(CancellationToken ct)
    {
        try
        {
            await SetCoilAsync(DeltaRegisters.M4_Move, false, ChannelPriority.Move, ct);
            await SetCoilAsync(DeltaRegisters.M0_Run, false, ChannelPriority.Move, ct);

            var required = _cfg.LimitInputs is null
                ? DeltaRegisters.RequiredSetup
                : [.. DeltaRegisters.RequiredSetup, .. DeltaRegisters.LimitSetup];

            foreach (var (address, value, why) in required)
            {
                var current = await _channel.ReadHoldingAsync(DeltaRegisters.DriveUnit, address, 1, why,
                    ChannelPriority.Move, ct);
                if (current[0] == value) continue;
                _logger?.LogInformation("{Axis}: {Why} (was {Current}, setting {Value})",
                    _cfg.Name, why, current[0], value);
                await _channel.WriteRegisterAsync(DeltaRegisters.DriveUnit, address, value, why,
                    ChannelPriority.Move, ct);
            }

            _setupError = null;
        }
        catch (MotionException ex)
        {
            // Recorded but NOT latched as an axis fault: a startup problem must not make every later
            // successful move look failed.
            _setupError = ex.Message;
            _logger?.LogWarning(ex, "{Axis}: drive setup failed", _cfg.Name);
        }
    }

    /// <summary>
    /// The FR-11 heartbeat's paired read arriving. On an idle axis it is what gives
    /// <see cref="StatusChanged"/> a cadence; during a move the move loop is already reading, so this
    /// stands aside rather than adding traffic to a busy channel.
    /// </summary>
    internal async Task OnHeartbeatTickAsync(CancellationToken ct)
    {
        if (IsMoving) return;

        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            if (now - _lastIdleStatus < IdleStatusPoll) return;
            _lastIdleStatus = now;
        }

        try
        {
            await ReadStatusAsync(ct);
        }
        catch (MotionException ex)
        {
            _logger?.LogDebug(ex, "{Axis}: idle status poll failed", _cfg.Name);
        }
    }

    /// <summary>The drive's watchdog latched a stall while this process was not the one commanding.</summary>
    internal void OnWatchdogTripped()
    {
        Fault(MotionError.WatchdogTripped,
            $"{_cfg.Name}: the drive's dead-commander watchdog tripped (D132). The run state was "
            + "dropped and the limit functions re-asserted; the home latch is untouched, so recovery "
            + "is ResetAsync + re-command, not a re-home");
    }

    // ═══════════════════════ status ═══════════════════════

    /// <inheritdoc/>
    public async Task<AxisStatus> ReadStatusAsync(CancellationToken ct = default)
    {
        AxisStatus status;
        try
        {
            var pulses = await ReadPositionAsync(ct);
            var coils = await _channel.ReadCoilsAsync(DeltaRegisters.PlcUnit, DeltaRegisters.M0_Run, 6,
                "coils", ChannelPriority.Move, ct);
            var inputs = await ReadInputsAsync(ct);
            var outHz = await ReadOutputHzAsync(ct);
            var fault = await ReadFaultAsync(ct);

            var moving = outHz.Hertz > 0.2;
            var limits = LimitsOf(inputs);

            // Signed speed: the magnitude comes from the calibration, the sign from the direction
            // coil resolved back into ANGLE space. There is no direction field (P-2).
            var magnitude = _cfg.SpeedCalibration.ToDegPerSecond(outHz.Hertz);
            var countDirection = coils[5] != _cfg.InvertDirection ? CountDirection.Down : CountDirection.Up;
            var signedSpeed = moving ? magnitude * AngleSignOf(countDirection) : 0.0;

            lock (_sync)
            {
                if (fault != 0 && _state != AxisState.ErrorStop)
                {
                    _state = AxisState.ErrorStop;
                    _error = MotionError.DriveFault;
                    _logger?.LogError("{Axis}: drive reported fault {Fault}", _cfg.Name, fault);
                }

                status = new AxisStatus(_state, PositionOf(pulses), signedSpeed, limits, _error);
                _status = status;
            }
        }
        catch (MotionException ex)
        {
            lock (_sync)
            {
                status = _status with { State = _state, Error = _error ?? MotionError.CommunicationLost };
                _status = status;
            }

            _logger?.LogWarning(ex, "{Axis}: status read failed", _cfg.Name);
            throw;
        }

        StatusChanged?.Invoke(this, status);
        return status;
    }

    /// <summary>
    /// The displayed position, or <see langword="null"/> when the zero is not known — an unhomed axis
    /// that needs homing has no meaningful angle, and reporting a number derived from wherever the
    /// encoder happened to power up would be a confident lie.
    /// </summary>
    private double? PositionOf(long pulses)
    {
        lock (_sync)
        {
            if (_cfg.RequiresHoming && !_homed) return null;
        }

        return (double)PulsesToDegrees(pulses);
    }

    // ═══════════════════════ lifecycle commands ═══════════════════════

    /// <inheritdoc/>
    public async Task PowerAsync(bool on, CancellationToken ct = default)
    {
        if (!on)
        {
            await HardStopAsync(ct);
            lock (_sync)
            {
                _state = AxisState.Disabled;
                _error = null;
            }

            _logger?.LogInformation("{Axis}: powered off", _cfg.Name);
            return;
        }

        lock (_sync)
        {
            if (_state is not (AxisState.Disabled or AxisState.Standstill))
                throw new MotionException(MotionError.Busy,
                    _state == AxisState.ErrorStop
                        ? $"{_cfg.Name}: a fault is latched — call ResetAsync, which is the only exit from ErrorStop"
                        : $"{_cfg.Name}: cannot power on while the axis is {_state}", _cfg.Name);
        }

        await SetCoilAsync(DeltaRegisters.M0_Run, true, ChannelPriority.Move, ct);
        lock (_sync) _state = AxisState.Standstill;
        _logger?.LogInformation("{Axis}: powered on", _cfg.Name);
    }

    /// <inheritdoc/>
    public Task HomeAsync(CancellationToken ct = default) =>
        RunOperationAsync(AxisState.Homing, HomeCoreAsync, ct);

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct = default)
    {
        // Cancel first so a running approach stops commanding new motion, THEN ramp down. The other
        // order lets the control loop issue another step while the axis is still decelerating.
        CancellationTokenSource? abort;
        lock (_sync)
        {
            abort = _abort;
            if (_state is AxisState.Homing or AxisState.DiscreteMotion or AxisState.ContinuousMotion)
                _state = AxisState.Stopping;
        }

        if (abort is not null) await abort.CancelAsync();

        // Stop lane: this is the write NFR-5's 200 ms is measured to, and it must not queue behind a
        // 26 s homing hold (AC-23).
        await JogStopAsync(ChannelPriority.Stop, ct);

        var supervisor = Volatile.Read(ref _velocitySupervisor);
        if (supervisor is not null)
        {
            try { await supervisor; } catch (OperationCanceledException) { /* expected */ }
        }

        lock (_sync)
        {
            if (_state == AxisState.Stopping) _state = AxisState.Standstill;
        }
    }

    /// <inheritdoc/>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        await HardStopAsync(ct);
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        await ResetDriveFaultAsync(ct);

        // The watchdog latch is cleared by the client writing 0 — nothing else clears it. Done
        // through the same channel the heartbeat uses, so it applies whether or not one is running.
        await _channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D132_WatchdogFault,
            DeltaRegisters.WatchdogHealthy, "clear watchdog fault", ChannelPriority.Stop, ct);

        await SetCoilAsync(DeltaRegisters.M0_Run, true, ChannelPriority.Move, ct);

        lock (_sync)
        {
            _error = null;
            _state = AxisState.Standstill;
        }

        _logger?.LogInformation("{Axis}: reset — fault cleared, axis back to Standstill "
            + "(the home latch was never touched, so no re-home is needed)", _cfg.Name);
    }

    // ═══════════════════════ positioning ═══════════════════════

    /// <inheritdoc/>
    public Task MoveAbsoluteAsync(Degree<double> target,
        AngularSpeed<double, DegreePerSecond<double>>? speed = null,
        RotationSense sense = RotationSense.Shortest,
        CancellationToken ct = default)
        => MoveAbsoluteCoreAsync(target, ResolveSpeed(speed), sense, ct);

    /// <inheritdoc/>
    public Task MoveAbsoluteAsync(Degree<double> target, Percentage speedOfMax,
        RotationSense sense = RotationSense.Shortest,
        CancellationToken ct = default)
        => MoveAbsoluteCoreAsync(target, ResolveSpeed(speedOfMax), sense, ct);

    /// <inheritdoc/>
    public Task MoveRelativeAsync(Degree<double> delta,
        AngularSpeed<double, DegreePerSecond<double>>? speed = null,
        CancellationToken ct = default)
        => MoveRelativeCoreAsync(delta, ResolveSpeed(speed), ct);

    /// <inheritdoc/>
    public Task MoveRelativeAsync(Degree<double> delta, Percentage speedOfMax,
        CancellationToken ct = default)
        => MoveRelativeCoreAsync(delta, ResolveSpeed(speedOfMax), ct);

    private async Task MoveAbsoluteCoreAsync(Degree<double> target, Frequency<double> hz,
        RotationSense sense, CancellationToken ct)
    {
        EnsureStandstill();
        EnsureSenseSupported(sense);
        EnsureHomed();

        var current = UnwrappedAngle(await ReadPositionAsync(ct));
        var destination = _cfg.Continuous
            ? current + WrappedTravel(current, (double)target, sense)
            : RequireInRange((double)target);

        await RunOperationAsync(AxisState.DiscreteMotion,
            token => MoveCoreAsync(destination, hz, token), ct);
    }

    private async Task MoveRelativeCoreAsync(Degree<double> delta, Frequency<double> hz, CancellationToken ct)
    {
        EnsureStandstill();
        EnsureHomed();

        var current = UnwrappedAngle(await ReadPositionAsync(ct));
        var destination = current + (double)delta;

        // A relative move is unbounded on a wrapping axis — a +720° delta really turns twice, which
        // is exactly why the whole move loop works in UNWRAPPED angle rather than re-deciding the
        // shortest path on every iteration. On a limited axis the resulting target is range-checked.
        if (!_cfg.Continuous) RequireInRange(destination);

        await RunOperationAsync(AxisState.DiscreteMotion,
            token => MoveCoreAsync(destination, hz, token), ct);
    }

    /// <inheritdoc/>
    public async Task MoveVelocityAsync(AngularSpeed<double, DegreePerSecond<double>> velocity,
        CancellationToken ct = default)
    {
        EnsureStandstill();
        var hz = RequireReachable(velocity);
        var direction = ToCount(velocity.Value >= 0);

        CancellationTokenSource abort;
        lock (_sync)
        {
            if (_state != AxisState.Standstill)
                throw new MotionException(MotionError.Busy,
                    $"{_cfg.Name}: another operation is running (axis is {_state})", _cfg.Name);

            _state = AxisState.ContinuousMotion;
            _error = null;
            abort = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _abort = abort;
        }

        try
        {
            await SetCoilAsync(DeltaRegisters.M0_Run, true, ChannelPriority.Move, abort.Token);
            await JogStartAsync(hz, direction, JogRampRaw, abort.Token);
            await WaitForCommandedVelocityAsync(hz, abort.Token);
        }
        catch
        {
            await AbandonVelocityAsync(abort);
            throw;
        }

        // MC_MoveVelocity semantics: the task completes once the commanded velocity is reached, but
        // the axis stays in ContinuousMotion until StopAsync — and `ct` REMAINS OBSERVED after this
        // returns, so cancelling it stops the axis. That is what the supervisor is for; on an axis
        // with travel limits it also watches them, because this jog is otherwise open-loop.
        Volatile.Write(ref _velocitySupervisor, SuperviseVelocityAsync(direction, abort));
    }

    private async Task WaitForCommandedVelocityAsync(Frequency<double> hz, CancellationToken ct)
    {
        // The drive ramps at D111/D112; give it the programmed ramp plus a margin, then accept
        // whatever it reached rather than failing a move that is plainly running.
        var rampSeconds = JogRampRaw / 100.0 * (hz.Hertz / 50.0) + 1.0;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(rampSeconds);
        var wanted = hz.Hertz * 0.9;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if ((await ReadOutputHzAsync(ct)).Hertz >= wanted) return;
            await Task.Delay(SmoothPoll, ct);
        }

        _logger?.LogWarning("{Axis}: commanded {Hz:0.##} Hz was not observed within the programmed ramp; "
            + "continuing in ContinuousMotion", _cfg.Name, hz.Hertz);
    }

    private async Task SuperviseVelocityAsync(CountDirection direction, CancellationTokenSource abort)
    {
        try
        {
            while (!abort.IsCancellationRequested)
            {
                await Task.Delay(ApproachPoll, abort.Token);
                if (_cfg.LimitInputs is null) continue;

                var inputs = await ReadInputsAsync(abort.Token);
                if (LimitHit(inputs, direction) is not { } hit) continue;

                await JogStopAsync(ChannelPriority.Stop, CancellationToken.None);
                Fault(MotionError.LimitTripped, $"{_cfg.Name}: {hit} travel limit tripped during continuous motion");
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // StopAsync or the caller's token: StopAsync does the ramp-down itself.
        }
        catch (MotionException ex)
        {
            Fault(ex.Error, ex.Message);
        }
    }

    private async Task AbandonVelocityAsync(CancellationTokenSource abort)
    {
        await JogStopAsync(ChannelPriority.Stop, CancellationToken.None);
        lock (_sync)
        {
            if (_state == AxisState.ContinuousMotion) _state = AxisState.Standstill;
            if (ReferenceEquals(_abort, abort)) _abort = null;
        }

        abort.Dispose();
    }

    // ═══════════════════════ commissioning self-check (FR-7) ═══════════════════════

    /// <inheritdoc/>
    public Task VerifyDirectionAsync(CancellationToken ct = default) =>
        RunOperationAsync(AxisState.DiscreteMotion, VerifyDirectionCoreAsync, ct);

    private async Task VerifyDirectionCoreAsync(CancellationToken ct)
    {
        // The jog below runs open-loop for a fixed time and does NOT watch the limits, so on an axis
        // that has them, refuse to start from a tripped one — the check would otherwise drive further
        // into it and leave the drive faulted.
        if (LimitHit(await ReadInputsAsync(ct), null) is { } tripped)
            throw new MotionException(MotionError.LimitTripped,
                $"{_cfg.Name}: cannot check direction while resting on the {tripped} limit — "
                + "move the axis clear first", _cfg.Name);

        await SetCoilAsync(DeltaRegisters.M0_Run, true, ChannelPriority.Move, ct);
        var before = await ReadPositionAsync(ct);
        var seek = Frequency<double>.FromHertz(Math.Max(_cfg.MinJogHz.Hertz, 5.0));
        await JogForAsync(CountDirection.Up, seek, TimeSpan.FromSeconds(1.5), ct);
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        var after = await ReadPositionAsync(ct);

        var moved = after - before;
        _logger?.LogInformation(
            "{Axis}: direction check — forward moved the count by {Moved} (expected positive)",
            _cfg.Name, moved);

        if (Math.Abs(moved) < 20)
            throw Mechanical("the axis did not move during the direction check "
                + "(commanded and not turning — jammed, unpowered, or below breakaway speed)");

        if (moved < 0)
            throw new MotionException(MotionError.DriveFault,
                $"{_cfg.Name}: the drive's forward direction DECREASES the raw count — the motor is "
                + "wired opposite to this adapter's convention. Re-wire, or set "
                + $"{nameof(DeltaAxisConfig.InvertDirection)} on this axis. Left uncorrected, "
                + "positioning drives the correct distance the WRONG way and reads as a broken "
                + "control loop rather than a wiring fault", _cfg.Name);
    }

    // ═══════════════════════ operation plumbing ═══════════════════════

    /// <summary>
    /// Runs one exclusive operation. The <b>state itself is the mutual exclusion</b>: entering an
    /// operation means leaving <see cref="AxisState.Standstill"/> under the lock, so two concurrent
    /// commands cannot both start and there is no second "busy" flag that could disagree with the
    /// state (FR-1 / AC-1).
    /// </summary>
    private async Task RunOperationAsync(AxisState working, Func<CancellationToken, Task> body,
        CancellationToken ct)
    {
        CancellationTokenSource abort;
        lock (_sync)
        {
            if (_state != AxisState.Standstill)
                throw new MotionException(MotionError.Busy,
                    _state == AxisState.ErrorStop
                        ? $"{_cfg.Name}: a fault is latched ({_error}) — call ResetAsync first"
                        : $"{_cfg.Name}: another operation is running (axis is {_state})", _cfg.Name);

            _state = working;
            _error = null;
            abort = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _abort = abort;
        }

        try
        {
            await body(abort.Token);
            lock (_sync)
            {
                if (_state == working) _state = AxisState.Standstill;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation stops the axis and leaves it in a state a subsequent command accepts
            // (AC-10). It is NOT a MotionError: an aborted Task is already the machine-readable
            // outcome, and ErrorStop is reserved for faults.
            await JogStopAsync(ChannelPriority.Stop, CancellationToken.None);
            lock (_sync)
            {
                if (_state is not AxisState.ErrorStop) _state = AxisState.Standstill;
            }

            throw;
        }
        catch (MotionException ex)
        {
            Fault(ex.Error, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            Fault(MotionError.DriveFault, $"{_cfg.Name}: {ex.Message}");
            throw new MotionException(MotionError.DriveFault, $"{_cfg.Name}: {ex.Message}", _cfg.Name);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_abort, abort)) _abort = null;
            }

            abort.Dispose();
        }
    }

    private void Fault(MotionError error, string message)
    {
        lock (_sync)
        {
            _state = AxisState.ErrorStop;
            _error = error;
            _status = _status with { State = AxisState.ErrorStop, Error = error };
        }

        _logger?.LogError("{Axis}: {Error} — {Message}", _cfg.Name, error, message);
        StatusChanged?.Invoke(this, Status);
    }

    private void EnsureStandstill()
    {
        var state = State;
        if (state == AxisState.Standstill) return;

        throw new MotionException(MotionError.Busy,
            state == AxisState.ErrorStop
                ? $"{_cfg.Name}: a fault is latched — call ResetAsync first"
                : $"{_cfg.Name}: a motion command was issued while the axis is {state}", _cfg.Name);
    }

    private void EnsureSenseSupported(RotationSense sense)
    {
        if (sense == RotationSense.Shortest || _cfg.Continuous) return;

        throw new MotionException(MotionError.UnsupportedSense,
            $"{_cfg.Name}: RotationSense.{sense} needs two paths to the target, and this axis does "
            + "not declare ContinuousRotation — only Shortest is meaningful here", _cfg.Name);
    }

    private void EnsureHomed()
    {
        if (!_cfg.RequiresHoming || IsHomed) return;

        throw new MotionException(MotionError.NotHomed,
            $"{_cfg.Name}: the axis is not homed, so no absolute angle is defined", _cfg.Name);
    }

    private double RequireInRange(double angle)
    {
        var lo = (double)_cfg.Min;
        var hi = (double)_cfg.Max;
        if (angle >= lo && angle <= hi) return angle;

        throw new MotionException(MotionError.OutOfRange,
            $"{_cfg.Name}: {angle:0.##}° is outside the travel range {lo:0.##}–{hi:0.##}°", _cfg.Name);
    }

    /// <summary>
    /// A failure of the mechanism that the frozen <see cref="MotionError"/> set has no member for —
    /// a stall, a positioning timeout, a move that stopped outside tolerance, or a home latch that
    /// never fired. All four are reported as <see cref="MotionError.DriveFault"/> with the specific
    /// cause in the message.
    /// </summary>
    /// <remarks>
    /// This is a deliberate lossy mapping, recorded in this project's <c>dev-log.md</c> as an open
    /// question for the reviewer: the contract froze before the adapter landed, and the four causes
    /// share one caller response (reset and re-command, possibly re-home) but are not "the drive
    /// reported a fault of its own". An additive <c>MotionError.MotionFailed</c> would say it
    /// honestly; adding one is a contract change and is not made here.
    /// </remarks>
    private MotionException Mechanical(string what) =>
        new(MotionError.DriveFault, $"{_cfg.Name}: {what}", _cfg.Name);

    // ═══════════════════════ speed resolution (FR-5) ═══════════════════════

    private Frequency<double> ResolveSpeed(AngularSpeed<double, DegreePerSecond<double>>? speed) =>
        speed is { } s ? RequireReachable(s) : _moveHz;

    /// <summary>
    /// Resolves a percentage against <see cref="MaxSpeed"/> <b>first</b>, and then subjects the
    /// result to the same rejection rule as any other speed — so <c>Percentage(1)</c> of a fast axis
    /// that lands below <see cref="MinSpeed"/> is rejected, not quietly raised to the floor (FR-5).
    /// </summary>
    private Frequency<double> ResolveSpeed(Percentage speedOfMax) => RequireReachable(MaxSpeed * speedOfMax);

    /// <summary>
    /// Rejects a speed outside the axis's achievable range — never clamps it. A caller must be able
    /// to learn that the speed it asked for is not the speed it would have got (FR-5 / AC-6).
    /// The <b>magnitude</b> is what is checked: a signed velocity's sign is direction, not speed.
    /// </summary>
    private Frequency<double> RequireReachable(AngularSpeed<double, DegreePerSecond<double>> speed)
    {
        var magnitude = new AngularSpeed<double, DegreePerSecond<double>>(Math.Abs(speed.Value));
        if (magnitude < MinSpeed || magnitude > MaxSpeed)
            throw new MotionException(MotionError.UnreachableSpeed,
                $"{_cfg.Name}: {magnitude} is outside the axis's range {MinSpeed}–{MaxSpeed} "
                + $"({_cfg.MinJogHz.Hertz:0.##}–{_cfg.MaxMoveHz.Hertz:0.##} Hz)", _cfg.Name);

        return _cfg.SpeedCalibration.ToFrequency(magnitude);
    }

    // ═══════════════════════ homing ═══════════════════════

    /// <summary>
    /// Software homing. The zero MUST always be captured on the same edge, because the sensor cam
    /// has width: (1) find the cam from wherever we are, (2) drive fully off it the other way,
    /// (3) creep back on — that edge is the zero, latched by the PLC.
    /// </summary>
    private async Task HomeCoreAsync(CancellationToken ct)
    {
        var org = _cfg.HomeSensorInput;
        const CountDirection approach = CountDirection.Up;
        const CountDirection back = CountDirection.Down;
        var seek = _cfg.SeekHz;
        var fine = Frequency<double>.FromHertz(Math.Max(_cfg.MinJogHz.Hertz, seek.Hertz / 3.0));

        await SetCoilAsync(DeltaRegisters.M0_Run, true, ChannelPriority.Move, ct);
        var inputs = await ReadInputsAsync(ct);

        if (LimitHit(inputs, null) is { } tripped)
        {
            await ReleaseLimitAsync(tripped, ct);
            inputs = await ReadInputsAsync(ct);
        }

        if (inputs[org])   // not on the cam yet — go find it
        {
            try
            {
                await JogUntilAsync(approach, seek, x => !x[org], "searching for the home sensor", MoveTimeout, ct);
            }
            catch (MotionException ex) when (ex.Error == MotionError.LimitTripped)
            {
                inputs = await ReadInputsAsync(ct);
                if (LimitHit(inputs, null) is { } hit) await ReleaseLimitAsync(hit, ct);
                await JogUntilAsync(back, seek, x => !x[org], "searching for the home sensor", MoveTimeout, ct);
            }
        }

        await JogUntilAsync(back, seek, x => x[org], "leaving the home sensor", MoveTimeout / 3, ct);
        await JogForAsync(back, seek, TimeSpan.FromSeconds(1.5), ct);   // clear of the whole edge

        // Sentinel BEFORE arming. The other order lets the latch fire and then be overwritten — and
        // because the ladder runs DSUB before DMOV, a correctly working latch writes the SAME number
        // back when the axis returns to the same cam edge, so nothing but a sentinel distinguishes
        // "latched again" from "never ran".
        await _channel.WriteDWordAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D120_HomeLatch,
            LatchSentinel, "arm home latch", ChannelPriority.Move, ct);
        await SetCoilAsync(DeltaRegisters.M6_ArmLatch, true, ChannelPriority.Move, ct);
        long latched;
        try
        {
            // Ends on SENSOR DETECTION as well as on the latch changing: the cam sits close to the
            // travel limit, and stopping late used to run the axis onto the limit instead.
            await JogUntilAsync(approach, fine,
                x => !x[org] || LatchFiredNoWait(),
                "creeping onto the home edge", MoveTimeout, ct);

            await Task.Delay(TimeSpan.FromMilliseconds(800), ct);   // let the PLC scan complete
            latched = await _channel.ReadDWordAsync(DeltaRegisters.PlcUnit,
                DeltaRegisters.D120_HomeLatch, "read home latch", ChannelPriority.Move, ct);

            if (latched == LatchSentinel)
                throw Mechanical("the home sensor was detected but the drive never latched the "
                    + "position — check the home-latch network in the PLC program");
        }
        finally
        {
            await SetCoilAsync(DeltaRegisters.M6_ArmLatch, false, ChannelPriority.Stop, CancellationToken.None);
        }

        lock (_sync)
        {
            _zeroOffset = latched;
            _homed = true;
        }

        await PersistAsync(ct);
        _logger?.LogInformation("{Axis}: homed — zero latched at raw count {Zero}", _cfg.Name, latched);

        bool LatchFiredNoWait() =>
            _channel.ReadDWordAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D120_HomeLatch, "poll latch",
                    ChannelPriority.Move, ct)
                .GetAwaiter().GetResult() != LatchSentinel;
    }

    // ═══════════════════════ the move loop ═══════════════════════

    /// <summary>
    /// Drives to <paramref name="destination"/>, an angle in the axis's <b>unwrapped</b> space: the
    /// wrap and the <see cref="RotationSense"/> were resolved once at command time, so this loop
    /// never re-decides which way round to go and a relative move of +720° really turns twice.
    /// </summary>
    private async Task MoveCoreAsync(double destination, Frequency<double> hz, CancellationToken ct)
    {
        var tol = (double)_cfg.Tolerance;

        if (_cfg.LimitInputs is not null)
        {
            var inputs = await ReadInputsAsync(ct);
            if (LimitHit(inputs, null) is { } tripped) await ReleaseLimitAsync(tripped, ct);
        }

        await SetCoilAsync(DeltaRegisters.M0_Run, true, ChannelPriority.Move, ct);

        var degPerHz = _cfg.TheoreticalDegPerSecondPerHz;
        var decel = 50.0 / (JogRampRaw / 100.0) * degPerHz;

        // The threshold at which micro-pulses take over. With the continuous approach it MUST equal
        // the handover distance, or a band opens up where neither phase advances the axis and the
        // move fails without ever moving. BENCH-TUNED — never against the simulator.
        var pulseZone = Math.Max(3.0 * tol, 0.25);
        if (_cfg.SmoothApproach) pulseZone = Math.Max(pulseZone, (double)_cfg.SmoothHandover);

        var deadline = DateTime.UtcNow + MoveTimeout;

        for (var step = 0; step < 20; step++)
        {
            ct.ThrowIfCancellationRequested();
            if (DateTime.UtcNow > deadline)
                throw Mechanical("positioning timed out");

            var remainingDeg = destination - UnwrappedAngle(await ReadPositionAsync(ct));
            var remaining = Math.Abs(remainingDeg);
            if (remaining <= tol) return;

            var direction = ToCount(remainingDeg > 0);

            if (remaining <= pulseZone)
            {
                await PulseAsync(direction, _cfg.Pulse.SecondsFor(remaining), ct);
                continue;
            }

            if (_cfg.SmoothApproach)
            {
                await ApproachSmoothAsync(destination, pulseZone, decel, hz, deadline, ct);
                continue;
            }

            var fast = step == 0 ? hz : Frequency<double>.FromHertz(Math.Min(hz.Hertz, 8.0));
            Frequency<double> stepHz;
            double lead;
            if (remaining > LeadFor(fast.Hertz) + 2.0)
            {
                stepHz = fast;
                lead = LeadFor(fast.Hertz);
            }
            else
            {
                stepHz = Frequency<double>.FromHertz(Math.Max(2.0, _cfg.MinJogHz.Hertz));
                lead = Math.Min(LeadFor(stepHz.Hertz), Math.Max(pulseZone, remaining * 0.5));
            }

            try
            {
                await ApproachStagedAsync(destination, direction, stepHz, lead, deadline, ct);
            }
            catch (MotionException ex) when (ex.Error == MotionError.DriveFault && ex.Message.Contains("not moving"))
            {
                // Cold gearbox or resting against a limit: break it free with a short, faster pulse
                // and let the normal approach continue.
                var kick = Frequency<double>.FromHertz(Math.Min(hz.Hertz, Math.Max(stepHz.Hertz * 2, 10.0)));
                await JogForAsync(direction, kick, TimeSpan.FromMilliseconds(800), ct);
            }
        }

        var finalError = Math.Abs(destination - UnwrappedAngle(await ReadPositionAsync(ct)));
        if (finalError > tol)
            throw Mechanical($"stopped {finalError:0.##}° from the target");

        double LeadFor(double atHz)
        {
            var v = atHz * degPerHz;
            return v * v / (2 * decel) + 0.35 * v + 0.2;
        }
    }

    /// <summary>
    /// One step of the staged approach: run at a fixed speed until only <paramref name="leadDeg"/>
    /// remains, then stop and let the ramp run out.
    /// </summary>
    private async Task ApproachStagedAsync(double destination, CountDirection direction,
        Frequency<double> hz, double leadDeg, DateTime deadline, CancellationToken ct)
    {
        await JogStartAsync(hz, direction, JogRampRaw, ct);
        var remaining = destination - UnwrappedAngle(await ReadPositionAsync(ct));
        var sign0 = remaining >= 0 ? 1 : -1;
        var stallRef = remaining;
        var stallAt = DateTime.UtcNow;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(ApproachPoll, ct);

                remaining = destination - UnwrappedAngle(await ReadPositionAsync(ct));
                var inputs = await ReadInputsAsync(ct);
                if (LimitHit(inputs, direction) is { } hit)
                    throw new MotionException(MotionError.LimitTripped,
                        $"{_cfg.Name}: {hit} travel limit tripped", _cfg.Name);

                if (Math.Abs(remaining - stallRef) * _cfg.CountsPerDegree > 30)
                {
                    stallRef = remaining;
                    stallAt = DateTime.UtcNow;
                }
                else if (DateTime.UtcNow - stallAt > StallTimeout)
                {
                    throw Mechanical($"not moving at {hz.Hertz:0.##} Hz");
                }

                if ((remaining >= 0 ? 1 : -1) != sign0) break;   // crossed the target
                if (Math.Abs(remaining) <= leadDeg) break;       // close enough to coast in
            }
        }
        finally
        {
            await JogStopAsync(ChannelPriority.Move, CancellationToken.None);
        }
    }

    /// <summary>
    /// Continuous approach: the commanded frequency falls with the distance still to run
    /// (<c>v = sqrt(2·a·s)</c>), so the axis decelerates INTO the handover point instead of running
    /// at a fixed speed and guessing where to brake.
    ///
    /// <para>
    /// This is possible because the PLC tracks the frequency register live — no stop/start edge is
    /// needed to change speed. The braking plan is deliberately weaker than the drive's real
    /// capability so the ramp always keeps up.
    /// </para>
    ///
    /// <para>
    /// It deliberately stops short of the target. Handing over to micro-pulses matters: a pulse
    /// starts from rest, so it cannot carry the axis past the target the way an arriving continuous
    /// motion does.
    /// </para>
    /// </summary>
    private async Task ApproachSmoothAsync(double destination, double handoverDeg, double decel,
        Frequency<double> hz, DateTime deadline, CancellationToken ct)
    {
        var degPerHz = _cfg.TheoreticalDegPerSecondPerHz;
        var plan = decel * _cfg.SmoothDecelerationFraction;
        var floorHz = Math.Max(_cfg.MinJogHz.Hertz, 2.0);
        CountDirection? current = null;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                var remainingDeg = destination - UnwrappedAngle(await ReadPositionAsync(ct));
                var inputs = await ReadInputsAsync(ct);
                if (LimitHit(inputs, current) is { } hit)
                    throw new MotionException(MotionError.LimitTripped,
                        $"{_cfg.Name}: {hit} travel limit tripped", _cfg.Name);

                var remaining = Math.Abs(remainingDeg);
                if (remaining <= handoverDeg) return;

                var direction = ToCount(remainingDeg > 0);
                var runway = Math.Max(0.0, remaining - handoverDeg);
                var next = Frequency<double>.FromHertz(
                    Math.Min(hz.Hertz, Math.Max(floorHz, Math.Sqrt(2.0 * plan * runway) / degPerHz)));

                if (direction != current)
                {
                    if (current is not null) await JogStopAsync(ChannelPriority.Move, ct);   // reverse only from a standstill
                    await JogStartAsync(next, direction, JogRampRaw, ct);
                    current = direction;
                }
                else
                {
                    await SetFrequencyAsync(next, ct);
                }

                await Task.Delay(SmoothPoll, ct);
            }

            throw Mechanical("positioning timed out");
        }
        finally
        {
            await JogStopAsync(ChannelPriority.Move, CancellationToken.None);
        }
    }

    // ═══════════════════════ elementary motion ═══════════════════════

    /// <summary>
    /// Direction expressed as the way the RAW COUNT moves. Kept distinct from the direction the
    /// reported ANGLE moves: on an axis with <see cref="DeltaAxisConfig.InvertAngle"/> the two are
    /// opposites, and conflating them makes a positive velocity turn the machine the wrong way.
    /// </summary>
    private enum CountDirection
    {
        Up,
        Down,
    }

    /// <summary>The count direction that increases (or decreases) the reported angle.</summary>
    private CountDirection ToCount(bool angleIncreases) =>
        angleIncreases != _cfg.InvertAngle ? CountDirection.Up : CountDirection.Down;

    /// <summary>The sign the reported angle moves in when the count moves this way.</summary>
    private int AngleSignOf(CountDirection direction) =>
        (direction == CountDirection.Up) != _cfg.InvertAngle ? 1 : -1;

    private bool DirectionBit(CountDirection direction) =>
        (direction == CountDirection.Down) != _cfg.InvertDirection;

    private async Task JogStartAsync(Frequency<double> hz, CountDirection direction, ushort ramp,
        CancellationToken ct)
    {
        var commanded = Math.Max(hz.Hertz, _cfg.MinJogHz.Hertz);
        if (commanded > _cfg.MaxJogHz.Hertz)
            throw new MotionException(MotionError.UnreachableSpeed,
                $"{_cfg.Name}: {commanded:0.##} Hz is above the {_cfg.MaxJogHz.Hertz:0.##} Hz jog guard",
                _cfg.Name);

        await _channel.WriteRegistersAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D111_Ramp,
            [ramp, ramp], "set ramps", ChannelPriority.Move, ct);
        await _channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency,
            (ushort)Math.Round(commanded * 100), "set frequency", ChannelPriority.Move, ct);
        await SetCoilAsync(DeltaRegisters.M5_Direction, DirectionBit(direction), ChannelPriority.Move, ct);
        await SetCoilAsync(DeltaRegisters.M4_Move, true, ChannelPriority.Move, ct);
    }

    /// <summary>Changes speed WITHOUT interrupting motion — the PLC tracks the register live.</summary>
    private Task SetFrequencyAsync(Frequency<double> hz, CancellationToken ct)
    {
        var commanded = Math.Clamp(hz.Hertz, _cfg.MinJogHz.Hertz, _cfg.MaxJogHz.Hertz);
        return _channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency,
            (ushort)Math.Round(commanded * 100), "set frequency", ChannelPriority.Move, ct);
    }

    /// <summary>
    /// Ramps down, waits for the drive to actually stop, then drops the motion coil. The coil must
    /// only fall on a stationary axis — the PLC switches mode on that edge.
    ///
    /// <para>
    /// The <b>first</b> write is the frequency going to zero, and on the stop lane it preempts queued
    /// move traffic. That write is what AC-23 measures NFR-5's 200 ms against.
    /// </para>
    /// </summary>
    private async Task JogStopAsync(ChannelPriority priority, CancellationToken ct)
    {
        await _channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency,
            0, "ramp down", priority, ct);

        var deadline = DateTime.UtcNow + JogStopTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(JogStopPoll, ct);
            if ((await ReadOutputHzAsync(ct)).Hertz <= 0.2) break;
        }

        await SetCoilAsync(DeltaRegisters.M4_Move, false, priority, ct);
    }

    private async Task JogForAsync(CountDirection direction, Frequency<double> hz, TimeSpan duration,
        CancellationToken ct)
    {
        await JogStartAsync(hz, direction, JogRampRaw, ct);
        await Task.Delay(duration, ct);
        await JogStopAsync(ChannelPriority.Move, ct);
    }

    private async Task JogUntilAsync(CountDirection direction, Frequency<double> hz, Func<bool[], bool> until,
        string what, TimeSpan timeout, CancellationToken ct)
    {
        await JogStartAsync(hz, direction, JogRampRaw, ct);
        var deadline = DateTime.UtcNow + timeout;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(ApproachPoll, ct);

                var inputs = await ReadInputsAsync(ct);
                if (LimitHit(inputs, direction) is { } hit)
                    throw new MotionException(MotionError.LimitTripped,
                        $"{_cfg.Name}: {hit} travel limit tripped", _cfg.Name);
                if (until(inputs)) return;
            }

            throw Mechanical($"timed out {what}");
        }
        finally
        {
            await JogStopAsync(ChannelPriority.Move, CancellationToken.None);
        }
    }

    /// <summary>
    /// Timed micro-pulse — the only way to move less than the axis's smallest continuous step. The
    /// drive is cut at the end of the pulse rather than ramped, so the axis stops in milliseconds.
    /// </summary>
    private async Task PulseAsync(CountDirection direction, double seconds, CancellationToken ct)
    {
        await _channel.WriteRegistersAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D111_Ramp,
            [NudgeRampRaw, NudgeRampRaw], "set pulse ramps", ChannelPriority.Move, ct);
        await _channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency,
            (ushort)Math.Round(_cfg.NudgeHz.Hertz * 100), "set pulse frequency", ChannelPriority.Move, ct);
        await SetCoilAsync(DeltaRegisters.M5_Direction, DirectionBit(direction), ChannelPriority.Move, ct);
        await SetCoilAsync(DeltaRegisters.M4_Move, true, ChannelPriority.Move, ct);

        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);

        await _channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency,
            0, "end pulse", ChannelPriority.Move, ct);
        await SetCoilAsync(DeltaRegisters.M4_Move, false, ChannelPriority.Move, ct);
        await _channel.WriteRegistersAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D111_Ramp,
            [JogRampRaw, JogRampRaw], "restore ramps", ChannelPriority.Move, ct);

        await WaitSettledAsync(ct);
    }

    /// <summary>
    /// Waits for the position count to stop changing, rather than sleeping a fixed time. Pays for
    /// the standstill that actually happened, and unlike a fixed delay it verifies the axis really
    /// stopped instead of assuming it.
    /// </summary>
    private async Task WaitSettledAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(1500);
        long? last = null;
        var stable = 0;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
            long pulses;
            try
            {
                pulses = await ReadPositionAsync(ct);
            }
            catch (MotionException)
            {
                return;   // a read problem must not stall the move; the caller will see it next loop
            }

            if (last == pulses)
            {
                if (++stable >= 2) return;
            }
            else
            {
                stable = 0;
            }

            last = pulses;
        }
    }

    private async Task HardStopAsync(CancellationToken ct)
    {
        try
        {
            await _channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency, 0,
                "hard stop", ChannelPriority.Stop, ct);
            await SetCoilAsync(DeltaRegisters.M4_Move, false, ChannelPriority.Stop, ct);
            await SetCoilAsync(DeltaRegisters.M0_Run, false, ChannelPriority.Stop, ct);
        }
        catch (MotionException ex)
        {
            _logger?.LogDebug(ex, "{Axis}: hard stop could not be delivered", _cfg.Name);
        }
    }

    // ═══════════════════════ limits ═══════════════════════

    private CountDirection AwayFrom(LimitSwitchState side) =>
        // Retreating from the lower limit means the ANGLE must increase, and vice versa.
        ToCount(side == LimitSwitchState.Min);

    private LimitSwitchState LimitsOf(bool[] inputs)
    {
        if (_cfg.LimitInputs is not { } lim) return LimitSwitchState.None;

        // Normally closed: 0 means tripped. Min and Max asserted together is a wiring fault and is
        // reported as such rather than masked (LimitSwitchState's whole point).
        var state = LimitSwitchState.None;
        if (!inputs[lim.Min]) state |= LimitSwitchState.Min;
        if (!inputs[lim.Max]) state |= LimitSwitchState.Max;
        return state;
    }

    private LimitSwitchState? LimitHit(bool[] inputs, CountDirection? moving)
    {
        var state = LimitsOf(inputs);
        foreach (var side in new[] { LimitSwitchState.Min, LimitSwitchState.Max })
        {
            if (!state.HasFlag(side)) continue;
            if (moving == AwayFrom(side)) continue;   // retreating from it is allowed
            return side;
        }

        return null;
    }

    /// <summary>
    /// Jogs off a tripped limit.
    ///
    /// <para>
    /// In speed mode the drive's own limit function faults the drive, so the function is lifted from
    /// the input for the duration of the retreat while this code keeps watching the input itself.
    /// It is restored in every exit path — and because a killed process cannot run that path, the
    /// functions are also re-asserted at startup and, since FR-11, by the drive's own watchdog on a
    /// trip. That bounds the unprotected window at the 1 s stall instead of "until somebody notices"
    /// (risk R-2).
    /// </para>
    /// </summary>
    private async Task ReleaseLimitAsync(LimitSwitchState side, CancellationToken ct)
    {
        var parameter = side == LimitSwitchState.Min ? DeltaRegisters.Pr0204_Mi4 : DeltaRegisters.Pr0205_Mi5;
        var function = side == LimitSwitchState.Min ? DeltaRegisters.Mi4LimitFunction : DeltaRegisters.Mi5LimitFunction;
        var input = side == LimitSwitchState.Min ? _cfg.LimitInputs!.Value.Min : _cfg.LimitInputs!.Value.Max;
        var away = AwayFrom(side);

        try
        {
            await HardStopAsync(ct);
            await Task.Delay(TimeSpan.FromMilliseconds(1500), ct);
            await ResetDriveFaultAsync(ct);
            await _channel.WriteRegisterAsync(DeltaRegisters.DriveUnit, parameter, 0,
                "lift limit function", ChannelPriority.Move, ct);
            await SetCoilAsync(DeltaRegisters.M0_Run, true, ChannelPriority.Move, ct);

            await JogUntilAsync(away, _cfg.SeekHz, x => x[input], $"retreating from the {side} limit",
                TimeSpan.FromSeconds(60), ct);
            await JogForAsync(away, _cfg.SeekHz, TimeSpan.FromSeconds(1), ct);
        }
        finally
        {
            await HardStopAsync(CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(1500), CancellationToken.None);
            try
            {
                await _channel.WriteRegisterAsync(DeltaRegisters.DriveUnit, parameter, function,
                    "restore limit function", ChannelPriority.Move, CancellationToken.None);
                await ResetDriveFaultAsync(CancellationToken.None);
                await SetCoilAsync(DeltaRegisters.M0_Run, true, ChannelPriority.Move, CancellationToken.None);
            }
            catch (MotionException ex)
            {
                _logger?.LogError(ex,
                    "{Axis}: FAILED to restore the {Side} limit function — the drive is running "
                    + "without hardware limit protection until it is restored", _cfg.Name, side);
            }
        }
    }

    // ═══════════════════════ angle maths ═══════════════════════

    private int Sign => _cfg.InvertAngle ? -1 : 1;

    private static double Mod(double value, double modulus) => ((value % modulus) + modulus) % modulus;

    /// <summary>
    /// The axis angle in <b>unwrapped</b> space: monotonic in the raw count, so a wrapping axis can
    /// be commanded past 360° and a relative move stays a relative move.
    /// </summary>
    private double UnwrappedAngle(long pulses)
    {
        long zero;
        lock (_sync) zero = _zeroOffset;
        return (pulses - zero) * Sign / _cfg.CountsPerDegree;
    }

    /// <summary>The reported angle: wrapped into [0°, 360°) on a continuous axis, as-is otherwise.</summary>
    public Degree<double> PulsesToDegrees(long pulses)
    {
        var raw = UnwrappedAngle(pulses);
        return Degree<double>.Create(Math.Round(_cfg.Continuous ? Mod(raw, 360.0) : raw, 2));
    }

    /// <summary>
    /// How far to travel, in signed degrees, to reach <paramref name="target"/> from
    /// <paramref name="currentUnwrapped"/> on a wrapping axis, given the requested sense.
    /// The target is normalised into the wrap domain first — that is what
    /// <see cref="AxisCapabilities.ContinuousRotation"/> means.
    /// </summary>
    private static double WrappedTravel(double currentUnwrapped, double target, RotationSense sense)
    {
        var normalised = Mod(target, 360.0);
        var forward = Mod(normalised - Mod(currentUnwrapped, 360.0), 360.0);

        return sense switch
        {
            RotationSense.Positive => forward,
            RotationSense.Negative => forward == 0.0 ? 0.0 : forward - 360.0,
            _ => forward <= 180.0 ? forward : forward - 360.0,
        };
    }

    private Task PersistAsync(CancellationToken ct)
    {
        long zero;
        bool homed;
        lock (_sync)
        {
            zero = _zeroOffset;
            homed = _homed;
        }

        return _store.SaveAsync(_cfg.Name,
            new AxisPersistedState(zero, homed, _cfg.SpeedCalibration.ToDegPerSecond(_moveHz.Hertz)), ct);
    }

    // ═══════════════════════ raw I/O ═══════════════════════

    private async Task<long> ReadPositionAsync(CancellationToken ct) =>
        await _channel.ReadDWordAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D1051_Position,
            "read position", ChannelPriority.Move, ct);

    private Task<bool[]> ReadInputsAsync(CancellationToken ct) =>
        _channel.ReadDiscreteInputsAsync(DeltaRegisters.PlcUnit, DeltaRegisters.X0_Inputs,
            DeltaRegisters.InputCount, "read inputs", ChannelPriority.Move, ct);

    private Task SetCoilAsync(ushort coil, bool value, ChannelPriority priority, CancellationToken ct) =>
        _channel.WriteCoilAsync(DeltaRegisters.PlcUnit, coil, value, "write coil", priority, ct);

    private async Task<Frequency<double>> ReadOutputHzAsync(CancellationToken ct)
    {
        var regs = await _channel.ReadHoldingAsync(DeltaRegisters.DriveUnit,
            DeltaRegisters.OutputFrequency, 1, "read output frequency", ChannelPriority.Move, ct);
        return Frequency<double>.FromHertz(regs[0] / 100.0);
    }

    private async Task<int> ReadFaultAsync(CancellationToken ct)
    {
        var regs = await _channel.ReadHoldingAsync(DeltaRegisters.DriveUnit,
            DeltaRegisters.FaultCode, 1, "read fault", ChannelPriority.Move, ct);
        return regs[0] & 0xFF;
    }

    private async Task ResetDriveFaultAsync(CancellationToken ct)
    {
        await _channel.WriteRegisterAsync(DeltaRegisters.DriveUnit, DeltaRegisters.CommandWord, 0,
            "clear command", ChannelPriority.Stop, ct);
        await Task.Delay(TimeSpan.FromMilliseconds(400), ct);
        await _channel.WriteRegisterAsync(DeltaRegisters.DriveUnit, DeltaRegisters.CommandWord, 2,
            "reset fault", ChannelPriority.Stop, ct);
        await Task.Delay(TimeSpan.FromMilliseconds(1200), ct);
    }

    /// <summary>The last recorded drive-setup problem, or <see langword="null"/>.</summary>
    internal string? SetupError => _setupError;

    /// <inheritdoc/>
    public void Dispose()
    {
        CancellationTokenSource? abort;
        lock (_sync)
        {
            abort = _abort;
            _abort = null;
        }

        try { abort?.Cancel(); } catch (ObjectDisposedException) { /* already gone */ }
        abort?.Dispose();
    }
}
