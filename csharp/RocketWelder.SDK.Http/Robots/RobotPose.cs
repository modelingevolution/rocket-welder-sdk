namespace RocketWelder.SDK.Http.Robots;

/// <summary>
/// TCP pose in the robot's base frame. Wire-flat shape — the in-process server
/// projects from <c>Pose3&lt;double&gt;</c> (ModelingEvolution.Drawing.Robotics).
/// Translation in meters, rotation in radians (axis-angle ordering as configured
/// per robot driver — typically intrinsic XYZ Euler).
/// </summary>
public sealed record RobotPose(
    double X,
    double Y,
    double Z,
    double Rx,
    double Ry,
    double Rz);
