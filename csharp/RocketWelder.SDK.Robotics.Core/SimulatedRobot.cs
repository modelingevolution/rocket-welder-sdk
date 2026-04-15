using System.Reactive.Linq;
using System.Reactive.Subjects;
using ModelingEvolution.Drawing;
using RocketWelder.SDK.Automation;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Mutable IRobot implementation backed by forward/inverse kinematics.
/// Each agent/user needs their own instance. Not thread-safe.
/// MoveLin/MoveJoint update internal state instantaneously (no simulated motion time).
/// </summary>
public sealed class SimulatedRobot : IRobot
{
    private const double MaxStepDegrees = 5.0;

    private readonly RobotModel _model;
    private readonly Pose3<double>? _toolTransform;
    private readonly Pose3<double>? _basePose;
    private readonly CollisionEnvironment? _collisionEnv;
    private readonly Subject<Pose3<double>> _poseSubject = new();

    private RobotState _currentState;
    private TeachingPointSet? _teachingPoints;
    private bool _isConnected;
    private bool _isDisposed;
    private Uri _address = new("sim://localhost");
    private bool _jointMode;

    /// <summary>
    /// Creates a SimulatedRobot at the model's home position. When <paramref name="environment"/>
    /// is supplied, every move checks the target configuration for self and environment collisions
    /// before committing.
    /// </summary>
    public SimulatedRobot(
        RobotModel model,
        Pose3<double>? toolTransform = null,
        Pose3<double>? basePose = null,
        CollisionEnvironment? environment = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _toolTransform = toolTransform;
        _basePose = basePose;
        _collisionEnv = environment;
        _currentState = ForwardKinematics.Compute(model, model.HomePosition, toolTransform, basePose);
    }

    /// <summary>The robot model backing this simulator.</summary>
    public RobotModel Model => _model;

    #region IRobot Implementation

    public Uri Address
    {
        get => _address;
        set => _address = value;
    }

    public bool IsConnected => _isConnected;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return Task.FromResult(_isConnected);
    }

    public int Connect()
    {
        ThrowIfDisposed();
        if (!_isConnected)
        {
            _isConnected = true;
            Connected?.Invoke(this, EventArgs.Empty);
        }
        return 0;
    }

    public void Disconnect()
    {
        ThrowIfDisposed();
        if (_isConnected)
        {
            _isConnected = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public IObservable<Pose3<double>> PoseStream => _poseSubject.AsObservable();

    public Pose3<double> GetActualPose()
    {
        ThrowIfDisposed();
        return _currentState.TcpPose;
    }

    public bool TryGetActualPose(out Pose3<double> pose)
    {
        ThrowIfDisposed();
        pose = _currentState.TcpPose;
        return true;
    }

    public Pose3<double> GetTeachingPoint(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_teachingPoints is null)
            throw new InvalidOperationException("No TeachingPointSet attached. Call AttachTeachingPoints first.");
        return _teachingPoints.Get(name);
    }

    public bool TryGetTeachingPoint(string name, out Pose3<double> pose)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_teachingPoints is null)
        {
            pose = default;
            return false;
        }
        return _teachingPoints.TryGet(name, out pose);
    }

    public Joints6<double> GetJointPositions()
    {
        ThrowIfDisposed();
        return _currentState.Joints;
    }

    public bool JointMode
    {
        get => _jointMode;
        set => _jointMode = value;
    }

    public void MoveJoint(Joints6<double> joints)
    {
        ThrowIfDisposed();
        ThrowIfNotConnected();

        var violations = _model.ValidateJoints(joints);
        if (violations.Count > 0)
            throw new ArgumentOutOfRangeException(nameof(joints),
                $"Joint angles exceed limits: joint {violations[0].JointIndex} requested {violations[0].RequestedDeg}deg, limit {violations[0].LimitDeg}deg");

        var collision = FirstCollision(joints);
        if (collision is { } hit)
            throw new InvalidOperationException(
                $"Target configuration collides: {hit.BodyA} vs {hit.BodyB} (penetration {hit.PenetrationDepth:F3} mm).");

        _currentState = ForwardKinematics.Compute(_model, joints, _toolTransform, _basePose);
        _poseSubject.OnNext(_currentState.TcpPose);
    }

    public int MoveLin(Pose3<double> target, Velocity velocity)
    {
        ThrowIfDisposed();
        ThrowIfNotConnected();

        var ikResult = InverseKinematics.Compute(_model, target, _currentState.Joints, _toolTransform, _basePose);
        if (!ikResult.Success)
            return -1; // IRobot contract: non-zero on failure

        if (FirstCollision(ikResult.Joints) is not null)
            return -2;

        _currentState = ForwardKinematics.Compute(_model, ikResult.Joints, _toolTransform, _basePose);
        _poseSubject.OnNext(_currentState.TcpPose);
        return 0;
    }

    public int MoveCircular(Pose3<double> pathPoint, Pose3<double> target, Velocity velocity) =>
        throw new NotSupportedException("MoveCircular is not supported in v1.");

    public int MoveSpline(IEnumerable<Pose3<double>> waypoints, Velocity velocity) =>
        throw new NotSupportedException("MoveSpline is not supported in v1.");

    public int ResetAllErrors()
    {
        ThrowIfDisposed();
        return 0;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _isConnected = false;
        _poseSubject.OnCompleted();
        _poseSubject.Dispose();
    }

    #endregion

    #region SimulatedRobot-specific methods

    /// <summary>
    /// Attempts to move linearly to the target pose. Returns a structured result instead of throwing.
    /// </summary>
    public MoveResult TryMoveLin(Pose3<double> target, Velocity velocity)
    {
        ThrowIfDisposed();
        ThrowIfNotConnected();

        var ikResult = InverseKinematics.Compute(_model, target, _currentState.Joints, _toolTransform, _basePose);
        if (!ikResult.Success)
            return MoveResult.Failed(ikResult.Reason!.Value.ToMoveReason(), ikResult.Violations);

        if (FirstCollision(ikResult.Joints) is { } hit)
            return MoveResult.RejectedByCollision(hit);

        _currentState = ForwardKinematics.Compute(_model, ikResult.Joints, _toolTransform, _basePose);
        _poseSubject.OnNext(_currentState.TcpPose);
        return MoveResult.Succeeded();
    }

    /// <summary>
    /// Attempts to move to the given joint angles. Returns a structured result instead of throwing.
    /// </summary>
    public MoveResult TryMoveJoint(Joints6<double> joints)
    {
        ThrowIfDisposed();
        ThrowIfNotConnected();

        var violations = _model.ValidateJoints(joints);
        if (violations.Count > 0)
            return MoveResult.Failed(MoveFailureReason.JointLimitsExceeded, violations);

        if (FirstCollision(joints) is { } hit)
            return MoveResult.RejectedByCollision(hit);

        _currentState = ForwardKinematics.Compute(_model, joints, _toolTransform, _basePose);
        _poseSubject.OnNext(_currentState.TcpPose);
        return MoveResult.Succeeded();
    }

    /// <summary>
    /// Executes a sequence of waypoints with joint-space interpolation.
    /// Each segment is divided into steps with a maximum of 5.0 degrees per joint per step.
    /// </summary>
    public SimulationRunResult ExecuteWaypoints(IReadOnlyList<Pose3<double>> waypoints, Velocity velocity)
    {
        ThrowIfDisposed();
        ThrowIfNotConnected();
        ArgumentNullException.ThrowIfNull(waypoints);
        if (waypoints.Count == 0)
            throw new ArgumentException("Waypoint list must not be empty.", nameof(waypoints));

        var steps = new List<RobotState>();
        var currentJoints = _currentState.Joints;

        // Add starting state
        steps.Add(_currentState);

        for (int wpIdx = 0; wpIdx < waypoints.Count; wpIdx++)
        {
            var targetPose = waypoints[wpIdx];
            var ikResult = InverseKinematics.Compute(_model, targetPose, currentJoints, _toolTransform, _basePose);
            if (!ikResult.Success)
            {
                // Update robot to last successful position
                if (steps.Count > 0)
                    _currentState = steps[^1];
                return SimulationRunResult.Failed(steps, wpIdx, ikResult.Reason!.Value);
            }

            var targetJoints = ikResult.Joints;
            var maxDelta = (double)currentJoints.MaxAbsDelta(targetJoints);
            var numSteps = Math.Max(1, (int)Math.Ceiling(maxDelta / MaxStepDegrees));

            for (int s = 1; s <= numSteps; s++)
            {
                var t = (double)s / numSteps;
                var interpJoints = Joints6<double>.Lerp(currentJoints, targetJoints, t);

                if (FirstCollision(interpJoints) is { } hit)
                {
                    _currentState = steps[^1];
                    return SimulationRunResult.FailedByCollision(steps, wpIdx, hit);
                }

                var state = ForwardKinematics.Compute(_model, interpJoints, _toolTransform, _basePose);
                steps.Add(state);
            }

            currentJoints = targetJoints;
        }

        // Update robot state to final position
        _currentState = steps[^1];
        _poseSubject.OnNext(_currentState.TcpPose);

        return SimulationRunResult.Succeeded(steps);
    }

    /// <summary>Attach or replace the teaching-point set used by Get/TryGet/Set/Remove teaching-point methods.</summary>
    public void AttachTeachingPoints(TeachingPointSet points)
    {
        ArgumentNullException.ThrowIfNull(points);
        ThrowIfDisposed();
        _teachingPoints = points;
    }

    /// <summary>Set (add or overwrite) a teaching point. Requires an attached set.</summary>
    public void SetTeachingPoint(string name, Pose3<double> pose)
    {
        ArgumentNullException.ThrowIfNull(name);
        ThrowIfDisposed();
        if (_teachingPoints is null)
            throw new InvalidOperationException("No TeachingPointSet attached. Call AttachTeachingPoints first.");
        _teachingPoints.Set(name, pose);
    }

    /// <summary>Remove a teaching point. Requires an attached set. Returns true if it existed.</summary>
    public bool RemoveTeachingPoint(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        ThrowIfDisposed();
        if (_teachingPoints is null)
            throw new InvalidOperationException("No TeachingPointSet attached. Call AttachTeachingPoints first.");
        return _teachingPoints.Remove(name);
    }

    /// <summary>Execute every step of the program from the beginning.</summary>
    public SimulationRunResult Execute(RobotProgram program, Velocity velocity) =>
        Execute(program, velocity, 0);

    /// <summary>Execute program steps from <paramref name="startIndex"/> to end.</summary>
    public SimulationRunResult Execute(RobotProgram program, Velocity velocity, int startIndex)
    {
        ThrowIfDisposed();
        ThrowIfNotConnected();
        ArgumentNullException.ThrowIfNull(program);
        if (startIndex < 0 || startIndex > program.Count)
            throw new ArgumentOutOfRangeException(nameof(startIndex));

        var steps = new List<RobotState> { _currentState };
        var currentJoints = _currentState.Joints;

        for (int i = startIndex; i < program.Count; i++)
        {
            var step = program.Steps[i];
            Joints6<double> targetJoints;

            switch (step)
            {
                case MoveJointStep mj:
                {
                    var violations = _model.ValidateJoints(mj.Target);
                    if (violations.Count > 0)
                    {
                        _currentState = steps[^1];
                        return SimulationRunResult.Failed(steps, i, IkFailureReason.JointLimitsExceeded);
                    }
                    targetJoints = mj.Target;
                    break;
                }
                case MoveLinStep ml:
                {
                    var ik = InverseKinematics.Compute(_model, ml.Target, currentJoints, _toolTransform, _basePose);
                    if (!ik.Success)
                    {
                        _currentState = steps[^1];
                        return SimulationRunResult.Failed(steps, i, ik.Reason!.Value);
                    }
                    targetJoints = ik.Joints;
                    break;
                }
                default:
                    throw new NotSupportedException($"Unsupported ProgramStep: {step.GetType().Name}");
            }

            var maxDelta = (double)currentJoints.MaxAbsDelta(targetJoints);
            var numSteps = Math.Max(1, (int)Math.Ceiling(maxDelta / MaxStepDegrees));
            for (int s = 1; s <= numSteps; s++)
            {
                var t = (double)s / numSteps;
                var interp = Joints6<double>.Lerp(currentJoints, targetJoints, t);

                if (FirstCollision(interp) is { } hit)
                {
                    _currentState = steps[^1];
                    return SimulationRunResult.FailedByCollision(steps, i, hit);
                }

                steps.Add(ForwardKinematics.Compute(_model, interp, _toolTransform, _basePose));
            }
            currentJoints = targetJoints;
        }

        _currentState = steps[^1];
        _poseSubject.OnNext(_currentState.TcpPose);
        return SimulationRunResult.Succeeded(steps);
    }

    private CollisionResult? FirstCollision(Joints6<double> joints)
    {
        if (_collisionEnv is null) return null;
        var hits = CollisionDetector.CheckCollision(_model, joints, _collisionEnv);
        return hits.Count == 0 ? null : hits[0];
    }

    #endregion

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private void ThrowIfNotConnected()
    {
        if (!_isConnected)
            throw new InvalidOperationException("SimulatedRobot is not connected. Call Connect() first.");
    }
}
