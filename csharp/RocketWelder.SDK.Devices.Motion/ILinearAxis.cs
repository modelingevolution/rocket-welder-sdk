using ModelingEvolution.Drawing.Units;

namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// A prismatic axis: positions in <c>Length&lt;double, Millimetre&lt;double&gt;&gt;</c>, speeds in
/// <c>Speed&lt;double, MillimetrePerSecond&lt;double&gt;&gt;</c>. Same pattern as
/// <see cref="IRotaryAxis"/>, with no <see cref="RotationSense"/> anywhere — a track has one path
/// to a target.
///
/// <para>
/// <b>Import <see cref="ModelingEvolution.Drawing.Units"/> explicitly.</b> The root
/// <c>ModelingEvolution.Drawing</c> namespace also carries a legacy <c>Speed&lt;T&gt;</c>
/// hard-wired to mm/min — same name, different physics. Axis code must never use it.
/// </para>
///
/// <para>
/// <c>Length&lt;T, TUnit&gt;</c> has no implicit conversion from its numeric type, so a call site
/// reads <c>MoveAbsoluteAsync(new(150))</c> via target-typed <c>new</c>.
/// </para>
/// </summary>
public interface ILinearAxis : IMotionAxis
{
    /// <summary>The typed position read, or <see langword="null"/> when it is unknown.</summary>
    Length<double, Millimetre<double>>? Offset { get; }

    /// <summary>The lower travel limit; a target below it is rejected with
    /// <see cref="MotionError.OutOfRange"/>.</summary>
    Length<double, Millimetre<double>> Min { get; }

    /// <summary>The upper travel limit; a target above it is rejected with
    /// <see cref="MotionError.OutOfRange"/>.</summary>
    Length<double, Millimetre<double>> Max { get; }

    /// <summary>The in-position tolerance a move settles within.</summary>
    Length<double, Millimetre<double>> Tolerance { get; }

    /// <summary>The lowest speed the axis can actually deliver. A request below it is rejected with
    /// <see cref="MotionError.UnreachableSpeed"/>, never raised to the floor (FR-5).</summary>
    Speed<double, MillimetrePerSecond<double>> MinSpeed { get; }

    /// <summary>The highest speed the axis can deliver — and what a <see cref="Percentage"/>
    /// overload resolves against.</summary>
    Speed<double, MillimetrePerSecond<double>> MaxSpeed { get; }

    /// <summary>
    /// Moves to an absolute position. Completes when the motion has <b>physically finished</b> —
    /// callers never poll. Cancelling <paramref name="ct"/> stops the axis.
    /// </summary>
    /// <exception cref="MotionException">The axis is not in <see cref="AxisState.Standstill"/>
    /// (<see cref="MotionError.Busy"/>), the speed is unreachable, or the target is out of range.</exception>
    Task MoveAbsoluteAsync(Length<double, Millimetre<double>> target,
                           Speed<double, MillimetrePerSecond<double>>? speed = null,
                           CancellationToken ct = default);

    /// <summary>
    /// Moves to an absolute position at a percentage of <see cref="MaxSpeed"/>. The percentage
    /// resolves against <see cref="MaxSpeed"/> <b>first</b> and the result is then subject to the
    /// same rejection rule (FR-5).
    /// </summary>
    Task MoveAbsoluteAsync(Length<double, Millimetre<double>> target, Percentage speedOfMax,
                           CancellationToken ct = default);

    /// <summary>Moves by a signed delta from the current position; the sign is the direction.</summary>
    Task MoveRelativeAsync(Length<double, Millimetre<double>> delta,
                           Speed<double, MillimetrePerSecond<double>>? speed = null,
                           CancellationToken ct = default);

    /// <summary>Moves by a signed delta at a percentage of <see cref="MaxSpeed"/>.</summary>
    Task MoveRelativeAsync(Length<double, Millimetre<double>> delta, Percentage speedOfMax,
                           CancellationToken ct = default);

    /// <summary>
    /// Travels at a commanded velocity (<c>MC_MoveVelocity</c>). The <b>sign</b> of
    /// <paramref name="velocity"/> is the direction — the only velocity form, and it takes no
    /// <see cref="Percentage"/> overload because a percentage cannot be negative (P-2).
    ///
    /// <para>
    /// The returned task completes when the <b>commanded velocity is reached</b>. The axis stays in
    /// <see cref="AxisState.ContinuousMotion"/> until <see cref="IMotionAxis.StopAsync"/>, and
    /// <paramref name="ct"/> <b>remains observed after the task completes</b>: cancelling it stops
    /// the axis.
    /// </para>
    /// </summary>
    Task MoveVelocityAsync(Speed<double, MillimetrePerSecond<double>> velocity,
                           CancellationToken ct = default);
}
