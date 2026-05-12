namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Reason why a move was rejected. Superset of <see cref="IkFailureReason"/> plus collision.
/// </summary>
public enum MoveFailureReason
{
    /// <summary>Target is beyond the robot's kinematic reach.</summary>
    OutOfReach,

    /// <summary>A solution exists but violates joint limits.</summary>
    JointLimitsExceeded,

    /// <summary>The Jacobian is ill-conditioned (singularity detected).</summary>
    Singularity,

    /// <summary>The solver did not converge within the maximum number of iterations.</summary>
    NoConvergence,

    /// <summary>The target configuration collides with self or the environment.</summary>
    Collision
}

/// <summary>Conversion helpers between <see cref="IkFailureReason"/> and <see cref="MoveFailureReason"/>.</summary>
public static class MoveFailureReasonExtensions
{
    public static MoveFailureReason ToMoveReason(this IkFailureReason reason) => reason switch
    {
        IkFailureReason.OutOfReach => MoveFailureReason.OutOfReach,
        IkFailureReason.JointLimitsExceeded => MoveFailureReason.JointLimitsExceeded,
        IkFailureReason.Singularity => MoveFailureReason.Singularity,
        IkFailureReason.NoConvergence => MoveFailureReason.NoConvergence,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown IkFailureReason")
    };
}
