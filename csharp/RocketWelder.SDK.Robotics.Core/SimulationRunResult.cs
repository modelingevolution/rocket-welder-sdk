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

    /// <summary>Failure reason, or null on success.</summary>
    public IkFailureReason? Reason { get; }

    private SimulationRunResult(bool success, IReadOnlyList<RobotState> steps,
        int? failedWaypointIndex, IkFailureReason? reason)
    {
        Success = success;
        Steps = steps;
        FailedWaypointIndex = failedWaypointIndex;
        Reason = reason;
    }

    /// <summary>Creates a successful simulation run result.</summary>
    public static SimulationRunResult Succeeded(IReadOnlyList<RobotState> steps) =>
        new(true, steps, null, null);

    /// <summary>Creates a failed simulation run result.</summary>
    public static SimulationRunResult Failed(IReadOnlyList<RobotState> steps, int failedWaypointIndex, IkFailureReason reason) =>
        new(false, steps, failedWaypointIndex, reason);
}
