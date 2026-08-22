using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Devices.Motion.Delta;

/// <summary>
/// Everything the adapter needs to drive one axis. These are mechanical facts about a specific
/// machine, not preferences — most were established by measurement and are wrong for a differently
/// built positioner, which is why FR-8 puts the <i>values</i> in the devices hub and only the
/// <i>roster</i> in code.
///
/// <para>
/// Frequencies are the drive's own unit and stay <see cref="Frequency{T}"/>-typed here; the axis's
/// engineering unit (°/s) is reached only through <see cref="SpeedCalibration"/>.
/// </para>
/// </summary>
public sealed record DeltaAxisConfig
{
    /// <summary>The plugin-frozen axis name (FR-8) — role-based and vendor-neutral.</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable label; this is where renaming happens.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Drive endpoint (host or IP).</summary>
    public required string Host { get; init; }

    /// <summary>Modbus TCP port.</summary>
    public int Port { get; init; } = DeltaRegisters.Port;

    // ── Travel ────────────────────────────────────────────────────

    /// <summary>
    /// Lower bound: the start of the wrap domain when <see cref="Continuous"/>, otherwise the lower
    /// travel limit.
    /// </summary>
    public required Degree<double> Min { get; init; }

    /// <summary>
    /// Upper bound: the end of the wrap domain when <see cref="Continuous"/>, otherwise the upper
    /// travel limit — a target outside it is rejected with <see cref="MotionError.OutOfRange"/>.
    /// </summary>
    public required Degree<double> Max { get; init; }

    /// <summary>
    /// Endless rotary axis: wraps at 360°, absolute targets are normalised into
    /// [<see cref="Min"/>, <see cref="Max"/>) and <see cref="RotationSense"/> selects the path.
    /// Surfaces as <see cref="AxisCapabilities.ContinuousRotation"/>.
    /// </summary>
    public bool Continuous { get; init; }

    /// <summary>
    /// Absolute positioning requires homing first. Surfaces as
    /// <see cref="AxisCapabilities.Homing"/>.
    /// </summary>
    public bool RequiresHoming { get; init; } = true;

    // ── Mechanics ─────────────────────────────────────────────────

    /// <summary>
    /// Raw quadrature counts per full revolution OF THE AXIS. Measured on the machine — the encoder
    /// is behind the gearbox, so this is not derivable from the encoder's own resolution.
    /// </summary>
    public required int CountsPerRevolution { get; init; }

    /// <summary>
    /// Motor-to-encoder ratio. Must match the drive's <c>Pr.10-04/10-05</c> or the drive raises
    /// PGFb warnings.
    /// </summary>
    public required double GearRatio { get; init; }

    /// <summary>
    /// <b>Mounting.</b> True when a positive axis angle corresponds to a DECREASING raw count.
    /// Affects only the angle arithmetic, not which way the drive is told to turn.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="InvertDirection"/>: the docs conflate the two, and they
    /// are different facts about the machine (risk R-4). This one is permanently true on the tilt
    /// axis because of how the gearbox is mounted.
    /// </remarks>
    public bool InvertAngle { get; init; }

    /// <summary>
    /// <b>Wiring.</b> True when the drive's forward direction DECREASES the raw count, i.e. the motor
    /// is wired opposite to this adapter's convention.
    /// </summary>
    /// <remarks>
    /// Leave false and fix the wiring where you can — a mismatch here makes positioning drive the
    /// correct distance the wrong way and settle opposite the target, which reads as a broken control
    /// loop rather than a wiring fault. Use <see cref="ISelfCheckingAxis.VerifyDirectionAsync"/> at
    /// commissioning rather than guessing (FR-7).
    /// </remarks>
    public bool InvertDirection { get; init; }

    // ── Speeds, in motor Hz (what the drive actually takes) ───────

    /// <summary>Speed used to search for the home sensor.</summary>
    public required Frequency<double> SeekHz { get; init; }

    /// <summary>Default traverse speed for positioning, used when a caller passes no speed.</summary>
    public required Frequency<double> MoveHz { get; init; }

    /// <summary>
    /// Highest traverse speed allowed for positioning. This is what
    /// <see cref="IRotaryAxis.MaxSpeed"/> resolves from, and therefore what a
    /// <c>Percentage</c> overload resolves against.
    /// </summary>
    public required Frequency<double> MaxMoveHz { get; init; }

    /// <summary>
    /// Lowest speed the axis reliably turns at. Below this the drive still outputs a frequency but
    /// the axis creeps unpredictably or not at all — so <see cref="IRotaryAxis.MinSpeed"/> resolves
    /// from it and anything under is rejected, never raised (FR-5).
    /// </summary>
    public required Frequency<double> MinJogHz { get; init; }

    /// <summary>Speed used for the micro-pulses that close the last fraction of a degree.</summary>
    public required Frequency<double> NudgeHz { get; init; }

    /// <summary>
    /// Hard ceiling for any jog, positioning or not. Above this the adapter refuses to command the
    /// drive at all.
    /// </summary>
    /// <remarks>
    /// <b>Provenance: inherited from Daniel's port</b> (<c>DeltaAxis.MaxJogHz = 60.0</c>), where it
    /// was a bare constant. Nothing measured the drive's real ceiling — the one recorded sweep stops
    /// at 50 Hz. Named here rather than hidden so a bench session can pin it; the simulator's
    /// <c>MaxOutputHz</c> carries the same 60 for the same reason and says so.
    /// </remarks>
    public Frequency<double> MaxJogHz { get; init; } = Frequency<double>.FromHertz(60.0);

    // ── Endgame calibration ───────────────────────────────────────

    /// <summary>
    /// How far a micro-pulse actually moves the axis. Measured; the relationship is not proportional
    /// because of breakaway friction.
    /// </summary>
    public required PulseCalibration Pulse { get; init; }

    /// <summary>Positioning tolerance — a move completes once inside this band.</summary>
    public required Degree<double> Tolerance { get; init; }

    // ── Speed calibration ─────────────────────────────────────────

    /// <summary>
    /// The per-machine speed fit (FR-5). Left unset this falls back to the theoretical ratio with no
    /// dead band, which overstates the speed badly at the low end.
    /// </summary>
    public SpeedCalibration? Speed { get; init; }

    // ── I/O ───────────────────────────────────────────────────────

    /// <summary>X input carrying the home sensor. Normally-closed: bit 0 means "sees the cam".</summary>
    public required int HomeSensorInput { get; init; }

    /// <summary>X inputs carrying the travel limits, or <see langword="null"/> on an axis without them.</summary>
    public (int Min, int Max)? LimitInputs { get; init; }

    // ── Approach strategy ─────────────────────────────────────────

    /// <summary>
    /// Decelerate continuously into the target instead of stepping down through fixed speeds.
    /// Measured on the turntable: 24 % faster and the axis never stops mid-approach.
    /// </summary>
    public bool SmoothApproach { get; init; }

    /// <summary>
    /// Fraction of the drive's real deceleration used when planning the continuous approach.
    /// Below 1 so the ramp always keeps up with the commanded speed.
    /// </summary>
    public double SmoothDecelerationFraction { get; init; } = 0.7;

    /// <summary>
    /// Distance at which the continuous approach hands over to micro-pulses.
    /// </summary>
    /// <remarks>
    /// Must leave room for the ramp to run out, otherwise the continuous phase overshoots. The
    /// adapter also uses this as the pulse threshold — they have to be one number, or a dead band
    /// opens up in which neither phase advances the axis.
    /// <para>
    /// <b>Bench-only.</b> This, the braking margin and the lead distance are tuned against the
    /// physical machine and never against the simulator, whose coast model is the programmed ramp
    /// alone and runs ~7 % fast on long moves.
    /// </para>
    /// </remarks>
    public Degree<double> SmoothHandover { get; init; } = 0.8;

    // ── FR-11 watchdog ────────────────────────────────────────────

    /// <summary>
    /// The heartbeat this adapter writes into D130 while its connection lives (FR-11). Default
    /// 5 Hz — decision D-f pins the beat rate at ≥ 5 Hz against a 1 s stall window, leaving four
    /// missed beats of slack.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// The drive-side stall window, which is also the advisory lease's expiry (D-f: 1 s).
    /// </summary>
    public TimeSpan WatchdogStallWindow { get; init; } = TimeSpan.FromSeconds(1);

    // ── Derived ───────────────────────────────────────────────────

    /// <summary>Raw counts per degree of axis rotation.</summary>
    public double CountsPerDegree => CountsPerRevolution / 360.0;

    /// <summary>Theoretical axis speed per motor Hz, from the gearing alone.</summary>
    public double TheoreticalDegPerSecondPerHz => 360.0 * 10000.0 / (2.0 * GearRatio * CountsPerRevolution);

    /// <summary>
    /// The speed calibration in force, falling back to the theoretical ratio when none was measured.
    /// </summary>
    public SpeedCalibration SpeedCalibration => Speed ?? new SpeedCalibration(TheoreticalDegPerSecondPerHz, 0.0);

    /// <summary>
    /// Checks that the frequencies are orderable and that every speed this axis will ever command
    /// falls inside its own declared range.
    ///
    /// <para>
    /// This exists because FR-5's promise — a speed outside the achievable range is <b>rejected,
    /// never clamped</b> — cannot be kept by the command path alone. The default traverse speed and
    /// the seek and nudge speeds never pass through the caller-facing check, so a configuration with
    /// <c>MoveHz</c> below <c>MinJogHz</c> would have been silently raised to the floor deep inside
    /// the jog. Catching it at construction turns a wrong machine into a startup failure that names
    /// the field, instead of a machine that quietly runs at a speed nobody asked for.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">Some frequency is out of order or non-positive.</exception>
    public void Validate()
    {
        if (MinJogHz.Hertz <= 0)
            throw Invalid($"{nameof(MinJogHz)} must be positive (is {MinJogHz.Hertz:0.###} Hz)");

        if (MaxMoveHz < MinJogHz)
            throw Invalid($"{nameof(MaxMoveHz)} ({MaxMoveHz.Hertz:0.###} Hz) is below "
                + $"{nameof(MinJogHz)} ({MinJogHz.Hertz:0.###} Hz)");

        if (MaxJogHz < MaxMoveHz)
            throw Invalid($"{nameof(MaxJogHz)} ({MaxJogHz.Hertz:0.###} Hz) is below "
                + $"{nameof(MaxMoveHz)} ({MaxMoveHz.Hertz:0.###} Hz), so the guard would reject "
                + "speeds the axis advertises as reachable");

        foreach (var (name, value) in new[]
                 {
                     (nameof(MoveHz), MoveHz), (nameof(SeekHz), SeekHz), (nameof(NudgeHz), NudgeHz),
                 })
        {
            if (value < MinJogHz || value > MaxMoveHz)
                throw Invalid($"{name} ({value.Hertz:0.###} Hz) is outside "
                    + $"{MinJogHz.Hertz:0.###}–{MaxMoveHz.Hertz:0.###} Hz, the range this axis "
                    + "declares as reachable");
        }

        // The X inputs are read as one block of DeltaRegisters.InputCount discrete inputs, and every
        // index below is used to subscript that array. An out-of-range index is a wrong machine, and
        // it must fail HERE, by name, at startup — not as an IndexOutOfRangeException surfacing from
        // the middle of a jog, which is what it did before.
        RequireInputIndex(nameof(HomeSensorInput), HomeSensorInput);
        if (LimitInputs is { } limits)
        {
            RequireInputIndex($"{nameof(LimitInputs)}.Min", limits.Min);
            RequireInputIndex($"{nameof(LimitInputs)}.Max", limits.Max);

            if (limits.Min == limits.Max)
                throw Invalid($"{nameof(LimitInputs)} names input {limits.Min} for both ends of "
                    + "travel, so the axis could never tell which limit it was resting on");

            if (limits.Min == HomeSensorInput || limits.Max == HomeSensorInput)
                throw Invalid($"{nameof(HomeSensorInput)} ({HomeSensorInput}) collides with a travel "
                    + "limit input, so homing and the limit check would read the same switch");
        }

        if (Max < Min)
            throw Invalid($"{nameof(Max)} ({(double)Max:0.##}°) is below {nameof(Min)} ({(double)Min:0.##}°)");

        if ((double)Tolerance <= 0)
            throw Invalid($"{nameof(Tolerance)} must be positive (is {(double)Tolerance:0.###}°)");

        if (CountsPerRevolution <= 0)
            throw Invalid($"{nameof(CountsPerRevolution)} must be positive (is {CountsPerRevolution})");

        if (SpeedCalibration.Slope <= 0)
            throw Invalid($"the speed calibration's slope must be positive (is "
                + $"{SpeedCalibration.Slope:0.####}); a non-positive slope makes every speed "
                + "conversion meaningless");
    }

    private void RequireInputIndex(string field, int index)
    {
        if (index < 0 || index >= DeltaRegisters.InputCount)
            throw Invalid($"{field} is X{index}, outside the X0–X{DeltaRegisters.InputCount - 1} block "
                + "the adapter reads in one transaction");
    }

    private ArgumentException Invalid(string what) =>
        new($"Axis '{Name}': {what}.", nameof(DeltaAxisConfig));
}
