using Microsoft.Extensions.Logging;
using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Devices.Positioner.Delta;

/// <summary>
/// One axis of a Delta VFD-C2000 positioner.
///
/// <para>
/// The drive runs in SPEED mode and this class derives position itself, because the encoders sit
/// behind the gearboxes and the drive's own positioning modes are unusable there. Everything below
/// — homing, the staged or continuous approach, the micro-pulse endgame — exists for that reason.
/// </para>
/// </summary>
public sealed class DeltaAxis : IPositionerAxis, IDisposable
{
    private const double MaxJogHz = 60.0;
    private const ushort JogRampRaw = 200;      // 2.00 s per 50 Hz
    private const ushort NudgeRampRaw = 20;     // 0.20 s per 50 Hz
    private static readonly TimeSpan JogStopTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MoveTimeout = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ApproachPoll = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan SmoothPoll = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan JogStopPoll = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Written into the latch register before arming the home latch. The PLC overwrites it with the
    /// captured position, so an unchanged value proves the ladder did not run.
    ///
    /// <para>
    /// This is the only reliable test. Comparing against the PREVIOUS latch value cannot work: the
    /// axis returns to the same cam edge every time and the encoder is not reset between runs, so a
    /// correctly working latch stores the same number again — and the better the machine's
    /// repeatability, the more often that happens.
    /// </para>
    ///
    /// <para>Chosen well outside encoder range but small enough that the ladder's DSUB against it
    /// cannot overflow a signed 32-bit result.</para>
    /// </summary>
    private const int LatchSentinel = 1_000_000_000;

    private readonly DeltaAxisConfig _cfg;
    private readonly ModbusChannel _channel;
    private readonly IAxisStateStore _store;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private long _zeroOffset;
    private volatile bool _homed;
    private double _moveHz;
    private volatile string? _lastError;
    private volatile string? _setupError;
    private Degree<double>? _target;
    private PositionerOperation? _operation;
    private CancellationTokenSource? _abort;
    private PositionerAxisStatus _status;

    internal DeltaAxis(DeltaAxisConfig cfg, ModbusChannel channel, IAxisStateStore store, ILogger? logger)
    {
        _cfg = cfg;
        _channel = channel;
        _store = store;
        _logger = logger;
        _moveHz = cfg.MoveHz;
        _status = PositionerAxisStatus.Offline(cfg.Name, "not read yet");
    }

    /// <summary>Configuration this axis was built from.</summary>
    public DeltaAxisConfig Config => _cfg;

    /// <inheritdoc/>
    public string Name => _cfg.Name;

    /// <inheritdoc/>
    public string DisplayName => _cfg.DisplayName;

    /// <inheritdoc/>
    public Degree<double> Min => _cfg.Min;

    /// <inheritdoc/>
    public Degree<double> Max => _cfg.Max;

    /// <inheritdoc/>
    public bool IsContinuous => _cfg.Continuous;

    /// <inheritdoc/>
    public bool RequiresHoming => _cfg.RequiresHoming;

    /// <inheritdoc/>
    public double MinSpeedDegPerSecond => _cfg.SpeedCalibration.ToDegPerSecond(_cfg.MinJogHz);

    /// <inheritdoc/>
    public double MaxSpeedDegPerSecond => _cfg.SpeedCalibration.ToDegPerSecond(_cfg.MaxMoveHz);

    /// <inheritdoc/>
    public Degree<double> Tolerance => _cfg.Tolerance;

    /// <inheritdoc/>
    public PositionerAxisStatus Status => _status;

    /// <inheritdoc/>
    public event EventHandler<PositionerAxisStatus>? StatusChanged;

    // ═══════════════════════ lifecycle ═══════════════════════

    internal async Task InitialiseAsync(CancellationToken ct)
    {
        if (await _store.LoadAsync(_cfg.Name, ct) is { } saved)
        {
            _zeroOffset = saved.ZeroOffset;
            _homed = saved.Homed;
            _moveHz = _cfg.SpeedCalibration.ToHz(saved.SpeedDegPerSecond);
        }

        await ApplyDriveSetupAsync(ct);
    }

    /// <summary>
    /// Forces the drive parameters this controller depends on, so behaviour does not depend on what
    /// was last typed into the keypad. Writes only what differs — parameter writes need a stopped
    /// drive, so this runs before any motion.
    /// </summary>
    private async Task ApplyDriveSetupAsync(CancellationToken ct)
    {
        try
        {
            await SetCoilAsync(DeltaRegisters.M4_Move, false, ct);
            await SetCoilAsync(DeltaRegisters.M0_Run, false, ct);

            var required = _cfg.LimitInputs is null
                ? DeltaRegisters.RequiredSetup
                : [.. DeltaRegisters.RequiredSetup, .. DeltaRegisters.LimitSetup];

            foreach (var (address, value, why) in required)
            {
                var current = await _channel.ReadHoldingAsync(DeltaRegisters.DriveUnit, address, 1, why, ct);
                if (current[0] == value) continue;
                _logger?.LogInformation("{Axis}: {Why} (was {Current}, setting {Value})",
                    _cfg.Name, why, current[0], value);
                await _channel.WriteRegisterAsync(DeltaRegisters.DriveUnit, address, value, why, ct);
            }

            _setupError = null;
        }
        catch (Exception ex)
        {
            // Recorded but NOT merged into the operation error: a startup problem must not make
            // every later successful move look failed.
            _setupError = ex.Message;
            _logger?.LogWarning(ex, "{Axis}: drive setup failed", _cfg.Name);
        }
    }

    // ═══════════════════════ status ═══════════════════════

    /// <inheritdoc/>
    public async Task<PositionerAxisStatus> ReadStatusAsync(CancellationToken ct = default)
    {
        PositionerAxisStatus status;
        try
        {
            var pulses = await ReadPositionAsync(ct);
            var coils = await _channel.ReadCoilsAsync(DeltaRegisters.PlcUnit, DeltaRegisters.M0_Run, 6, "coils", ct);
            var inputs = await ReadInputsAsync(ct);
            var outHz = await ReadOutputHzAsync(ct);
            var fault = await ReadFaultAsync(ct);

            var busy = _operationGate.CurrentCount == 0;
            var moving = outHz > 0.2;
            var limits = _cfg.LimitInputs is { } lim
                ? new LimitSwitchState(!inputs[lim.Min], !inputs[lim.Max])
                : (LimitSwitchState?)null;

            status = new PositionerAxisStatus(
                Axis: _cfg.Name,
                Connected: true,
                Busy: busy,
                Operation: busy ? _operation : null,
                Ready: !busy && _lastError is null && fault == 0 && (_homed || !_cfg.RequiresHoming),
                Homed: _homed,
                Angle: PulsesToDegrees(pulses),
                Target: _target,
                Moving: moving,
                Direction: moving
                    ? ToRotation(coils[5] != _cfg.InvertDirection ? CountDirection.Down : CountDirection.Up)
                    : null,
                ServoOn: coils[0],
                SpeedDegPerSecond: _cfg.SpeedCalibration.ToDegPerSecond(_moveHz),
                ActualSpeedDegPerSecond: _cfg.SpeedCalibration.ToDegPerSecond(outHz),
                DriveFault: fault,
                Limits: limits,
                HomeSensor: !inputs[_cfg.HomeSensorInput],
                Error: _lastError,
                RawPosition: pulses);
        }
        catch (PositionerException ex)
        {
            status = PositionerAxisStatus.Offline(_cfg.Name, ex.Message);
        }

        _status = status;
        StatusChanged?.Invoke(this, status);
        return status;
    }

    // ═══════════════════════ public motion ═══════════════════════

    /// <inheritdoc/>
    public Task HomeAsync(CancellationToken ct = default) =>
        RunOperationAsync(PositionerOperation.Home, HomeCoreAsync, ct);

    /// <inheritdoc/>
    public Task MoveToAsync(Degree<double> target, CancellationToken ct = default)
    {
        var lo = (double)_cfg.Min;
        var hi = (double)_cfg.Max;
        var value = (double)target;
        if (value < lo || value > hi)
            throw new ArgumentOutOfRangeException(nameof(target), value,
                $"{_cfg.Name}: {value}° is outside {lo}–{hi}°");

        return RunOperationAsync(PositionerOperation.Move, token => MoveCoreAsync(target, token), ct);
    }

    /// <inheritdoc/>
    public async Task RotateAsync(double speedDegPerSecond, RotationDirection direction, CancellationToken ct = default)
    {
        if (!_cfg.Continuous)
            throw new NotSupportedException($"{_cfg.Name}: axis does not support continuous rotation");

        var hz = RequireHz(speedDegPerSecond);

        // Continuous rotation is not tracked as an operation the way a move is, so take the gate to
        // keep it mutually exclusive with move/home — otherwise a positioning command could be
        // accepted onto a spinning axis.
        if (!await _operationGate.WaitAsync(TimeSpan.Zero, ct))
            throw new PositionerException(PositionerError.Busy, $"{_cfg.Name}: another operation is running")
                { Axis = _cfg.Name };
        try
        {
            _lastError = null;
            _target = null;
            _operation = PositionerOperation.Rotate;
            await SetCoilAsync(DeltaRegisters.M0_Run, true, ct);
            await JogStartAsync(hz, ToCount(direction), JogRampRaw, ct);
        }
        finally
        {
            // Released immediately: rotation runs until StopAsync, and holding the gate would block
            // the stop that ends it.
            _operationGate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct = default)
    {
        // Cancel first so a running approach stops commanding new motion, THEN ramp down. The other
        // order lets the control loop issue another step while the axis is still decelerating.
        _abort?.Cancel();
        _operation = null;
        await JogStopAsync(ct);
    }

    /// <inheritdoc/>
    public Task SetServoAsync(bool on, CancellationToken ct = default) =>
        SetCoilAsync(DeltaRegisters.M0_Run, on, ct);

    /// <inheritdoc/>
    public async Task SetSpeedAsync(double degPerSecond, CancellationToken ct = default)
    {
        _moveHz = RequireHz(degPerSecond, forPositioning: true);
        await PersistAsync(ct);
    }

    /// <inheritdoc/>
    public async Task ResetFaultAsync(CancellationToken ct = default)
    {
        await HardStopAsync(ct);
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
        await ResetDriveFaultAsync(ct);
        await SetCoilAsync(DeltaRegisters.M0_Run, true, ct);
        _lastError = null;
    }

    /// <inheritdoc/>
    public async Task<bool> VerifyDirectionAsync(CancellationToken ct = default)
    {
        if (!await _operationGate.WaitAsync(TimeSpan.Zero, ct))
            throw new PositionerException(PositionerError.Busy, $"{_cfg.Name}: another operation is running")
                { Axis = _cfg.Name };
        try
        {
            // The jog below runs open-loop for a fixed time and does NOT watch the limits, so on an
            // axis that has them, refuse to start from a tripped one — the check would otherwise
            // drive further into it and leave the drive faulted.
            if (LimitHit(await ReadInputsAsync(ct), null) is { } tripped)
                throw new PositionerException(PositionerError.LimitTripped,
                    $"{_cfg.Name}: cannot check direction while resting on the {tripped} limit — "
                    + "move the axis clear first") { Axis = _cfg.Name };

            await SetCoilAsync(DeltaRegisters.M0_Run, true, ct);
            var before = await ReadPositionAsync(ct);
            await JogForAsync(CountDirection.Up, Math.Max(_cfg.MinJogHz, 5.0),
                TimeSpan.FromSeconds(1.5), ct);
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            var after = await ReadPositionAsync(ct);

            var moved = after - before;
            _logger?.LogInformation(
                "{Axis}: direction check — forward moved the count by {Moved} (expected positive)",
                _cfg.Name, moved);

            if (Math.Abs(moved) < 20)
                throw new PositionerException(PositionerError.Stalled,
                    $"{_cfg.Name}: axis did not move during the direction check") { Axis = _cfg.Name };

            return moved > 0;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    // ═══════════════════════ operation plumbing ═══════════════════════

    /// <summary>
    /// Runs one exclusive operation. Uses a real mutual exclusion rather than "is the previous task
    /// still alive?", so two concurrent commands cannot both start.
    /// </summary>
    private async Task RunOperationAsync(PositionerOperation operation, Func<CancellationToken, Task> body, CancellationToken ct)
    {
        if (!await _operationGate.WaitAsync(TimeSpan.Zero, ct))
            throw new PositionerException(PositionerError.Busy, $"{_cfg.Name}: another operation is running")
                { Axis = _cfg.Name };

        var abort = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _abort = abort;
        _operation = operation;
        _lastError = null;
        try
        {
            await body(abort.Token);
        }
        catch (OperationCanceledException) when (abort.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _lastError = "motion stopped by command";
            throw new PositionerException(PositionerError.Aborted, _lastError) { Axis = _cfg.Name };
        }
        catch (PositionerException ex)
        {
            _lastError = ex.Message;
            throw;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            throw new PositionerException(PositionerError.Unknown, $"{_cfg.Name}: {ex.Message}", ex)
                { Axis = _cfg.Name };
        }
        finally
        {
            _operation = null;
            _abort = null;
            abort.Dispose();
            _operationGate.Release();
        }
    }

    private double RequireHz(double degPerSecond, bool forPositioning = false)
    {
        var cal = _cfg.SpeedCalibration;
        var hz = cal.ToHz(degPerSecond);
        var maxHz = forPositioning ? _cfg.MaxMoveHz : MaxJogHz;

        // Rejected rather than silently clamped: a caller must be able to learn that the speed it
        // asked for was not the speed it got.
        if (hz < _cfg.MinJogHz || hz > maxHz)
            throw new ArgumentOutOfRangeException(nameof(degPerSecond), degPerSecond,
                $"{_cfg.Name}: speed must be between {cal.ToDegPerSecond(_cfg.MinJogHz):0.###} and "
                + $"{cal.ToDegPerSecond(maxHz):0.###} °/s ({_cfg.MinJogHz}–{maxHz} Hz)");
        return hz;
    }

    private Task PersistAsync(CancellationToken ct) => _store.SaveAsync(_cfg.Name,
        new AxisPersistedState(_zeroOffset, _homed, _cfg.SpeedCalibration.ToDegPerSecond(_moveHz)), ct);

    // ═══════════════════════ homing ═══════════════════════

    /// <summary>
    /// Software homing. The zero MUST always be captured on the same edge, because the sensor cam
    /// has width: (1) find the cam from wherever we are, (2) drive fully off it the other way,
    /// (3) creep back on — that edge is the zero, latched by the PLC.
    /// </summary>
    private async Task HomeCoreAsync(CancellationToken ct)
    {
        var org = _cfg.HomeSensorInput;
        var approach = CountDirection.Up;
        var back = CountDirection.Down;
        var seek = _cfg.SeekHz;
        var fine = Math.Max(_cfg.MinJogHz, seek / 3.0);

        await SetCoilAsync(DeltaRegisters.M0_Run, true, ct);
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
            catch (PositionerException ex) when (ex.Code == PositionerError.LimitTripped)
            {
                inputs = await ReadInputsAsync(ct);
                if (LimitHit(inputs, null) is { } hit) await ReleaseLimitAsync(hit, ct);
                await JogUntilAsync(back, seek, x => !x[org], "searching for the home sensor", MoveTimeout, ct);
            }
        }

        await JogUntilAsync(back, seek, x => x[org], "leaving the home sensor", MoveTimeout / 3, ct);
        await JogForAsync(back, seek, TimeSpan.FromSeconds(1.5), ct);   // clear of the whole edge

        // Sentinel BEFORE arming: the other order lets the latch fire and then be overwritten.
        await _channel.WriteDWordAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D120_HomeLatch,
            LatchSentinel, "arm home latch", ct);
        await SetCoilAsync(DeltaRegisters.M6_ArmLatch, true, ct);
        try
        {
            // Ends on SENSOR DETECTION as well as on the latch changing: the cam sits close to the
            // travel limit, and stopping late used to run the axis onto the limit instead.
            await JogUntilAsync(approach, fine,
                x => !x[org] || LatchFiredNoWait(),
                "creeping onto the home edge", MoveTimeout, ct);

            await Task.Delay(TimeSpan.FromMilliseconds(800), ct);   // let the PLC scan complete
            var latched = await _channel.ReadDWordAsync(DeltaRegisters.PlcUnit,
                DeltaRegisters.D120_HomeLatch, "read home latch", ct);

            if (latched == LatchSentinel)
                throw new PositionerException(PositionerError.HomeLatchFailed,
                    $"{_cfg.Name}: home sensor was detected but the drive never latched the position "
                    + "— check the home-latch network in the PLC program") { Axis = _cfg.Name };

            _zeroOffset = latched;
        }
        finally
        {
            await SetCoilAsync(DeltaRegisters.M6_ArmLatch, false, CancellationToken.None);
        }

        _homed = true;
        _target = null;
        await PersistAsync(ct);

        bool LatchFiredNoWait() =>
            _channel.ReadDWordAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D120_HomeLatch, "poll latch", ct)
                .GetAwaiter().GetResult() != LatchSentinel;
    }

    // ═══════════════════════ positioning ═══════════════════════

    private async Task MoveCoreAsync(Degree<double> target, CancellationToken ct)
    {
        if (_cfg.RequiresHoming && !_homed)
            throw new PositionerException(PositionerError.NotHomed,
                $"{_cfg.Name}: axis is not homed") { Axis = _cfg.Name };

        var tol = (double)_cfg.Tolerance;
        _target = target;

        if (_cfg.LimitInputs is not null)
        {
            var inputs = await ReadInputsAsync(ct);
            if (LimitHit(inputs, null) is { } tripped) await ReleaseLimitAsync(tripped, ct);
        }

        await SetCoilAsync(DeltaRegisters.M0_Run, true, ct);

        var degPerHz = _cfg.TheoreticalDegPerSecondPerHz;
        var decel = 50.0 / (JogRampRaw / 100.0) * degPerHz;

        // The threshold at which micro-pulses take over. With the continuous approach it MUST equal
        // the handover distance, or a band opens up where neither phase advances the axis and the
        // move fails without ever moving.
        var pulseZone = Math.Max(3.0 * tol, 0.25);
        if (_cfg.SmoothApproach) pulseZone = Math.Max(pulseZone, (double)_cfg.SmoothHandover);

        var deadline = DateTime.UtcNow + MoveTimeout;

        for (var step = 0; step < 20; step++)
        {
            ct.ThrowIfCancellationRequested();
            if (DateTime.UtcNow > deadline)
                throw new PositionerException(PositionerError.Timeout,
                    $"{_cfg.Name}: positioning timed out") { Axis = _cfg.Name };

            var delta = DeltaTo(target, await ReadPositionAsync(ct));
            var remaining = Math.Abs(delta) / _cfg.CountsPerDegree;
            if (remaining <= tol) return;

            var direction = delta > 0 ? CountDirection.Up : CountDirection.Down;

            if (remaining <= pulseZone)
            {
                await PulseAsync(direction, _cfg.Pulse.SecondsFor(remaining), ct);
                continue;
            }

            if (_cfg.SmoothApproach)
            {
                await ApproachSmoothAsync(target, pulseZone, decel, deadline, ct);
                continue;
            }

            var fast = step == 0 ? _moveHz : Math.Min(_moveHz, 8.0);
            double hz, lead;
            if (remaining > LeadFor(fast) + 2.0)
            {
                hz = fast;
                lead = LeadFor(fast);
            }
            else
            {
                hz = Math.Max(2.0, _cfg.MinJogHz);
                lead = Math.Min(LeadFor(hz), Math.Max(pulseZone, remaining * 0.5));
            }

            try
            {
                await ApproachStagedAsync(target, direction, hz, lead, deadline, ct);
            }
            catch (PositionerException ex) when (ex.Code == PositionerError.Stalled)
            {
                // Cold gearbox or resting against a limit: break it free with a short, faster pulse
                // and let the normal approach continue.
                await JogForAsync(direction, Math.Min(_moveHz, Math.Max(hz * 2, 10.0)),
                    TimeSpan.FromMilliseconds(800), ct);
            }
        }

        var finalDelta = DeltaTo(target, await ReadPositionAsync(ct));
        var finalError = Math.Abs(finalDelta) / _cfg.CountsPerDegree;
        if (finalError > tol)
            throw new PositionerException(PositionerError.PositionNotReached,
                $"{_cfg.Name}: stopped {finalError:0.##}° from target") { Axis = _cfg.Name };

        double LeadFor(double hz)
        {
            var v = hz * degPerHz;
            return v * v / (2 * decel) + 0.35 * v + 0.2;
        }
    }

    /// <summary>
    /// One step of the staged approach: run at a fixed speed until only <paramref name="leadDeg"/>
    /// remains, then stop and let the ramp run out.
    /// </summary>
    private async Task ApproachStagedAsync(Degree<double> target, CountDirection direction,
        double hz, double leadDeg, DateTime deadline, CancellationToken ct)
    {
        await JogStartAsync(hz, direction, JogRampRaw, ct);
        var delta = DeltaTo(target, await ReadPositionAsync(ct));
        var sign0 = Math.Sign(delta) >= 0 ? 1 : -1;
        var stallRef = delta;
        var stallAt = DateTime.UtcNow;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(ApproachPoll, ct);

                delta = DeltaTo(target, await ReadPositionAsync(ct));
                var inputs = await ReadInputsAsync(ct);
                if (LimitHit(inputs, direction) is { } hit)
                    throw new PositionerException(PositionerError.LimitTripped,
                        $"{_cfg.Name}: {hit} travel limit tripped") { Axis = _cfg.Name };

                if (Math.Abs(delta - stallRef) > 30)
                {
                    stallRef = delta;
                    stallAt = DateTime.UtcNow;
                }
                else if (DateTime.UtcNow - stallAt > StallTimeout)
                {
                    throw new PositionerException(PositionerError.Stalled,
                        $"{_cfg.Name}: not moving at {hz} Hz") { Axis = _cfg.Name };
                }

                if ((Math.Sign(delta) >= 0 ? 1 : -1) != sign0) break;          // crossed the target
                if (Math.Abs(delta) / _cfg.CountsPerDegree <= leadDeg) break;  // close enough to coast in
            }
        }
        finally
        {
            await JogStopAsync(CancellationToken.None);
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
    private async Task ApproachSmoothAsync(Degree<double> target, double handoverDeg,
        double decel, DateTime deadline, CancellationToken ct)
    {
        var degPerHz = _cfg.TheoreticalDegPerSecondPerHz;
        var plan = decel * _cfg.SmoothDecelerationFraction;
        var floorHz = Math.Max(_cfg.MinJogHz, 2.0);
        CountDirection? current = null;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                var delta = DeltaTo(target, await ReadPositionAsync(ct));
                var inputs = await ReadInputsAsync(ct);
                if (LimitHit(inputs, current) is { } hit)
                    throw new PositionerException(PositionerError.LimitTripped,
                        $"{_cfg.Name}: {hit} travel limit tripped") { Axis = _cfg.Name };

                var remaining = Math.Abs(delta) / _cfg.CountsPerDegree;
                if (remaining <= handoverDeg) return;

                var direction = delta > 0 ? CountDirection.Up : CountDirection.Down;
                var runway = Math.Max(0.0, remaining - handoverDeg);
                var hz = Math.Min(_moveHz, Math.Max(floorHz, Math.Sqrt(2.0 * plan * runway) / degPerHz));

                if (direction != current)
                {
                    if (current is not null) await JogStopAsync(ct);   // reverse only from a standstill
                    await JogStartAsync(hz, direction, JogRampRaw, ct);
                    current = direction;
                }
                else
                {
                    await SetFrequencyAsync(hz, ct);
                }

                await Task.Delay(SmoothPoll, ct);
            }

            throw new PositionerException(PositionerError.Timeout,
                $"{_cfg.Name}: positioning timed out") { Axis = _cfg.Name };
        }
        finally
        {
            await JogStopAsync(CancellationToken.None);
        }
    }

    // ═══════════════════════ elementary motion ═══════════════════════

    /// <summary>
    /// Direction expressed as the way the RAW COUNT moves. Kept distinct from the public
    /// <see cref="RotationDirection"/>, which is defined in terms of the reported ANGLE — on an axis
    /// with <see cref="DeltaAxisConfig.InvertAngle"/> the two are opposites, and conflating them
    /// makes "rotate forward" turn the machine the wrong way.
    /// </summary>
    private enum CountDirection
    {
        Up,
        Down,
    }

    private CountDirection ToCount(RotationDirection direction) =>
        (direction == RotationDirection.Forward) != _cfg.InvertAngle
            ? CountDirection.Up
            : CountDirection.Down;

    private RotationDirection ToRotation(CountDirection direction) =>
        (direction == CountDirection.Up) != _cfg.InvertAngle
            ? RotationDirection.Forward
            : RotationDirection.Reverse;

    private bool DirectionBit(CountDirection direction) =>
        (direction == CountDirection.Down) != _cfg.InvertDirection;

    private async Task JogStartAsync(double hz, CountDirection direction, ushort ramp, CancellationToken ct)
    {
        hz = Math.Max(hz, _cfg.MinJogHz);
        if (hz > MaxJogHz)
            throw new ArgumentOutOfRangeException(nameof(hz), hz, $"{_cfg.Name}: above the {MaxJogHz} Hz limit");

        await _channel.WriteRegistersAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D111_Ramp,
            [ramp, ramp], "set ramps", ct);
        await _channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency,
            (ushort)Math.Round(hz * 100), "set frequency", ct);
        await SetCoilAsync(DeltaRegisters.M5_Direction, DirectionBit(direction), ct);
        await SetCoilAsync(DeltaRegisters.M4_Move, true, ct);
    }

    /// <summary>Changes speed WITHOUT interrupting motion — the PLC tracks the register live.</summary>
    private Task SetFrequencyAsync(double hz, CancellationToken ct)
    {
        hz = Math.Clamp(hz, _cfg.MinJogHz, MaxJogHz);
        return _channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency,
            (ushort)Math.Round(hz * 100), "set frequency", ct);
    }

    /// <summary>
    /// Ramps down, waits for the drive to actually stop, then drops the motion coil. The coil must
    /// only fall on a stationary axis — the PLC switches mode on that edge.
    /// </summary>
    private async Task JogStopAsync(CancellationToken ct)
    {
        await _channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency,
            0, "ramp down", ct);

        var deadline = DateTime.UtcNow + JogStopTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(JogStopPoll, ct);
            if (await ReadOutputHzAsync(ct) <= 0.2) break;
        }

        await SetCoilAsync(DeltaRegisters.M4_Move, false, ct);
    }

    private async Task JogForAsync(CountDirection direction, double hz, TimeSpan duration, CancellationToken ct)
    {
        await JogStartAsync(hz, direction, JogRampRaw, ct);
        await Task.Delay(duration, ct);
        await JogStopAsync(ct);
    }

    private async Task JogUntilAsync(CountDirection direction, double hz, Func<bool[], bool> until,
        string what, TimeSpan timeout, CancellationToken ct)
    {
        await JogStartAsync(hz, direction, JogRampRaw, ct);
        var deadline = DateTime.UtcNow + timeout;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromMilliseconds(150), ct);

                var inputs = await ReadInputsAsync(ct);
                if (LimitHit(inputs, direction) is { } hit)
                    throw new PositionerException(PositionerError.LimitTripped,
                        $"{_cfg.Name}: {hit} travel limit tripped") { Axis = _cfg.Name };
                if (until(inputs)) return;
            }

            throw new PositionerException(PositionerError.Timeout, $"{_cfg.Name}: timed out {what}")
                { Axis = _cfg.Name };
        }
        finally
        {
            await JogStopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Timed micro-pulse — the only way to move less than the axis's smallest continuous step. The
    /// drive is cut at the end of the pulse rather than ramped, so the axis stops in milliseconds.
    /// </summary>
    private async Task PulseAsync(CountDirection direction, double seconds, CancellationToken ct)
    {
        await _channel.WriteRegistersAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D111_Ramp,
            [NudgeRampRaw, NudgeRampRaw], "set pulse ramps", ct);
        await _channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency,
            (ushort)Math.Round(_cfg.NudgeHz * 100), "set pulse frequency", ct);
        await SetCoilAsync(DeltaRegisters.M5_Direction, DirectionBit(direction), ct);
        await SetCoilAsync(DeltaRegisters.M4_Move, true, ct);

        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);

        await _channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency,
            0, "end pulse", ct);
        await SetCoilAsync(DeltaRegisters.M4_Move, false, ct);
        await _channel.WriteRegistersAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D111_Ramp,
            [JogRampRaw, JogRampRaw], "restore ramps", ct);

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
            catch (PositionerException)
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
            await _channel.WriteRegisterAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D110_Frequency, 0, "hard stop", ct);
            await SetCoilAsync(DeltaRegisters.M4_Move, false, ct);
            await SetCoilAsync(DeltaRegisters.M0_Run, false, ct);
        }
        catch (PositionerException ex)
        {
            _logger?.LogDebug(ex, "{Axis}: hard stop could not be delivered", _cfg.Name);
        }
    }

    // ═══════════════════════ limits ═══════════════════════

    private CountDirection AwayFrom(LimitSide side)
    {
        // Retreating from the lower limit means the ANGLE must increase, and vice versa.
        return ToCount(side == LimitSide.Min ? RotationDirection.Forward : RotationDirection.Reverse);
    }

    private LimitSide? LimitHit(bool[] inputs, CountDirection? moving)
    {
        if (_cfg.LimitInputs is not { } lim) return null;
        foreach (var side in new[] { LimitSide.Min, LimitSide.Max })
        {
            var input = side == LimitSide.Min ? lim.Min : lim.Max;
            if (!inputs[input] && moving != AwayFrom(side)) return side;
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
    /// functions are also re-asserted at startup.
    /// </para>
    /// </summary>
    private async Task ReleaseLimitAsync(LimitSide side, CancellationToken ct)
    {
        var parameter = side == LimitSide.Min ? DeltaRegisters.Pr0204_Mi4 : DeltaRegisters.Pr0205_Mi5;
        var function = side == LimitSide.Min ? DeltaRegisters.Mi4LimitFunction : DeltaRegisters.Mi5LimitFunction;
        var input = side == LimitSide.Min ? _cfg.LimitInputs!.Value.Min : _cfg.LimitInputs!.Value.Max;
        var away = AwayFrom(side);

        try
        {
            await HardStopAsync(ct);
            await Task.Delay(TimeSpan.FromMilliseconds(1500), ct);
            await ResetDriveFaultAsync(ct);
            await _channel.WriteRegisterAsync(DeltaRegisters.DriveUnit, parameter, 0, "lift limit function", ct);
            await SetCoilAsync(DeltaRegisters.M0_Run, true, ct);

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
                    "restore limit function", CancellationToken.None);
                await ResetDriveFaultAsync(CancellationToken.None);
                await SetCoilAsync(DeltaRegisters.M0_Run, true, CancellationToken.None);
            }
            catch (PositionerException ex)
            {
                _logger?.LogError(ex,
                    "{Axis}: FAILED to restore the {Side} limit function — the drive is running "
                    + "without hardware limit protection until it is restored", _cfg.Name, side);
            }
        }
    }

    private enum LimitSide
    {
        Min,
        Max,
    }

    // ═══════════════════════ maths ═══════════════════════

    private int Sign => _cfg.InvertAngle ? -1 : 1;

    private static long Mod(long value, long modulus) => ((value % modulus) + modulus) % modulus;

    /// <summary>Converts a raw count to an axis angle.</summary>
    public Degree<double> PulsesToDegrees(long pulses)
    {
        var rel = (pulses - _zeroOffset) * Sign;
        var degrees = _cfg.Continuous
            ? Mod(rel, _cfg.CountsPerRevolution) / _cfg.CountsPerDegree
            : rel / _cfg.CountsPerDegree;
        return Math.Round(degrees, 2);
    }

    /// <summary>
    /// Raw counts still to travel; the sign is the direction the COUNT must move. A continuous axis
    /// takes the short way round.
    /// </summary>
    private long DeltaTo(Degree<double> target, long pulses)
    {
        var rel = (pulses - _zeroOffset) * Sign;
        var wanted = (double)target * _cfg.CountsPerDegree;
        if (!_cfg.Continuous)
            return (long)Math.Round((wanted - rel) * Sign);

        var half = _cfg.CountsPerRevolution / 2.0;
        var shortest = Mod((long)Math.Round(wanted - Mod(rel, _cfg.CountsPerRevolution) + half),
            _cfg.CountsPerRevolution) - half;
        return (long)Math.Round(shortest * Sign);
    }

    // ═══════════════════════ raw I/O ═══════════════════════

    private Task<long> ReadPositionAsync(CancellationToken ct) =>
        _channel.ReadDWordAsync(DeltaRegisters.PlcUnit, DeltaRegisters.D1051_Position, "read position", ct)
            .ContinueWith(t => (long)t.Result, ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

    private Task<bool[]> ReadInputsAsync(CancellationToken ct) =>
        _channel.ReadDiscreteInputsAsync(DeltaRegisters.PlcUnit, DeltaRegisters.X0_Inputs,
            DeltaRegisters.InputCount, "read inputs", ct);

    private Task SetCoilAsync(ushort coil, bool value, CancellationToken ct) =>
        _channel.WriteCoilAsync(DeltaRegisters.PlcUnit, coil, value, "write coil", ct);

    private async Task<double> ReadOutputHzAsync(CancellationToken ct)
    {
        var regs = await _channel.ReadHoldingAsync(DeltaRegisters.DriveUnit,
            DeltaRegisters.OutputFrequency, 1, "read output frequency", ct);
        return regs[0] / 100.0;
    }

    private async Task<int> ReadFaultAsync(CancellationToken ct)
    {
        var regs = await _channel.ReadHoldingAsync(DeltaRegisters.DriveUnit,
            DeltaRegisters.FaultCode, 1, "read fault", ct);
        return regs[0] & 0xFF;
    }

    private async Task ResetDriveFaultAsync(CancellationToken ct)
    {
        await _channel.WriteRegisterAsync(DeltaRegisters.DriveUnit, DeltaRegisters.CommandWord, 0, "clear command", ct);
        await Task.Delay(TimeSpan.FromMilliseconds(400), ct);
        await _channel.WriteRegisterAsync(DeltaRegisters.DriveUnit, DeltaRegisters.CommandWord, 2, "reset fault", ct);
        await Task.Delay(TimeSpan.FromMilliseconds(1200), ct);
    }

    /// <inheritdoc/>
    public void Dispose() => _operationGate.Dispose();
}
