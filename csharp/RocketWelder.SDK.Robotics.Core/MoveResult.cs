using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Result of a TryMoveLin or TryMoveJoint operation.
/// </summary>
public readonly record struct MoveResult
{
    /// <summary>Whether the move succeeded.</summary>
    public bool Success { get; }

    /// <summary>Failure reason (null on success).</summary>
    public IkFailureReason? Reason { get; }

    /// <summary>Joint limit violations (populated when Reason is JointLimitsExceeded).</summary>
    public IReadOnlyList<JointLimitViolation>? Violations { get; }

    private MoveResult(bool success, IkFailureReason? reason, IReadOnlyList<JointLimitViolation>? violations)
    {
        Success = success;
        Reason = reason;
        Violations = violations;
    }

    /// <summary>Creates a successful move result.</summary>
    public static MoveResult Succeeded() => new(true, null, null);

    /// <summary>Creates a failed move result.</summary>
    public static MoveResult Failed(IkFailureReason reason, IReadOnlyList<JointLimitViolation>? violations = null) =>
        new(false, reason, violations);
}
