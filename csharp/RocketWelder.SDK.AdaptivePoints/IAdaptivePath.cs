using RocketWelder.SDK.Devices.Robot;

namespace RocketWelder.SDK.AdaptivePoints;

/// <summary>
/// Live handle to one taught path, bound to the program's active robot and identified by its
/// start point's name. The path is the pass's ordered points — start, each intermediate point,
/// end — shifted by the endpoints' current offsets (an absent offset is zero, so the path
/// degrades to taught geometry rather than refusing to move). Obtained via
/// <c>ctx.GetAdaptivePath(startName)</c>.
///
/// <para>
/// It traverses; it does not adapt. The host resolves the polyline from the stored points and the
/// catalogue's endpoint offsets — this handle only drives the motion.
/// </para>
/// </summary>
public interface IAdaptivePath
{
    /// <summary>Name of the path's start point — its identity within the robot's catalogue.</summary>
    string StartName { get; }

    /// <summary>
    /// Drives the robot through the resolved polyline — start → each intermediate point in order →
    /// end — by straight linear moves at <paramref name="velocity"/>, honouring cancellation.
    /// Failure is a return value, never an exception: a failed or cancelled move stops the traversal
    /// at the point it reached and is reported in the result. See <see cref="TraverseReport"/>.
    /// </summary>
    Task<TraverseReport> TraverseAsync(Velocity velocity, CancellationToken ct = default);
}
