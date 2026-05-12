using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Modified Denavit-Hartenberg (Craig convention) parameters for a single joint.
/// Transform: T_i = Rot_x(alpha_{i-1}) * Trans_x(a_{i-1}) * Rot_z(theta_i) * Trans_z(d_i)
/// </summary>
public readonly record struct DhJoint(
    double Alpha, // alpha_{i-1} in radians (twist angle from previous link)
    double A,     // a_{i-1} in mm (link length from previous link)
    double D,     // d_i in mm (link offset along Z)
    double ThetaOffset // theta offset in radians (added to joint variable)
)
{
    /// <summary>
    /// Creates a DhJoint from parameters in degrees/mm (the common datasheet format).
    /// </summary>
    public static DhJoint FromDegrees(double alphaDeg, double aMm, double dMm, double thetaOffsetDeg) =>
        new(alphaDeg * Math.PI / 180.0, aMm, dMm, thetaOffsetDeg * Math.PI / 180.0);
}
