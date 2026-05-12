namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Details of a single joint limit violation.
/// </summary>
public readonly record struct JointLimitViolation(
    int JointIndex,
    double LimitDeg,
    double RequestedDeg,
    double OvershootDeg
);
