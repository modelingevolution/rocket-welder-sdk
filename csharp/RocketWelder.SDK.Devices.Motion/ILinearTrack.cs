namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// Resolution handle for a linear track: <c>ctx.GetRequiredDevice&lt;ILinearTrack&gt;()</c>.
/// Empty on purpose — see <see cref="IPositioner"/>; the cell role <c>"linearTrack"</c> maps to this
/// handle — the role is <b>derived</b> from this interface name (drop a leading <c>I</c>, lower-case the
/// first character), not chosen, so a plugin must never write <c>"track"</c>: it would match nothing.
/// </summary>
public interface ILinearTrack : IMotionDevice;
