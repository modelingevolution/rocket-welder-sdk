namespace RocketWelder.SDK.Http.Robots;

/// <summary>
/// Named teaching point stored on a robot. Wire shape; the server projects from
/// the SDK's <c>TeachingPoint</c> domain record.
/// </summary>
public sealed record TeachingPointInfo(
    string Name,
    RobotPose Pose);
