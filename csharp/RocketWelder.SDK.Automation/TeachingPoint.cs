using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// A teach-point recorded on the robot controller: a named TCP pose.
/// Produced by the operator via the controller's pendant during a teaching session
/// and surfaced through <see cref="IRobot"/>'s teaching members (added separately).
/// </summary>
/// <param name="Name">The teach-point's name in the controller's DB (vendor-assigned, e.g. Fairino number-suffix).</param>
/// <param name="Pose">The TCP pose at the teach-point, in robot-base coordinates.</param>
public sealed record TeachingPoint(string Name, Pose3<double> Pose);
