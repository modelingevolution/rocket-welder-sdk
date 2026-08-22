using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Devices.Motion.Delta;

/// <summary>
/// Mechanical constants of the two-axis Delta positioner characterised on 2026-08-19. Established by
/// measurement on <b>that</b> machine and wrong for a differently built one — which is why FR-8 puts
/// per-installation values in the devices hub and leaves only the roster in code. These are the
/// starting point a commissioning session re-measures, not constants of nature.
///
/// <para>
/// <b>Provenance of every value that is not measured</b> is stated on the value itself, following
/// the convention the simulator repository established: a constant that is invented and does not say
/// so is the failure mode this table exists to avoid.
/// </para>
/// </summary>
public static class DeltaPositionerDefaults
{
    /// <summary>The frozen axis name of the tilt axis (FR-8) — role-based, never vendor-specific.</summary>
    public const string TiltAxisName = "tilt";

    /// <summary>The frozen axis name of the turntable axis (FR-8).</summary>
    public const string TurntableAxisName = "turntable";

    /// <summary>
    /// The turntable's <b>measured</b> speed fit: <c>°/s = 0.5435 · hz − 0.199</c>, dead band
    /// 0.366 Hz, asymptotic slip 2.6 % against the theoretical 0.558 (°/s)/Hz. The one recorded speed
    /// sweep on the machine.
    /// </summary>
    public static SpeedCalibration TurntableSpeed { get; } = new(0.5435, -0.199);

    /// <summary>
    /// The tilt axis's speed fit — <b>DERIVED, not measured</b> (risk R-4, widened by simulator
    /// finding #1).
    ///
    /// <para>
    /// <c>current-state.md</c> records exactly one speed sweep and presents it unlabelled. Its slope
    /// (0.5435) is 97.4 % of the TURNTABLE's theoretical 0.558 (°/s)/Hz and 239 % of the tilt's
    /// 0.227, so the sweep is the turntable's and cannot be used for tilt as printed. Carried here
    /// as the same 2.6 % slip applied to tilt's own gearing with the same 0.366 Hz dead band — the
    /// dead band is a property of the drive and motor, not of the gearbox behind them. Identical
    /// derivation to the simulator's <c>MachineDefaults.TiltSpeed</c>, deliberately.
    /// </para>
    ///
    /// <para><b>Re-measure on the tilt axis before any tilt speed number is trusted.</b></para>
    /// </summary>
    public static SpeedCalibration TiltSpeed { get; } = DeriveFromTurntable(79.2);

    /// <summary>Tilt axis — limited travel, homing required, limit switches on MI4/MI5.</summary>
    public static DeltaAxisConfig Tilt { get; } = new()
    {
        Name = TiltAxisName,
        DisplayName = "Tilt",
        Host = "192.168.2.34",
        Min = Degree<double>.Create(-45.0),
        Max = Degree<double>.Create(90.0),
        Continuous = false,
        RequiresHoming = true,
        CountsPerRevolution = 100_000,
        GearRatio = 79.2,                        // Pr.10-04/05 = 7920/100
        InvertAngle = true,                      // MOUNTING, permanently true on this axis
        SeekHz = Frequency<double>.FromHertz(8.0),
        MoveHz = Frequency<double>.FromHertz(25.0),
        MaxMoveHz = Frequency<double>.FromHertz(50.0),
        MinJogHz = Frequency<double>.FromHertz(2.5),
        NudgeHz = Frequency<double>.FromHertz(3.0),
        // INHERITED, UNVERIFIED. The turntable's equivalent turned out to be wrong by 2x plus a dead
        // time, so treat this as a placeholder until measured on the tilt axis too (risk R-4). It
        // currently meets the 0.10° tolerance regardless — on inherited numbers.
        Pulse = new PulseCalibration(Slope: 0.7, DeadTime: 0.0),
        Tolerance = Degree<double>.Create(0.10),
        HomeSensorInput = 7,
        LimitInputs = (Min: 5, Max: 6),
        // Measured on this axis: the stepped cascade crawled every move under ~17° at 2.5 Hz
        // (0.57 °/s), so 5–15° moves took 21–49 s. Continuous deceleration does the same moves in
        // 2.7–3.7 s with equal or better accuracy.
        // NOT yet exercised against a tripped travel limit — mid-range and long moves only (R-2).
        SmoothApproach = true,
        SmoothDecelerationFraction = 0.7,
        // Smaller than the turntable's: this axis hands over at a much lower speed, so it coasts
        // ~0.06° rather than ~0.5° after the handover. BENCH-MEASURED — never re-tuned against the
        // simulator, whose coast model is the programmed ramp alone.
        SmoothHandover = Degree<double>.Create(0.3),
        Speed = TiltSpeed,                       // DERIVED — see TiltSpeed
    };

    /// <summary>Turntable — endless rotary axis, no limit switches.</summary>
    public static DeltaAxisConfig Turntable { get; } = new()
    {
        Name = TurntableAxisName,
        DisplayName = "Turntable",
        Host = "192.168.2.35",
        // The wrap domain, not travel limits: this axis declares ContinuousRotation.
        Min = Degree<double>.Create(0.0),
        Max = Degree<double>.Create(360.0),
        Continuous = true,
        RequiresHoming = false,
        CountsPerRevolution = 100_000,
        GearRatio = 32.26,                       // Pr.10-04/05 = 3226/100
        InvertAngle = false,
        SeekHz = Frequency<double>.FromHertz(10.0),
        MoveHz = Frequency<double>.FromHertz(30.0),
        MaxMoveHz = Frequency<double>.FromHertz(50.0),
        MinJogHz = Frequency<double>.FromHertz(1.0),
        NudgeHz = Frequency<double>.FromHertz(2.0),
        // Measured: 0.15 s -> 0.036°, 0.25 s -> 0.108°, 0.40 s -> 0.216°
        Pulse = new PulseCalibration(Slope: 0.72, DeadTime: 0.10),
        Tolerance = Degree<double>.Create(0.05),
        HomeSensorInput = 7,
        LimitInputs = null,
        SmoothApproach = true,
        SmoothDecelerationFraction = 0.7,
        SmoothHandover = Degree<double>.Create(0.8),     // BENCH-MEASURED
        Speed = TurntableSpeed,                  // MEASURED
    };

    /// <summary>
    /// Scales the single measured sweep onto another axis's gearing, keeping the measured slip and
    /// the measured dead band in Hz. Mirrors the simulator's derivation exactly, so the two agree
    /// about a number neither of them measured.
    /// </summary>
    public static SpeedCalibration DeriveFromTurntable(double gearRatio)
    {
        const double turntableGearRatio = 32.26;
        var turntableTheoretical = 360.0 * 10000.0 / (2.0 * turntableGearRatio * 100_000.0);
        var slip = TurntableSpeed.Slope / turntableTheoretical;
        var deadBandHz = TurntableSpeed.DeadBandHz;

        var theoretical = 360.0 * 10000.0 / (2.0 * gearRatio * 100_000.0);
        var slope = theoretical * slip;
        return new SpeedCalibration(slope, -slope * deadBandHz);
    }
}
