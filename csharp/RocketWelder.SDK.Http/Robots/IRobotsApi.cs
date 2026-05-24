namespace RocketWelder.SDK.Http.Robots;

/// <summary>
/// <c>/api/robots/{name?}/...</c> — read-only access to a registered
/// <c>IRobot</c>. Backed by the welder's <c>DeviceRegistry</c> server-side.
/// </summary>
/// <remarks>
/// <para>
/// Per epic-035 design §9 (safety boundary), there are no write operations
/// here — moves go through compiled <c>IProgram</c> plugins, never through
/// direct tools.
/// </para>
/// <para>
/// The optional <c>name</c> route segment lets callers target a specific robot
/// when more than one is registered; omitted, the server uses the default
/// (single) robot.
/// </para>
/// </remarks>
public interface IRobotsApi
{
    /// <summary>
    /// <c>GET /api/robots/{name?}/pose</c> — current TCP pose. Returns null when
    /// the robot is disconnected.
    /// </summary>
    Task<RobotPose?> GetPoseAsync(string? name = null, CancellationToken ct = default);

    /// <summary>
    /// <c>GET /api/robots/{name?}/joints</c> — current joint positions. Returns
    /// null when the robot is disconnected.
    /// </summary>
    Task<RobotJoints?> GetJointsAsync(string? name = null, CancellationToken ct = default);

    /// <summary>
    /// <c>GET /api/robots/{name?}/teaching-points</c> — named teaching points
    /// stored on the robot. Empty list when none have been taught.
    /// </summary>
    Task<IReadOnlyList<TeachingPointInfo>> GetTeachingPointsAsync(string? name = null, CancellationToken ct = default);
}
