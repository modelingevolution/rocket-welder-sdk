using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Immutable snapshot of a robot's kinematic state. Thread-safe, pure data.
/// Created by FK computation.
/// </summary>
public sealed record RobotState(
    Joints6<double> Joints,
    Pose3<double> TcpPose,
    IReadOnlyList<Pose3<double>> FramePoses,
    DateTimeOffset Timestamp
)
{
    /// <summary>
    /// Creates a RobotState with the current timestamp.
    /// </summary>
    public static RobotState Create(Joints6<double> joints, Pose3<double> tcpPose, IReadOnlyList<Pose3<double>> framePoses) =>
        new(joints, tcpPose, framePoses is Pose3<double>[] arr ? Array.AsReadOnly(arr) : framePoses, DateTimeOffset.UtcNow);
}
