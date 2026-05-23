using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Finds the robot TCP pose that aims the sensor beam at a target surface.
///
/// <para>
/// Sibling of <see cref="AdaptivePoints.ICameraViewLocator"/> for the 1-D distance sensor.
/// Bound to its owning <see cref="IDistanceSensor"/> at construction (so the standoff is
/// derived from <see cref="IDistanceSensor.TargetDistanceMM"/> — no caller-supplied
/// distance argument); consumers obtain the instance via <see cref="IDistanceSensor.Locator"/>.
/// </para>
/// </summary>
public interface IDistanceSensorViewLocator
{
    /// <summary>
    /// TCP pose that aims the beam perpendicular to <paramref name="plane"/>, hitting
    /// the plane at <c>plane.Position</c>, at standoff = <see cref="IDistanceSensor.TargetDistanceMM"/>.
    /// Drive the result with <see cref="IRobot.MoveLin"/>.
    /// </summary>
    /// <remarks>
    /// To sample multiple points on the same plane (e.g. the auto-probe's center +
    /// along-travel + perpendicular triple per FR-3.1), the caller composes translated
    /// planes via <c>plane + Vector3</c> rather than passing a separate offset argument.
    /// </remarks>
    /// <param name="plane">Target surface (convention: XY plane of the pose, +Z is the normal).</param>
    Pose3<double> GetTcpViewFor(Pose3<double> plane);
}
