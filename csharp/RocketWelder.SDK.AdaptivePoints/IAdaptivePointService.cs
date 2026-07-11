namespace RocketWelder.SDK.AdaptivePoints;

/// <summary>
/// Read surface for one robot's adaptive-point catalogue, resolved via
/// <c>ctx.GetService&lt;IAdaptivePointService&gt;()</c> and surfaced to programs by the
/// <see cref="AdaptivePointContextExtensions.GetAdaptivePoint"/> extension. The service is
/// scoped to the program's active robot — callers do not pass a robot id; the host bound
/// the service to the correct robot when the program started.
///
/// <para>
/// Each handle returned by <see cref="Get"/> / <see cref="List"/> / <see cref="GetByTeachPoint"/>
/// is a live <see cref="IAdaptivePoint"/> — call <see cref="IAdaptivePoint.AdaptAsync"/> on it
/// at run time to drive the camera to the master pose, detect the feature, and resolve the
/// corrected teach-point.
/// </para>
/// </summary>
public interface IAdaptivePointService
{
    /// <summary>The adaptive-point named <paramref name="name"/>, or null if absent.</summary>
    IAdaptivePoint? Get(string name);

    /// <summary>All adaptive-points captured for this robot, in arbitrary order.</summary>
    IReadOnlyList<IAdaptivePoint> List();

    /// <summary>The adaptive-point bound to the controller teach-point
    /// <paramref name="teachPointName"/>, or null if no adaptive-point points at it.</summary>
    IAdaptivePoint? GetByTeachPoint(string teachPointName);
}
