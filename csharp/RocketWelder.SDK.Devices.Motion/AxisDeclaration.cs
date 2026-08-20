using RocketWelder.SDK.Automation;

namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// One entry of a motion device's <b>axis roster</b> (FR-8): the plugin declares, in code, which
/// axes its device type has, what kind each is, and what per-installation values the Add-device
/// dialog must collect for it. Carried by <see cref="MotionDeviceTypeInfo.Axes"/>.
///
/// <para>
/// <b>The roster is code, not configuration.</b> A hub-owned roster would be a miniature of the
/// unversioned-ladder problem — fleet integrity by discipline. Declared here it gets review, diff
/// and versioning for free, and fleet identity becomes structural: every <c>delta-positioner-2r</c>
/// anywhere has <c>tilt</c> and <c>turntable</c> by construction. The accepted cost is that a new
/// machine shape is a plugin release, not a UI action.
/// </para>
///
/// <para>
/// The hub stores <b>values only</b> — drive IP, PG ratio, limits, per-machine calibration — keyed
/// by the declared <paramref name="Name"/>. It never stores structure.
/// </para>
/// </summary>
/// <param name="Name">The frozen axis identifier — role-based and vendor-neutral (<c>tilt</c>,
/// <c>turntable</c>; never <c>delta-a</c>). Weld programs, automation programs, the generated facade
/// and the hub all key on it, so it is never editable from a station. The identical-cells guarantee
/// across a vendor swap depends on the replacement plugin redeclaring the same names.</param>
/// <param name="Kind">Whether the axis is rotary or linear. The dialog and the builder's inspector
/// read the unit (° / mm) from this rather than from a constant (AC-15).</param>
/// <param name="PropertySchemas">The per-installation values the Add-device dialog renders as this
/// axis's own section. This is the type <c>DeviceTypeInfo</c> already uses for device-level config —
/// FR-8's "no new schema mechanism".</param>
public sealed record AxisDeclaration(
    string Name,
    AxisKind Kind,
    ConfigPropertySchema[] PropertySchemas);
