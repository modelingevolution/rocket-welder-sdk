using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Finds the robot TCP pose that aims the sensor beam at a target surface.
///
/// <para>
/// Sibling of <see cref="RocketWelder.SDK.Vision.ICameraViewLocator"/> for the 1-D distance sensor.
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
    /// <para>
    /// To sample multiple points on the same plane (e.g. the auto-probe's center +
    /// along-travel + perpendicular triple per FR-3.1), the caller composes translated
    /// planes via <c>plane + Vector3</c> rather than passing a separate offset argument.
    /// </para>
    /// <para>
    /// <b>Roll-about-beam.</b> Aiming the beam pins two rotational degrees of freedom
    /// (azimuth + elevation); the third — roll about the beam axis — is determined by
    /// the implementation's chosen orthonormal-frame convention. Two calls with planes
    /// that differ only by translation may produce TCP poses with the same azimuth /
    /// elevation but different rolls if the impl uses a non-deterministic rotation
    /// selector (e.g. <c>Vector3.RotationTo</c>'s shortest-arc rotation, which is
    /// geometrically valid but depends on the starting axis). Implementations that need
    /// stable roll across multi-sample probing (FR-3.1) must pin a third axis explicitly
    /// — see the camera-side precedent in rocket-welder2
    /// (<c>StandoffViewLocator.cs:86-97</c>: world-down up-vector with degeneracy
    /// fallback to target-local axis). Callers that re-pose the robot between probe
    /// samples are sensitive to this: arbitrary roll changes between samples can cause
    /// wrist-flip motions on the way from sample N to N+1.
    /// </para>
    /// </remarks>
    /// <param name="plane">Target surface (convention: XY plane of the pose, +Z is the normal).</param>
    Pose3<double> GetTcpViewFor(Pose3<double> plane);
}
