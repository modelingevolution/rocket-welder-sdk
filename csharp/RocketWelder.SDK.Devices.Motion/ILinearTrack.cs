namespace RocketWelder.SDK.Devices.Motion;

/// <summary>
/// Resolution handle for a linear track: <c>ctx.GetRequiredDevice&lt;ILinearTrack&gt;()</c>.
/// Empty on purpose — see <see cref="IPositioner"/>; the cell role <c>"track"</c> maps to this
/// handle.
/// </summary>
public interface ILinearTrack : IMotionDevice;
