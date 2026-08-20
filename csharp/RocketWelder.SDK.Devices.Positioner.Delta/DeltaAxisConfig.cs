using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Devices.Positioner.Delta;

/// <summary>
/// Everything the controller needs to drive one axis. These are mechanical facts about a specific
/// machine, not preferences — most were established by measurement and are wrong for a differently
/// built positioner.
/// </summary>
public sealed record DeltaAxisConfig
{
    /// <summary>Stable axis identifier used in API calls.</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable label.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Drive endpoint (host or IP).</summary>
    public required string Host { get; init; }

    /// <summary>Modbus TCP port.</summary>
    public int Port { get; init; } = DeltaRegisters.Port;

    // ── Travel ────────────────────────────────────────────────────

    /// <summary>Lower travel limit.</summary>
    public required Degree<double> Min { get; init; }

    /// <summary>Upper travel limit.</summary>
    public required Degree<double> Max { get; init; }

    /// <summary>Endless rotary axis: wraps at 360° and takes the short way to a target.</summary>
    public bool Continuous { get; init; }

    /// <summary>Absolute positioning requires homing first.</summary>
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
    /// True when a positive axis angle corresponds to a DECREASING raw count.
    /// <para>Affects only the angle arithmetic, not which way the drive is told to turn.</para>
    /// </summary>
    public bool InvertAngle { get; init; }

    /// <summary>
    /// True when the drive's forward direction DECREASES the raw count, i.e. the motor is wired
    /// opposite to this controller's convention.
    /// <para>
    /// Leave false and fix the wiring where you can — a mismatch here makes positioning drive the
    /// correct distance the wrong way and settle opposite the target. Use
    /// <see cref="IPositionerAxis.VerifyDirectionAsync"/> at commissioning rather than guessing.
    /// </para>
    /// </summary>
    public bool InvertDirection { get; init; }

    // ── Speeds, in motor Hz (what the drive actually takes) ───────

    /// <summary>Speed used to search for the home sensor.</summary>
    public required double SeekHz { get; init; }

    /// <summary>Default traverse speed for positioning.</summary>
    public required double MoveHz { get; init; }

    /// <summary>Highest traverse speed allowed for positioning.</summary>
    public required double MaxMoveHz { get; init; }

    /// <summary>
    /// Lowest speed the axis reliably turns at. Below this the drive still outputs a frequency but
    /// the axis creeps unpredictably or not at all.
    /// </summary>
    public required double MinJogHz { get; init; }

    /// <summary>Speed used for the micro-pulses that close the last fraction of a degree.</summary>
    public required double NudgeHz { get; init; }

    // ── Endgame calibration ───────────────────────────────────────

    /// <summary>
    /// How far a micro-pulse actually moves the axis, as <c>degrees = Slope * (seconds - DeadTime)</c>.
    /// Measured; the relationship is not proportional because of breakaway friction.
    /// </summary>
    public required PulseCalibration Pulse { get; init; }

    /// <summary>Positioning tolerance — a move completes once inside this band.</summary>
    public required Degree<double> Tolerance { get; init; }

    // ── Speed calibration ─────────────────────────────────────────

    /// <summary>
    /// Converts between commanded motor frequency and actual axis speed, as
    /// <c>degPerSecond = Slope * hz + Intercept</c>.
    ///
    /// <para>
    /// A single constant is not good enough: slip and a dead band make the axis run well under the
    /// nominal figure at low speed, which is exactly the range circumferential welding uses. Left
    /// unset this falls back to the theoretical ratio with no dead band, which overstates the speed.
    /// </para>
    /// </summary>
    public SpeedCalibration? Speed { get; init; }

    // ── I/O ───────────────────────────────────────────────────────

    /// <summary>X input carrying the home sensor. Normally-closed: bit 0 means "sees the cam".</summary>
    public required int HomeSensorInput { get; init; }

    /// <summary>X inputs carrying the travel limits, or <c>null</c> on an axis without them.</summary>
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
    /// <para>
    /// Must leave room for the ramp to run out, otherwise the continuous phase overshoots. The
    /// controller also uses this as the pulse threshold — they have to be one number, or a dead
    /// band opens up in which neither phase advances the axis.
    /// </para>
    /// </summary>
    public Degree<double> SmoothHandover { get; init; } = 0.8;

    // ── Derived ───────────────────────────────────────────────────

    /// <summary>Raw counts per degree of axis rotation.</summary>
    public double CountsPerDegree => CountsPerRevolution / 360.0;

    /// <summary>Theoretical axis speed per motor Hz, from the gearing.</summary>
    public double TheoreticalDegPerSecondPerHz => 360.0 * 10000.0 / (2.0 * GearRatio * CountsPerRevolution);

    /// <summary>Speed calibration, falling back to the theoretical ratio when none was measured.</summary>
    public SpeedCalibration SpeedCalibration => Speed ?? new SpeedCalibration(TheoreticalDegPerSecondPerHz, 0.0);
}

/// <summary>
/// Linear fit of actual axis speed against commanded motor frequency:
/// <c>degPerSecond = Slope * hz + Intercept</c>. A negative intercept is a dead band.
/// </summary>
/// <param name="Slope">Degrees per second gained per Hz.</param>
/// <param name="Intercept">Offset; negative means the axis does not move until some minimum Hz.</param>
public readonly record struct SpeedCalibration(double Slope, double Intercept)
{
    /// <summary>Axis speed produced by a commanded frequency (never negative).</summary>
    public double ToDegPerSecond(double hz) => Math.Max(0.0, Slope * hz + Intercept);

    /// <summary>Frequency needed to obtain a wanted axis speed.</summary>
    public double ToHz(double degPerSecond) => (degPerSecond - Intercept) / Slope;

    /// <summary>Frequency below which the axis does not turn at all.</summary>
    public double DeadBandHz => Intercept >= 0 ? 0.0 : -Intercept / Slope;
}

/// <summary>
/// How far a timed micro-pulse moves the axis: <c>degrees = Slope * (seconds - DeadTime)</c>.
/// The dead time is breakaway — a pulse shorter than it moves nothing.
/// </summary>
/// <param name="Slope">Degrees per second of pulse, once moving.</param>
/// <param name="DeadTime">Seconds of pulse that produce no motion.</param>
/// <param name="MinSeconds">Shortest pulse worth issuing.</param>
/// <param name="MaxSeconds">Longest pulse; beyond this use a normal approach.</param>
public readonly record struct PulseCalibration(
    double Slope,
    double DeadTime,
    double MinSeconds = 0.12,
    double MaxSeconds = 0.60)
{
    /// <summary>Pulse length needed to move a given distance, clamped to the usable range.</summary>
    public double SecondsFor(double degrees) =>
        Math.Clamp(DeadTime + degrees / Slope, MinSeconds, MaxSeconds);

    /// <summary>Distance a pulse of the given length actually produces.</summary>
    public double DegreesFor(double seconds) => Math.Max(0.0, Slope * (seconds - DeadTime));
}
