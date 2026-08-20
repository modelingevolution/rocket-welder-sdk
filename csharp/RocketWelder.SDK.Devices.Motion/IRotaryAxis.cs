using ModelingEvolution.Drawing;
using ModelingEvolution.Drawing.Units;

namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// A revolute axis: positions in <see cref="Degree{T}"/>, speeds in
/// <c>AngularSpeed&lt;double, DegreePerSecond&lt;double&gt;&gt;</c>.
///
/// <para>
/// <b>Import <see cref="ModelingEvolution.Drawing.Units"/> explicitly.</b> The root
/// <c>ModelingEvolution.Drawing</c> namespace also carries a legacy <c>Speed&lt;T&gt;</c>
/// hard-wired to mm/min — same name, different physics. Axis code must never use it.
/// </para>
///
/// <para>
/// <see cref="Degree{T}"/> converts implicitly from its numeric type, so
/// <c>MoveAbsoluteAsync(45)</c> compiles. The angular-speed dimension has SI base rad/s and is
/// disjoint from the linear <c>ISpeedUnit</c> dimension, so handing a linear speed to this axis —
/// or adding °/s to mm/s — does not compile (FR-2, AC-21).
/// </para>
/// </summary>
public interface IRotaryAxis : IMotionAxis
{
    /// <summary>The typed angle read, or <see langword="null"/> when the position is unknown.</summary>
    Degree<double>? Angle { get; }

    /// <summary>Lower bound: the start of the wrap domain [0°, 360°) when the axis declares
    /// <see cref="AxisCapabilities.ContinuousRotation"/>, otherwise the lower travel limit.</summary>
    Degree<double> Min { get; }

    /// <summary>Upper bound: the end of the wrap domain when the axis declares
    /// <see cref="AxisCapabilities.ContinuousRotation"/>, otherwise the upper travel limit — a
    /// target outside it is rejected with <see cref="MotionError.OutOfRange"/>.</summary>
    Degree<double> Max { get; }

    /// <summary>The in-position tolerance a move settles within.</summary>
    Degree<double> Tolerance { get; }

    /// <summary>The lowest speed the axis can actually deliver. A request below it is rejected with
    /// <see cref="MotionError.UnreachableSpeed"/>, never raised to the floor (FR-5).</summary>
    AngularSpeed<double, DegreePerSecond<double>> MinSpeed { get; }

    /// <summary>The highest speed the axis can deliver — and what a <see cref="Percentage"/>
    /// overload resolves against.</summary>
    AngularSpeed<double, DegreePerSecond<double>> MaxSpeed { get; }

    /// <summary>
    /// Moves to an absolute angle. Completes when the motion has <b>physically finished</b> —
    /// callers never poll. Cancelling <paramref name="ct"/> stops the axis.
    /// </summary>
    /// <param name="target">The absolute target. On a wrapping axis it is normalised into
    /// [<see cref="Min"/>, <see cref="Max"/>); otherwise out-of-range is rejected.</param>
    /// <param name="speed">Traverse speed; <see langword="null"/> uses the axis default.</param>
    /// <param name="sense">Which way round to reach the target — meaningful only on a wrapping axis.</param>
    /// <param name="ct">Cancellation; cancelling stops the axis.</param>
    /// <exception cref="MotionException">The axis is not in <see cref="AxisState.Standstill"/>
    /// (<see cref="MotionError.Busy"/>), the speed is unreachable, the target is out of range, or
    /// the sense is unsupported on this axis.</exception>
    Task MoveAbsoluteAsync(Degree<double> target,
                           AngularSpeed<double, DegreePerSecond<double>>? speed = null,
                           RotationSense sense = RotationSense.Shortest,
                           CancellationToken ct = default);

    /// <summary>
    /// Moves to an absolute angle at a percentage of <see cref="MaxSpeed"/>. The percentage
    /// resolves against <see cref="MaxSpeed"/> <b>first</b> and the result is then subject to the
    /// same rejection rule — <c>Percentage(1)</c> of a fast axis that lands below
    /// <see cref="MinSpeed"/> is rejected, not raised (FR-5).
    /// </summary>
    Task MoveAbsoluteAsync(Degree<double> target, Percentage speedOfMax,
                           RotationSense sense = RotationSense.Shortest,
                           CancellationToken ct = default);

    /// <summary>
    /// Moves by a signed delta from the current angle; unbounded on a wrapping axis. There is no
    /// <see cref="RotationSense"/>: the sign of <paramref name="delta"/> is the direction.
    /// </summary>
    Task MoveRelativeAsync(Degree<double> delta,
                           AngularSpeed<double, DegreePerSecond<double>>? speed = null,
                           CancellationToken ct = default);

    /// <summary>Moves by a signed delta at a percentage of <see cref="MaxSpeed"/>.</summary>
    Task MoveRelativeAsync(Degree<double> delta, Percentage speedOfMax,
                           CancellationToken ct = default);

    /// <summary>
    /// Turns at a commanded velocity (<c>MC_MoveVelocity</c>). The <b>sign</b> of
    /// <paramref name="velocity"/> is the direction — this is the only velocity form, and it takes
    /// no <see cref="Percentage"/> overload because a percentage cannot be negative (P-2).
    ///
    /// <para>
    /// The returned task completes when the <b>commanded velocity is reached</b>, not when the
    /// motion ends — continuous rotation has no end. The axis stays in
    /// <see cref="AxisState.ContinuousMotion"/> until <see cref="IMotionAxis.StopAsync"/>, and
    /// <paramref name="ct"/> <b>remains observed after the task completes</b>: cancelling it stops
    /// the axis.
    /// </para>
    /// </summary>
    Task MoveVelocityAsync(AngularSpeed<double, DegreePerSecond<double>> velocity,
                           CancellationToken ct = default);
}
