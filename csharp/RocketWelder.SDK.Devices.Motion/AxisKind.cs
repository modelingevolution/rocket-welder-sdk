namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// The two axis kinds of the motion contract's closed set — mirroring revolute / prismatic
/// (see epic-065 <c>architecture.md</c> §"The contract").
///
/// <para>
/// <b>Derived, never stored.</b> The typed leaf interface is the primary classification:
/// <c>IRotaryAxis</c> ⇒ <see cref="Rotary"/>, <c>ILinearAxis</c> ⇒ <see cref="Linear"/>.
/// <c>IMotionAxis.Kind</c> computes this enum from the leaf; an implementation never declares
/// it independently, so a kind that contradicts the interface is unrepresentable.
/// </para>
/// </summary>
public enum AxisKind
{
    /// <summary>A revolute axis: positions in degrees, speeds in degrees per second.</summary>
    Rotary,

    /// <summary>A prismatic axis: positions in millimetres, speeds in millimetres per second.</summary>
    Linear,
}
