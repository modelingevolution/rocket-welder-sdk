using ModelingEvolution.Drawing;
using ModelingEvolution.Drawing.Units;

namespace RocketWelder.SDK.Devices.Motion.Delta;

/// <summary>
/// The per-machine conversion between the axis's engineering unit and what the drive actually takes
/// (FR-5): a linear fit of axis speed against commanded motor frequency,
/// <c>°/s = Slope · hz + Intercept</c>. A negative intercept is a dead band.
///
/// <para>
/// <b>This type is the whole typed-speed boundary.</b> Everything above it speaks
/// <c>AngularSpeed&lt;double, DegreePerSecond&lt;double&gt;&gt;</c>; everything below it speaks
/// <see cref="Frequency{T}"/>, and nothing in between carries a bare <c>double</c> that could be
/// either. The bare-double members remain for the arithmetic inside the fit itself.
/// </para>
///
/// <para>
/// <b>Why a fit and not a ratio.</b> The nominal gearing ratio overstates real speed by +3 % at
/// 50 Hz but <b>+128 % at 1 Hz</b> — and low speed is exactly where circumferential welding runs.
/// Slip and the drive's dead band are what the fit captures.
/// </para>
/// </summary>
/// <param name="Slope">Degrees per second gained per Hz.</param>
/// <param name="Intercept">Offset in °/s; negative means the axis does not move until some minimum Hz.</param>
public readonly record struct SpeedCalibration(double Slope, double Intercept)
{
    /// <summary>Axis speed produced by a commanded frequency (never negative).</summary>
    public double ToDegPerSecond(double hz) => Math.Max(0.0, Slope * hz + Intercept);

    /// <summary>Frequency needed to obtain a wanted axis speed.</summary>
    public double ToHz(double degPerSecond) => (degPerSecond - Intercept) / Slope;

    /// <summary>Frequency below which the axis does not turn at all.</summary>
    public double DeadBandHz => Intercept >= 0 ? 0.0 : -Intercept / Slope;

    /// <summary>
    /// The typed read of <see cref="ToDegPerSecond"/>: what the axis actually does at a commanded
    /// frequency. Magnitude only — the sign of a motion lives in the direction coil, not in the
    /// frequency register, which cannot be negative.
    /// </summary>
    public AngularSpeed<double, DegreePerSecond<double>> ToAngularSpeed(Frequency<double> hz) =>
        new(ToDegPerSecond(hz.Hertz));

    /// <summary>
    /// The typed write of <see cref="ToHz"/>: the frequency to command for a wanted axis speed. The
    /// <b>magnitude</b> of <paramref name="speed"/> is converted; a signed velocity's sign is carried
    /// by the direction coil (P-2), so a negative speed and its positive twin produce the same
    /// frequency.
    /// </summary>
    public Frequency<double> ToFrequency(AngularSpeed<double, DegreePerSecond<double>> speed) =>
        Frequency<double>.FromHertz(ToHz(Math.Abs(speed.Value)));
}

/// <summary>
/// How far a timed micro-pulse moves the axis: <c>degrees = Slope · (seconds − DeadTime)</c>.
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
