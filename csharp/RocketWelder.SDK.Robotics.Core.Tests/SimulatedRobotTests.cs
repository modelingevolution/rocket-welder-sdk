using ModelingEvolution.Drawing;
using RocketWelder.SDK.Automation;
using static RocketWelder.SDK.Robotics.Core.Tests.TestData;

namespace RocketWelder.SDK.Robotics.Core.Tests;

/// <summary>Tests 2.0-2.19 — SimulatedRobot (IRobot implementation).</summary>
public class SimulatedRobotTests
{
    private readonly RobotModel _model = CreateFR5();

    /// <summary>Test 2.0 — Initial state at construction.</summary>
    [Fact]
    public void InitialState_ShouldBe_HomeAndDisconnected()
    {
        using var robot = new SimulatedRobot(_model);
        robot.GetJointPositions().Should().Be(Joints6<double>.Zero);
        AssertPoseEquals(FK_HOME, robot.GetActualPose());
        robot.IsConnected.Should().BeFalse();
    }

    /// <summary>Test 2.1 — MoveLin updates state.</summary>
    [Fact]
    public void MoveLin_ShouldUpdate_State()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();
        var result = ((IRobot)robot).MoveLin(WP1, DefaultVelocity);
        result.Should().Be(0);
        AssertPoseEquals(WP1, robot.GetActualPose());

        var joints = robot.GetJointPositions();
        var fk = ForwardKinematics.Compute(_model, joints);
        AssertPoseEquals(WP1, fk.TcpPose, 0.001);
    }

    /// <summary>Test 2.2 — MoveJoint updates state.</summary>
    [Fact]
    public void MoveJoint_ShouldUpdate_State()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();
        ((IRobot)robot).MoveJoint(CFG_A);
        robot.GetJointPositions().Should().Be(CFG_A);
        AssertPoseEquals(FK_CFG_A, robot.GetActualPose());
    }

    /// <summary>Test 2.3 — TryMoveLin returns structured failure.</summary>
    [Fact]
    public void TryMoveLin_Unreachable_ShouldFail()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();
        var result = robot.TryMoveLin(new Pose3<double>(2000, 0, 0, 0, 0, 0), DefaultVelocity);
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(IkFailureReason.OutOfReach);
        AssertPoseEquals(FK_HOME, robot.GetActualPose(), because: "state should be unchanged after failed move");
    }

    /// <summary>Test 2.4 — TryMoveJoint validates limits.</summary>
    [Fact]
    public void TryMoveJoint_OutOfLimits_ShouldFail()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();
        var result = robot.TryMoveJoint(new Joints6<double>(0, 0, 0, 0, 0, 200));
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(IkFailureReason.JointLimitsExceeded);
        result.Violations.Should().NotBeNull();
        result.Violations![0].JointIndex.Should().Be(5);
        result.Violations[0].OvershootDeg.Should().Be(25);
        robot.GetJointPositions().Should().Be(HOME, "state should remain at HOME");
    }

    [Fact]
    public void TryMoveJoint_Valid_ShouldSucceed()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();
        var result = robot.TryMoveJoint(CFG_A);
        result.Success.Should().BeTrue();
        robot.GetJointPositions().Should().Be(CFG_A);
    }

    /// <summary>Test 2.5 — Connect/Disconnect.</summary>
    [Fact]
    public void ConnectDisconnect_ShouldBeIdempotent()
    {
        using var robot = new SimulatedRobot(_model);
        robot.IsConnected.Should().BeFalse();

        robot.Connect();
        robot.IsConnected.Should().BeTrue();

        robot.Connect(); // idempotent
        robot.IsConnected.Should().BeTrue();

        robot.Disconnect();
        robot.IsConnected.Should().BeFalse();
    }

    /// <summary>Test 2.6 — ExecuteWaypoints.</summary>
    [Fact]
    public void ExecuteWaypoints_ShouldProduceSmooth_Sequence()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();
        var waypoints = new[] { WP1, WP2, WP3, WP4, WP5 };
        var result = robot.ExecuteWaypoints(waypoints, DefaultVelocity);

        result.Success.Should().BeTrue();
        result.Steps.Count.Should().BeGreaterThanOrEqualTo(20);

        // Check max step size
        for (int i = 1; i < result.Steps.Count; i++)
        {
            var delta = (double)result.Steps[i].Joints.MaxAbsDelta(result.Steps[i - 1].Joints);
            delta.Should().BeLessThanOrEqualTo(5.0 + 0.001,
                $"step {i} joint change should be <= 5.0 degrees");
        }

        // Final pose should match WP5
        AssertPoseEquals(WP5, result.Steps[^1].TcpPose);

        // All steps should have FK-consistent state
        foreach (var step in result.Steps)
        {
            var fk = ForwardKinematics.Compute(_model, step.Joints);
            AssertPoseEquals(step.TcpPose, fk.TcpPose, 0.001, 0.001);
        }
    }

    /// <summary>Test 2.7a — Address round-trips.</summary>
    [Fact]
    public void Address_ShouldRoundTrip()
    {
        using var robot = new SimulatedRobot(_model);
        IRobot iRobot = robot;
        iRobot.Address = new Uri("http://192.168.1.100:8080");
        iRobot.Address.Should().Be(new Uri("http://192.168.1.100:8080"));
    }

    /// <summary>Test 2.7b — JointMode round-trips.</summary>
    [Fact]
    public void JointMode_ShouldRoundTrip()
    {
        using var robot = new SimulatedRobot(_model);
        IRobot iRobot = robot;
        iRobot.JointMode = true;
        iRobot.JointMode.Should().BeTrue();
        iRobot.JointMode = false;
        iRobot.JointMode.Should().BeFalse();
    }

    /// <summary>Test 2.7c — IsAvailableAsync reflects connection state.</summary>
    [Fact]
    public async Task IsAvailableAsync_ShouldReflect_ConnectionState()
    {
        using var robot = new SimulatedRobot(_model);
        IRobot iRobot = robot;
        (await iRobot.IsAvailableAsync()).Should().BeFalse();
        iRobot.Connect();
        (await iRobot.IsAvailableAsync()).Should().BeTrue();
    }

    /// <summary>Test 2.7d — TryGetActualPose returns current pose.</summary>
    [Fact]
    public void TryGetActualPose_ShouldReturn_CurrentPose()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();
        IRobot iRobot = robot;

        iRobot.TryGetActualPose(out var pose).Should().BeTrue();
        AssertPoseEquals(FK_HOME, pose);

        iRobot.MoveLin(WP1, DefaultVelocity);
        iRobot.TryGetActualPose(out pose).Should().BeTrue();
        AssertPoseEquals(WP1, pose);
    }

    /// <summary>Test 2.7e — Robot operates normally after ResetAllErrors.</summary>
    [Fact]
    public void ResetAllErrors_ShouldAllow_NormalOperation()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();

        var failResult = robot.TryMoveLin(new Pose3<double>(2000, 0, 0, 0, 0, 0), DefaultVelocity);
        failResult.Success.Should().BeFalse();

        ((IRobot)robot).ResetAllErrors();

        ((IRobot)robot).MoveLin(WP1, DefaultVelocity);
        AssertPoseEquals(WP1, robot.GetActualPose());
    }

    /// <summary>Test 2.8 — PoseStream emits on movement.</summary>
    [Fact]
    public void PoseStream_ShouldEmit_OnMovement()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();

        var emitted = new List<Pose3<double>>();
        using var sub = robot.PoseStream.Subscribe(p => emitted.Add(p));

        ((IRobot)robot).MoveLin(WP1, DefaultVelocity);

        emitted.Count.Should().BeGreaterThanOrEqualTo(1);
        AssertPoseEquals(WP1, emitted[^1]);
    }

    /// <summary>Test 2.9 — Connected/Disconnected events.</summary>
    [Fact]
    public void Events_ShouldFire_OnConnectDisconnect()
    {
        using var robot = new SimulatedRobot(_model);
        int connectedCount = 0, disconnectedCount = 0;
        robot.Connected += (_, _) => connectedCount++;
        robot.Disconnected += (_, _) => disconnectedCount++;

        robot.Connect();
        connectedCount.Should().Be(1);
        robot.IsConnected.Should().BeTrue();

        robot.Disconnect();
        disconnectedCount.Should().Be(1);
        robot.IsConnected.Should().BeFalse();

        robot.Connect();
        robot.Disconnect();
        connectedCount.Should().Be(2);
        disconnectedCount.Should().Be(2);
    }

    /// <summary>Test 2.10 — Dispose releases resources.</summary>
    [Fact]
    public void Dispose_ShouldRelease_Resources()
    {
        var robot = new SimulatedRobot(_model);
        robot.Connect();
        robot.Dispose();

        robot.IsConnected.Should().BeFalse();

        var act = () => ((IRobot)robot).MoveLin(WP1, DefaultVelocity);
        act.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>Test 2.11 — All velocity units accepted.</summary>
    [Theory]
    [MemberData(nameof(VelocityUnits))]
    public void MoveLin_ShouldAccept_AllVelocityUnits(Velocity velocity)
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();

        var result = ((IRobot)robot).MoveLin(WP1, velocity);
        result.Should().Be(0);
        AssertPoseEquals(WP1, robot.GetActualPose());

        ((IRobot)robot).MoveJoint(HOME); // reset
    }

    /// <summary>Test 2.12 — ExecuteWaypoints with unreachable waypoint.</summary>
    [Fact]
    public void ExecuteWaypoints_Unreachable_ShouldFail()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();
        var unreachable = new Pose3<double>(2000, 0, 0, 0, 0, 0);
        var waypoints = new[] { WP1, unreachable, WP3 };
        var result = robot.ExecuteWaypoints(waypoints, DefaultVelocity);

        result.Success.Should().BeFalse();
        result.FailedWaypointIndex.Should().Be(1);
        result.Reason.Should().Be(IkFailureReason.OutOfReach);
        AssertPoseEquals(WP1, robot.GetActualPose(), because: "robot should stop at last successful waypoint");
    }

    /// <summary>Test 2.13 — Sequential MoveLin maintains consistent state.</summary>
    [Fact]
    public void SequentialMoveLin_ShouldMaintain_ConsistentState()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();

        foreach (var wp in new[] { WP1, WP2, WP3 })
        {
            ((IRobot)robot).MoveLin(wp, DefaultVelocity);
            AssertPoseEquals(wp, robot.GetActualPose());

            var joints = robot.GetJointPositions();
            var fk = ForwardKinematics.Compute(_model, joints);
            AssertPoseEquals(wp, fk.TcpPose, 0.001);
        }
    }

    /// <summary>Test 2.14 — MoveLin to current position (zero movement).</summary>
    [Fact]
    public void MoveLin_ToCurrentPosition_ShouldSucceed()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();
        ((IRobot)robot).MoveLin(WP1, DefaultVelocity);
        var jointsBefore = robot.GetJointPositions();

        ((IRobot)robot).MoveLin(WP1, DefaultVelocity); // same target
        AssertPoseEquals(WP1, robot.GetActualPose());
    }

    /// <summary>Test 1.10 — Shared RobotModel, independent state.</summary>
    [Fact]
    public void SharedModel_ShouldHave_IndependentState()
    {
        using var robot1 = new SimulatedRobot(_model);
        using var robot2 = new SimulatedRobot(_model);
        robot1.Connect();
        robot2.Connect();

        ((IRobot)robot1).MoveJoint(CFG_A);
        robot1.GetJointPositions().Should().Be(CFG_A);
        robot2.GetJointPositions().Should().Be(HOME, "robot2 should be unaffected");
    }

    /// <summary>Test 2.15 — MoveJoint with out-of-limit joints throws.</summary>
    [Fact]
    public void MoveJoint_OutOfLimits_ShouldThrow()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();
        var act = () => ((IRobot)robot).MoveJoint(new Joints6<double>(0, 0, 0, 0, 0, 200));
        act.Should().Throw<ArgumentOutOfRangeException>();
        robot.GetJointPositions().Should().Be(HOME);
    }

    /// <summary>Test 1.14 — Configuration jump prevention.</summary>
    [Fact]
    public void ExecuteWaypoints_ShouldPrevent_ConfigurationJumps()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();
        ((IRobot)robot).MoveJoint(CFG_A);

        // Build waypoints by small FK offsets
        var configs = new[]
        {
            new Joints6<double>(10, -43, 30, -15, 60, -20),
            new Joints6<double>(10, -41, 30, -15, 60, -20),
            new Joints6<double>(10, -41, 32, -15, 60, -20),
            new Joints6<double>(10, -41, 32, -13, 60, -20),
            new Joints6<double>(10, -41, 32, -13, 58, -20),
        };

        var waypoints = new Pose3<double>[configs.Length];
        for (int i = 0; i < configs.Length; i++)
        {
            var fk = ForwardKinematics.Compute(_model, configs[i]);
            waypoints[i] = fk.TcpPose;
        }

        var result = robot.ExecuteWaypoints(waypoints, DefaultVelocity);
        result.Success.Should().BeTrue();

        // No consecutive step should have > 45 deg change on any joint
        for (int i = 1; i < result.Steps.Count; i++)
        {
            var delta = (double)result.Steps[i].Joints.MaxAbsDelta(result.Steps[i - 1].Joints);
            delta.Should().BeLessThanOrEqualTo(45.0,
                $"step {i} should not have a configuration jump > 45 degrees");
        }
    }

    /// <summary>Test 2.16 — ExecuteWaypoints from non-HOME start position.</summary>
    [Fact]
    public void ExecuteWaypoints_FromNonHome_ShouldStartFromCurrentPosition()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();
        ((IRobot)robot).MoveJoint(CFG_A);

        var waypoints = new[] { WP1, WP2, WP3 };
        var result = robot.ExecuteWaypoints(waypoints, DefaultVelocity);
        result.Success.Should().BeTrue();

        result.Steps[0].Joints.Should().Be(CFG_A, "first step should be CFG-A");
        AssertPoseEquals(WP3, result.Steps[^1].TcpPose);

        foreach (var step in result.Steps)
        {
            var fk = ForwardKinematics.Compute(_model, step.Joints);
            AssertPoseEquals(step.TcpPose, fk.TcpPose, 0.001, 0.001);
        }
    }

    /// <summary>Test 2.17 — ExecuteWaypoints with single waypoint.</summary>
    [Fact]
    public void ExecuteWaypoints_SingleWaypoint_ShouldSucceed()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();
        var result = robot.ExecuteWaypoints(new[] { WP1 }, DefaultVelocity);
        result.Success.Should().BeTrue();
        result.Steps.Count.Should().BeGreaterThanOrEqualTo(1);
        AssertPoseEquals(WP1, result.Steps[^1].TcpPose);

        foreach (var step in result.Steps)
        {
            var fk = ForwardKinematics.Compute(_model, step.Joints);
            AssertPoseEquals(step.TcpPose, fk.TcpPose, 0.001, 0.001);
        }
    }

    /// <summary>Test 2.18 — MoveLin on disconnected robot throws.</summary>
    [Fact]
    public void MoveLin_WhenNotConnected_ShouldThrow()
    {
        using var robot = new SimulatedRobot(_model);
        var act = () => ((IRobot)robot).MoveLin(WP1, DefaultVelocity);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*not connected*");
        robot.GetJointPositions().Should().Be(HOME);
    }

    /// <summary>Test 2.19 — ExecuteWaypoints with empty list throws.</summary>
    [Fact]
    public void ExecuteWaypoints_EmptyList_ShouldThrow()
    {
        using var robot = new SimulatedRobot(_model);
        robot.Connect();
        var act = () => robot.ExecuteWaypoints(Array.Empty<Pose3<double>>(), DefaultVelocity);
        act.Should().Throw<ArgumentException>();
        robot.GetJointPositions().Should().Be(HOME);
    }

    public static IEnumerable<object[]> VelocityUnits()
    {
        yield return [Velocity.Percentage(50)];
        yield return [Velocity.MmPerSecond(100)];
        yield return [Velocity.CmPerMinute(500)];
    }
}
