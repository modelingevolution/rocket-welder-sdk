namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Result of executing a waypoint sequence. Contains the simulation run data and failure info if applicable.
/// </summary>
public sealed record SimulationRunResult
{
    /// <summary>Whether all waypoints were reached successfully.</summary>
    public bool Success { get; }

    /// <summary>The simulation run data (list of RobotState snapshots). May be partial on failure.</summary>
    public IReadOnlyList<RobotState> Steps { get; }

    /// <summary>Index of the failed waypoint (0-based), or null on success.</summary>
    public int? FailedWaypointIndex { get; }

    /// <summary>IK failure reason (null on success or non-IK failure).</summary>
    public IkFailureReason? Reason { get; }

    /// <summary>Normalised failure reason across IK and collision rejections. Null on success.</summary>
    public MoveFailureReason? FailureReason { get; }

    /// <summary>Collision details when <see cref="FailureReason"/> is <see cref="MoveFailureReason.Collision"/>.</summary>
    public CollisionResult? Collision { get; }

    private SimulationRunResult(
        bool success,
        IReadOnlyList<RobotState> steps,
        int? failedWaypointIndex,
        IkFailureReason? reason,
        MoveFailureReason? failureReason,
        CollisionResult? collision)
    {
        Success = success;
        Steps = steps;
        FailedWaypointIndex = failedWaypointIndex;
        Reason = reason;
        FailureReason = failureReason;
        Collision = collision;
    }

    /// <summary>Creates a successful simulation run result.</summary>
    public static SimulationRunResult Succeeded(IReadOnlyList<RobotState> steps) =>
        new(true, steps, null, null, null, null);

    /// <summary>Creates an IK-failure simulation run result.</summary>
    public static SimulationRunResult Failed(IReadOnlyList<RobotState> steps, int failedWaypointIndex, IkFailureReason reason) =>
        new(false, steps, failedWaypointIndex, reason, reason.ToMoveReason(), null);

    /// <summary>Creates a collision-rejected simulation run result.</summary>
    public static SimulationRunResult FailedByCollision(IReadOnlyList<RobotState> steps, int failedWaypointIndex, CollisionResult collision) =>
        new(false, steps, failedWaypointIndex, null, MoveFailureReason.Collision, collision);
}
