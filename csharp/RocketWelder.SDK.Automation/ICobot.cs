using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Interface for robot control abstraction.
/// Provides connection management and movement commands.
/// </summary>
public interface ICobot
{
    /// <summary>
    /// Gets or sets the robot IP address.
    /// </summary>
    string IpAddress { get; set; }

    /// <summary>
    /// Gets or sets the robot communication port.
    /// </summary>
    int Port { get; set; }

    /// <summary>
    /// Gets whether the robot is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Event raised when connection to the robot is established.
    /// </summary>
    event EventHandler? Connected;

    /// <summary>
    /// Event raised when connection to the robot is lost.
    /// </summary>
    event EventHandler? Disconnected;

    /// <summary>
    /// Event raised when the axle point record button is pressed.
    /// </summary>
    event EventHandler? AxlePointRecordBtnPressed;

    /// <summary>
    /// Checks if the robot is available (network reachable).
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Connects to the robot.
    /// </summary>
    /// <returns>Error code (0 = success).</returns>
    int Connect();

    /// <summary>
    /// Disconnects from the robot.
    /// </summary>
    void Disconnect();

    /// <summary>
    /// Gets a teaching point by name from the robot controller.
    /// </summary>
    /// <param name="name">Name of the teaching point.</param>
    /// <returns>The pose of the teaching point.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the teaching point cannot be retrieved.</exception>
    Pose3<double> GetTeachingPoint(string name);

    /// <summary>
    /// Tries to get a teaching point by name from the robot controller.
    /// </summary>
    /// <param name="name">Name of the teaching point.</param>
    /// <param name="pose">The retrieved pose, or default if not found.</param>
    /// <returns>True if the teaching point was found, false otherwise.</returns>
    bool TryGetTeachingPoint(string name, out Pose3<double> pose);

    /// <summary>
    /// Gets the actual TCP (Tool Center Point) pose.
    /// </summary>
    /// <returns>The current TCP pose.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the pose cannot be retrieved.</exception>
    Pose3<double> GetActualPose();

    /// <summary>
    /// Tries to get the actual TCP (Tool Center Point) pose.
    /// </summary>
    /// <param name="pose">The retrieved pose, or default if not available.</param>
    /// <returns>True if the pose was retrieved, false otherwise.</returns>
    bool TryGetActualPose(out Pose3<double> pose);

    /// <summary>
    /// Gets the actual TCP pose from the real-time streaming state.
    /// </summary>
    /// <returns>The current streaming pose.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the pose cannot be retrieved.</exception>
    Pose3<double> GetStreamingPose();

    /// <summary>
    /// Gets the current joint positions in degrees.
    /// </summary>
    /// <returns>Array of joint positions.</returns>
    /// <exception cref="InvalidOperationException">Thrown when joint positions cannot be retrieved.</exception>
    double[] GetJointPositions();

    /// <summary>
    /// Gets the current TCP velocity (linear and angular).
    /// </summary>
    /// <returns>The current TCP velocity.</returns>
    /// <exception cref="InvalidOperationException">Thrown when velocity cannot be retrieved.</exception>
    Pose3<double> GetTcpVelocity();

    /// <summary>
    /// Moves the robot linearly (straight line in Cartesian space) to the specified pose.
    /// </summary>
    /// <param name="target">Target position and orientation (X,Y,Z in mm, RX,RY,RZ in degrees).</param>
    /// <param name="velocity">Velocity percentage [0-100], default 20%.</param>
    /// <param name="tool">Tool coordinate system [0-14], default 1.</param>
    /// <param name="user">User/workpiece coordinate system [0-14], default 1.</param>
    /// <param name="blendR">Blend radius: -1.0 = blocking (wait for completion), [0-1000] = smooth radius in mm (non-blocking).</param>
    /// <returns>Error code (0 = success).</returns>
    int MoveLin(Pose3<double> target, float velocity = 20f, int tool = 1, int user = 1, float blendR = -1.0f);

    /// <summary>
    /// Moves the robot linearly to a teaching point by name.
    /// </summary>
    /// <param name="pointName">Name of the teaching point.</param>
    /// <param name="velocity">Velocity percentage [0-100], default 20%.</param>
    /// <param name="blendR">Blend radius: -1.0 = blocking, [0-1000] = smooth radius in mm.</param>
    /// <returns>Error code (0 = success).</returns>
    /// <exception cref="InvalidOperationException">Thrown when the teaching point cannot be retrieved.</exception>
    int MoveLin(string pointName, float velocity = 20f, float blendR = -1.0f);

    /// <summary>
    /// Moves the robot in Cartesian space to the specified pose.
    /// </summary>
    /// <param name="target">Target position and orientation (X,Y,Z in mm, RX,RY,RZ in degrees).</param>
    /// <param name="velocity">Velocity percentage [0-100], default 20%.</param>
    /// <param name="tool">Tool coordinate system [0-14], default 1.</param>
    /// <param name="user">User/workpiece coordinate system [0-14], default 1.</param>
    /// <param name="blendT">Blend time: -1.0 = blocking (wait for completion), [0-500] = smooth time in ms (non-blocking).</param>
    /// <returns>Error code (0 = success).</returns>
    int MoveCart(Pose3<double> target, float velocity = 20f, int tool = 1, int user = 1, float blendT = -1.0f);

    /// <summary>
    /// Moves the robot in a circular arc through a path point to a target point.
    /// </summary>
    /// <param name="pathPoint">Intermediate point on the arc (defines the curve).</param>
    /// <param name="target">Target/end point of the arc.</param>
    /// <param name="velocity">Velocity percentage [0-100], default 20%.</param>
    /// <param name="tool">Tool coordinate system [0-14], default 1.</param>
    /// <param name="user">User/workpiece coordinate system [0-14], default 1.</param>
    /// <param name="blendR">Blend radius: -1.0 = blocking (wait for completion), [0-1000] = smooth radius in mm (non-blocking).</param>
    /// <returns>Error code (0 = success).</returns>
    int MoveCircular(Pose3<double> pathPoint, Pose3<double> target, float velocity = 20f, int tool = 1, int user = 1, float blendR = -1.0f);

    /// <summary>
    /// Starts a spline trajectory segment.
    /// Call this before executing multiple movement commands that should be blended together.
    /// </summary>
    /// <returns>Error code (0 = success).</returns>
    int SplineStart();

    /// <summary>
    /// Ends a spline trajectory segment.
    /// Call this after executing all movement commands within the spline segment.
    /// </summary>
    /// <returns>Error code (0 = success).</returns>
    int SplineEnd();

    /// <summary>
    /// Switches the active point table on the robot controller.
    /// </summary>
    /// <param name="pointTableName">Name of the point table file.</param>
    /// <returns>Error code (0 = success).</returns>
    int SwitchPointTable(string pointTableName);

    /// <summary>
    /// Resets all robot errors.
    /// </summary>
    /// <returns>Error code (0 = success).</returns>
    int ResetAllErrors();
}
