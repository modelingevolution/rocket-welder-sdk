using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Pluggable backend supplying environment geometry for collision detection.
/// Headless default: <see cref="PrimitiveCollisionSource"/>. CAD/CAM sources resolve mesh-id tools.
/// </summary>
public interface ICollisionSource
{
    /// <summary>
    /// Reports all environment and tool-vs-environment collisions for the given robot pose.
    /// </summary>
    /// <param name="model">Robot kinematic model (for FK).</param>
    /// <param name="joints">Joint configuration in degrees.</param>
    /// <param name="linkRadii">Per-link capsule radii (mm); 6 entries.</param>
    /// <param name="tool">Tool geometry attached to the flange.</param>
    /// <param name="safetyMargin">Non-negative inflation in mm applied to every primitive and to the tool/link surface.</param>
    /// <returns>A list of collision reports. Empty when no contact.</returns>
    IReadOnlyList<CollisionResult> QueryCollision(
        RobotModel model,
        Joints6<double> joints,
        IReadOnlyList<double> linkRadii,
        ToolModel tool,
        double safetyMargin);
}
