namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Result of a TryMoveLin or TryMoveJoint operation.
/// </summary>
public readonly record struct MoveResult
{
    /// <summary>Whether the move succeeded.</summary>
    public bool Success { get; }

    /// <summary>Failure reason (null on success).</summary>
    public MoveFailureReason? Reason { get; }

    /// <summary>Joint limit violations (populated when Reason is JointLimitsExceeded).</summary>
    public IReadOnlyList<JointLimitViolation>? Violations { get; }

    /// <summary>Collision details (populated when Reason is Collision).</summary>
    public CollisionResult? Collision { get; }

    private MoveResult(bool success, MoveFailureReason? reason,
        IReadOnlyList<JointLimitViolation>? violations, CollisionResult? collision)
    {
        Success = success;
        Reason = reason;
        Violations = violations;
        Collision = collision;
    }

    /// <summary>Creates a successful move result.</summary>
    public static MoveResult Succeeded() => new(true, null, null, null);

    /// <summary>Creates a failed move result.</summary>
    public static MoveResult Failed(MoveFailureReason reason,
        IReadOnlyList<JointLimitViolation>? violations = null,
        CollisionResult? collision = null) =>
        new(false, reason, violations, collision);

    /// <summary>Creates a collision-rejected move result.</summary>
    public static MoveResult RejectedByCollision(CollisionResult collision) =>
        new(false, MoveFailureReason.Collision, null, collision);
}
