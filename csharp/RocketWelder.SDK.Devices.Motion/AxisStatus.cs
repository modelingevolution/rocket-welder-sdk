namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// One reading of an axis.
///
/// <para>
/// <see cref="State"/> is a <b>snapshot at read time</b> — the live value is
/// <see cref="IMotionAxis.State"/>. <see cref="Position"/> is in the axis's own unit (° or mm, per
/// <see cref="IMotionAxis.Kind"/>) and is for <b>display and logging only</b>; every typed read
/// lives on the leaves (<see cref="IRotaryAxis.Angle"/> / <see cref="ILinearAxis.Offset"/>), so the
/// values that reach a command are typed end-to-end (D-c).
/// </para>
///
/// <para>
/// <see cref="Speed"/> is <b>signed</b>, in the axis's unit per second. There is no direction
/// field: the sign <i>is</i> the direction (P-2).
/// </para>
/// </summary>
/// <param name="State">The axis state at read time.</param>
/// <param name="Position">Position in the axis's own unit, or <see langword="null"/> when unknown
/// (e.g. an unhomed axis). Display and logging only.</param>
/// <param name="Speed">Signed speed in the axis's unit per second.</param>
/// <param name="Limits">Which hardware travel limits are active.</param>
/// <param name="Error">The latched failure reason when <paramref name="State"/> is
/// <see cref="AxisState.ErrorStop"/>; otherwise <see langword="null"/>.</param>
public readonly record struct AxisStatus(
    AxisState State,
    double? Position,
    double Speed,
    LimitSwitchState Limits,
    MotionError? Error);
