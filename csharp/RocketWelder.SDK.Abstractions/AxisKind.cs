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
///
/// <para>
/// <b>Why this type lives in <c>RocketWelder.SDK.Abstractions</c> and not in
/// <c>RocketWelder.SDK.Devices.Motion</c> with the rest of the contract.</b>
/// It is needed by BOTH siblings: the motion contract (as the derived <c>Kind</c>) and
/// <c>AxisDeclaration</c> / <c>DeviceTypeInfo.Axes</c> in
/// <c>RocketWelder.SDK.Automation.Abstractions</c> (FR-8's plugin roster, which is typed by
/// <c>ConfigPropertySchema</c> and therefore cannot leave that package). Every
/// <c>RocketWelder.SDK.Devices.*</c> package and <c>Automation.Abstractions</c> depend on
/// <c>Abstractions</c> and on nothing of each other — <c>Automation.Abstractions</c> explicitly
/// carries "NO SDK.Devices.*". Declaring <c>AxisKind</c> in either sibling would either add a
/// forbidden sibling edge or (with <c>AxisDeclaration</c> moved) close a reference cycle.
/// The common ancestor is the only cycle-free home, and it adds no dependency to any package.
/// </para>
///
/// <para>
/// The <b>namespace</b> is deliberately the contract's own, so the published surface reads exactly
/// as <c>architecture.md</c> specifies and a future move into
/// <c>RocketWelder.SDK.Devices.Motion</c> is a <c>[TypeForwardedTo]</c> with no source change for
/// any consumer. Namespace ≠ package id is already the norm here (this package's
/// <c>ConfigPropertySchema</c> sibling lives in <c>RocketWelder.SDK.Automation</c>).
/// </para>
/// </summary>
public enum AxisKind
{
    /// <summary>A revolute axis: positions in degrees, speeds in degrees per second.</summary>
    Rotary,

    /// <summary>A prismatic axis: positions in millimetres, speeds in millimetres per second.</summary>
    Linear,
}
