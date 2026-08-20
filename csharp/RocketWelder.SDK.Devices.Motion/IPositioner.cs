namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// Resolution handle for a workpiece positioner: <c>ctx.GetRequiredDevice&lt;IPositioner&gt;()</c>
/// binds without strings, because a cell holds at most one motion device <i>per kind</i>
/// (an explicit non-goal of epic-065).
///
/// <para>
/// Empty on purpose. The marker <b>is</b> the device's classification — a plugin declares the kind
/// by the marker its factory's device implements, and the cell role maps to the handle
/// (<c>"positioner"</c> → <see cref="IPositioner"/>). Device identity is the cell role, never the
/// vendor discriminator, so swapping vendors while redeclaring the same axis names does not break a
/// stored program (FR-9 / D-e).
/// </para>
/// </summary>
public interface IPositioner : IMotionDevice;
