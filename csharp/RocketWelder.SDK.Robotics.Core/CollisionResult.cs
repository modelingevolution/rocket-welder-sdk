using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Structured report of a single collision pair. Immutable.
/// </summary>
/// <param name="BodyA">Identifier of the first colliding body (e.g., "Link3", "Tool", primitive id).</param>
/// <param name="BodyB">Identifier of the second colliding body.</param>
/// <param name="PenetrationDepth">Signed overlap in millimetres. Positive means the shapes interpenetrate.</param>
/// <param name="ContactPoint">Representative contact point in robot-base coordinates (mm).</param>
public readonly record struct CollisionResult(
    string BodyA,
    string BodyB,
    double PenetrationDepth,
    Point3<double> ContactPoint);
