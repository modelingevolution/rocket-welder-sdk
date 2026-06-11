namespace RocketWelder.SDK.AdaptivePoints;

/// <summary>
/// Read surface for one robot's taught paths, resolved via
/// <c>ctx.GetService&lt;IAdaptivePathService&gt;()</c> and surfaced to programs by the
/// <see cref="AdaptivePathContextExtensions.GetAdaptivePath"/> extension. Like
/// <see cref="IAdaptivePointService"/>, it is scoped to the program's active robot — callers do
/// not pass a robot id; the host bound the service to the correct robot when the program started.
///
/// <para>
/// Each handle returned by <see cref="Get"/> / <see cref="List"/> is a live
/// <see cref="IAdaptivePath"/> — call <see cref="IAdaptivePath.TraverseAsync"/> on it to move the
/// robot through the resolved polyline.
/// </para>
/// </summary>
public interface IAdaptivePathService
{
    /// <summary>The path whose start point is named <paramref name="startName"/>, or null if absent.</summary>
    IAdaptivePath? Get(string startName);

    /// <summary>All taught paths for this robot, in arbitrary order.</summary>
    IReadOnlyList<IAdaptivePath> List();
}
