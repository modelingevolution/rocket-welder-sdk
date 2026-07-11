using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Reason for IK failure.
/// </summary>
public enum IkFailureReason
{
    /// <summary>Target is beyond the robot's kinematic reach.</summary>
    OutOfReach,

    /// <summary>A solution exists but violates joint limits.</summary>
    JointLimitsExceeded,

    /// <summary>The Jacobian is ill-conditioned (singularity detected).</summary>
    Singularity,

    /// <summary>The solver did not converge within the maximum number of iterations.</summary>
    NoConvergence
}

/// <summary>
/// Result of an inverse kinematics computation. Either success with joint angles, or failure with reason.
/// </summary>
public readonly record struct IkResult
{
    /// <summary>Whether the computation succeeded.</summary>
    public bool Success { get; }

    /// <summary>The joint angles that reach the target pose (only valid when Success is true).</summary>
    public Joints6<double> Joints { get; }

    /// <summary>The failure reason (only valid when Success is false).</summary>
    public IkFailureReason? Reason { get; }

    /// <summary>Joint limit violations (only populated when Reason is JointLimitsExceeded).</summary>
    public IReadOnlyList<JointLimitViolation>? Violations { get; }

    private IkResult(bool success, Joints6<double> joints, IkFailureReason? reason, IReadOnlyList<JointLimitViolation>? violations)
    {
        Success = success;
        Joints = joints;
        Reason = reason;
        Violations = violations;
    }

    /// <summary>Creates a successful IK result.</summary>
    public static IkResult Succeeded(Joints6<double> joints) =>
        new(true, joints, null, null);

    /// <summary>Creates a failed IK result.</summary>
    public static IkResult Failed(IkFailureReason reason, IReadOnlyList<JointLimitViolation>? violations = null) =>
        new(false, Joints6<double>.Zero, reason, violations);
}
