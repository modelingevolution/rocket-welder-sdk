using RocketWelder.SDK.Abstractions;
using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Vendor-agnostic interface for robot control.
/// Implementations: FairinoCobot, AbbEgmRobot, etc.
/// </summary>
public interface IRobot : IDevice
{
    // === Connection ===

    /// <summary>Robot endpoint address. Scheme encodes the protocol:
    /// <list type="bullet">
    /// <item>Fairino: <c>tcp://192.168.8.99:8080</c></item>
    /// <item>ABB EGM: <c>egm://192.168.8.99:6510</c> (host = robot controller IP
    ///   used by IsAvailableAsync; port = local UDP listen port for EGM)</item>
    /// </list></summary>
    Uri Address { get; set; }

    /// <summary>Whether the robot is connected and ready.</summary>
    bool IsConnected { get; }

    /// <summary>Checks if the robot is reachable on the network.
    /// Uses <see cref="Address"/> host to probe the robot controller:
    /// Fairino checks TCP on Address.Port, ABB checks HTTP on port 80.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Connects to the robot using <see cref="Address"/>.
    /// For blocking implementations (Fairino): returns after connection is established.
    /// For streaming implementations (ABB EGM): returns immediately after starting the
    /// UDP listener — actual connection is confirmed asynchronously via the
    /// <see cref="Connected"/> event when the first robot message arrives and MCI enters
    /// RUNNING state.</summary>
    /// <returns>0 always. Connection failure is reported via state properties and events,
    /// not return codes.</returns>
    int Connect();

    /// <summary>Disconnects from the robot.</summary>
    void Disconnect();

    /// <summary>Raised when connection is established.</summary>
    event EventHandler? Connected;

    /// <summary>Raised when connection is lost.</summary>
    event EventHandler? Disconnected;

    // === Position Feedback ===

    /// <summary>Hot observable of current TCP pose in robot base frame.
    /// Emits at the robot's native feedback rate (e.g., ~250 Hz for ABB EGM).</summary>
    IObservable<Pose3<double>> PoseStream { get; }

    /// <summary>Gets the current TCP pose in robot base frame.</summary>
    Pose3<double> GetActualPose();

    /// <summary>Tries to get the current TCP pose.</summary>
    bool TryGetActualPose(out Pose3<double> pose);

    /// <summary>Gets a teaching point by name from the robot controller.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the teaching point cannot be retrieved.</exception>
    Pose3<double> GetTeachingPoint(string name);

    /// <summary>Tries to get a teaching point by name from the robot controller.</summary>
    bool TryGetTeachingPoint(string name, out Pose3<double> pose);

    /// <summary>Gets the current joint angles in degrees.</summary>
    Joints6<double> GetJointPositions();

    // === Teaching ===

    /// <summary>Enter teaching mode. Vendor-specific — e.g. Fairino enables pendant
    /// Axle-Record button polling so <see cref="TeachingPointAdded"/> fires on each press.</summary>
    void StartTeaching();

    /// <summary>Leave teaching mode.</summary>
    void EndTeaching();

    /// <summary>Snapshot of all teach-points currently stored in the controller's DB.</summary>
    IReadOnlyList<TeachingPoint> PeekTeachingPoints();

    /// <summary>Raised when the operator records a new teach-point on the controller
    /// (pendant press, or other vendor-native mechanism), while in teaching mode.</summary>
    event EventHandler<TeachingPoint>? TeachingPointAdded;

    // === Motion ===

    /// <summary>
    /// When true, the robot uses joint-space guidance instead of Cartesian.
    /// Must be set before <see cref="Connect"/> for protocols that need to know the mode
    /// from the first message (e.g., ABB EGM with EGMRunJoint in RAPID).
    /// Default is false (Cartesian mode).
    /// </summary>
    bool JointMode { get; set; }

    /// <summary>
    /// Moves the robot by sending absolute joint angles.
    /// Non-blocking for streaming robots (ABB EGM), blocking for direct robots (Fairino).
    /// </summary>
    /// <param name="joints">6 joint angles.</param>
    void MoveJoint(Joints6<double> joints);

    /// <summary>
    /// Moves the robot linearly to the target pose.
    /// Behavior is implementation-specific:
    /// - Blocking robots (Fairino): waits until motion completes
    /// - Streaming robots (ABB EGM): sets target for continuous guidance (non-blocking)
    /// </summary>
    /// <param name="target">Target pose (X,Y,Z in mm, Rx,Ry,Rz in degrees).</param>
    /// <param name="velocity">Motion velocity with explicit unit.
    /// Fairino: uses Percentage or converts mm/s via max speed.
    /// ABB EGM: ignored — speed is controlled by MaxSpeedDeviation in RAPID.</param>
    int MoveLin(Pose3<double> target, Velocity velocity);

    /// <summary>
    /// Moves the robot in a circular arc through pathPoint to target.
    /// </summary>
    int MoveCircular(Pose3<double> pathPoint, Pose3<double> target, Velocity velocity);

    /// <summary>
    /// Moves through waypoints using smooth spline motion.
    /// BLOCKING: This method blocks the calling thread until all waypoints have been reached.
    /// Implementation is vendor-specific:
    /// - Fairino: Uses SplineStart/SplineEnd with blended MoveLin (blocks until done)
    /// - ABB EGM: Streams waypoints sequentially, calling WaitForConvergence() after each
    /// </summary>
    /// <exception cref="TimeoutException">ABB EGM: if convergence is not reached within 30s per waypoint.</exception>
    int MoveSpline(IEnumerable<Pose3<double>> waypoints, Velocity velocity);

    // === Error Handling ===

    /// <summary>Resets all robot errors.</summary>
    int ResetAllErrors();
}
