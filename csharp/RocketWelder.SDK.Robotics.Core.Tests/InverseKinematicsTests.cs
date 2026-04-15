using ModelingEvolution.Drawing;
using static RocketWelder.SDK.Robotics.Core.Tests.TestData;

namespace RocketWelder.SDK.Robotics.Core.Tests;

/// <summary>Tests 1.5, 1.6, 1.12, 1.13, 1.14, 1.15, 1.16, 1.17, 1.19, 1.20 — Inverse Kinematics.</summary>
public class InverseKinematicsTests
{
    private readonly RobotModel _model = CreateFR5();

    /// <summary>Test 1.5 — IK reports unreachable pose.</summary>
    [Fact]
    public void IK_Unreachable_Should_Fail_WithOutOfReach()
    {
        var farPose = new Pose3<double>(2000, 0, 0, 0, 0, 0);
        var result = InverseKinematics.Compute(_model, farPose, HOME);
        result.Success.Should().BeFalse();
        result.Reason.Should().Be(IkFailureReason.OutOfReach);
    }

    /// <summary>Test 1.6 — Joint limit validation (single violation).</summary>
    [Fact]
    public void ValidateJoints_SingleViolation()
    {
        // J3 limit is [-162, +162]; 200 overshoots +162 by 38.
        var joints = new Joints6<double>(0, 0, 200, 0, 0, 0);
        var violations = _model.ValidateJoints(joints);
        violations.Count.Should().Be(1);
        violations[0].JointIndex.Should().Be(2);
        violations[0].LimitDeg.Should().Be(162);
        violations[0].RequestedDeg.Should().Be(200);
        violations[0].OvershootDeg.Should().Be(38);
    }

    /// <summary>Test 1.6 — Joint limit validation (two violations).</summary>
    [Fact]
    public void ValidateJoints_TwoViolations()
    {
        // J1 [-178,178]: -200 overshoots -178 by 22.
        // J6 [-360,360]: 400 overshoots +360 by 40.
        var joints = new Joints6<double>(-200, 0, 0, 0, 0, 400);
        var violations = _model.ValidateJoints(joints);
        violations.Count.Should().Be(2);

        violations[0].JointIndex.Should().Be(0);
        violations[0].OvershootDeg.Should().Be(22);

        violations[1].JointIndex.Should().Be(5);
        violations[1].OvershootDeg.Should().Be(40);
    }

    /// <summary>Test 1.6 — Joint limit validation (per-axis boundary values pass).</summary>
    [Fact]
    public void ValidateJoints_BoundaryValues_ShouldPass()
    {
        // Each joint at its upper limit (per fairino-preset-reference.md v6 limits).
        var joints = new Joints6<double>(178, 85, 162, 85, 178, 360);
        var violations = _model.ValidateJoints(joints);
        violations.Count.Should().Be(0);
    }

    /// <summary>Test 1.12 — Singularity handling: wrist singularity (J5=0).</summary>
    [Fact]
    public void IK_WristSingularity_WithCloseSeed_ShouldSucceed()
    {
        var singularJoints = new Joints6<double>(0, -45, 45, 30, 0, -30);
        var expectedPose = new Pose3<double>(-746.5804, 102.0000, -236.9416, -90.0000, 0.0000, 0.0000);

        var state = ForwardKinematics.Compute(_model, singularJoints);
        AssertPoseEquals(expectedPose, state.TcpPose);

        var ikResult = InverseKinematics.Compute(_model, state.TcpPose, singularJoints);
        ikResult.Success.Should().BeTrue("IK should succeed with close seed at wrist singularity");

        var state2 = ForwardKinematics.Compute(_model, ikResult.Joints);
        AssertPoseEquals(state.TcpPose, state2.TcpPose, SingPosTol, SingRotTol,
            "FK round-trip at wrist singularity (relaxed tolerance)");
    }

    /// <summary>Test 1.13 — Singularity handling: elbow near fully extended.</summary>
    [Fact]
    public void IK_ElbowSingularity_WithCloseSeed_ShouldSucceed()
    {
        var nearExtendedJoints = new Joints6<double>(0, -3, 3, 0, 0, 0);
        var expectedPose = new Pose3<double>(-819.4276, 102.0000, 27.6572, -90.0000, 0.0000, 0.0000);

        var state = ForwardKinematics.Compute(_model, nearExtendedJoints);
        AssertPoseEquals(expectedPose, state.TcpPose);

        var ikResult = InverseKinematics.Compute(_model, state.TcpPose, nearExtendedJoints);
        ikResult.Success.Should().BeTrue("IK should succeed with close seed at elbow singularity");

        var state2 = ForwardKinematics.Compute(_model, ikResult.Joints);
        AssertPoseEquals(state.TcpPose, state2.TcpPose, SingPosTol, SingRotTol,
            "FK round-trip at elbow singularity (relaxed tolerance)");
    }

    /// <summary>Test 1.13 — IK at elbow singularity should be deterministic.</summary>
    [Fact]
    public void IK_ElbowSingularity_ShouldBe_Deterministic()
    {
        var nearExtendedJoints = new Joints6<double>(0, -3, 3, 0, 0, 0);
        var state = ForwardKinematics.Compute(_model, nearExtendedJoints);

        var result1 = InverseKinematics.Compute(_model, state.TcpPose, nearExtendedJoints);
        var result2 = InverseKinematics.Compute(_model, state.TcpPose, nearExtendedJoints);

        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();

        for (int i = 0; i < 6; i++)
            ((double)result1.Joints[i]).Should().BeApproximately((double)result2.Joints[i], 1e-10,
                $"Joint {i} should be deterministic");
    }

    /// <summary>Test 1.15 — IK at wrist-singular pose with far seed fails.</summary>
    [Fact]
    public void IK_WristSingularity_WithFarSeed_ShouldFail()
    {
        var singularPose = new Pose3<double>(-746.5804, 102.0000, -236.9416, -90.0000, 0.0000, 0.0000);
        var result = InverseKinematics.Compute(_model, singularPose, CFG_B);
        result.Success.Should().BeFalse("IK with far seed at singular pose should fail");
        result.Reason.Should().Be(IkFailureReason.Singularity);
    }

    /// <summary>Test 1.16 — Shoulder near-singularity with close seed.</summary>
    [Fact]
    public void IK_ShoulderSingularity_WithCloseSeed_ShouldSucceed()
    {
        var shoulderJoints = new Joints6<double>(0, -124, 71, 0, 0, 0);
        var expectedPose = new Pose3<double>(81.4747, 102.0000, -577.2553, -90.0000, -53.0000, 0.0000);

        var state = ForwardKinematics.Compute(_model, shoulderJoints);
        AssertPoseEquals(expectedPose, state.TcpPose, SingPosTol, SingRotTol);

        var ikResult = InverseKinematics.Compute(_model, state.TcpPose, shoulderJoints);
        ikResult.Success.Should().BeTrue("IK should succeed with close seed at shoulder singularity");

        var state2 = ForwardKinematics.Compute(_model, ikResult.Joints);
        AssertPoseEquals(state.TcpPose, state2.TcpPose, SingPosTol, SingRotTol);
    }

    /// <summary>Test 1.17 — IK selects the branch closest to its seed (elbow-up vs elbow-down).</summary>
    [Fact]
    public void IK_Should_Select_ClosestToSeed()
    {
        // Elbow-up seed and its FK pose.
        var elbowUp = new Joints6<double>(0, -30, 60, 0, 0, 0);
        var fkUp = ForwardKinematics.Compute(_model, elbowUp);

        // Discover an alternate IK branch ("elbow-down") that reaches the same pose
        // by seeding IK with the sign-flipped elbow angle. Under the corrected FR5 DH
        // there is no clean closed-form mirror, so we derive elbow-down from the solver.
        var mirrorSeed = new Joints6<double>(0, -30, -60, 0, 0, 0);
        var mirror = InverseKinematics.Compute(_model, fkUp.TcpPose, mirrorSeed);
        mirror.Success.Should().BeTrue("mirror-seeded IK must converge to the alternate branch");
        var elbowDown = mirror.Joints;

        // The two configs must differ substantially (different IK branch).
        var jointDistSq = 0.0;
        for (int i = 0; i < 6; i++)
            jointDistSq += Math.Pow((double)elbowUp[i] - (double)elbowDown[i], 2);
        Math.Sqrt(jointDistSq).Should().BeGreaterThan(30.0, "branches should be meaningfully apart");

        // IK seeded with elbow-up returns elbow-up.
        var ikResultUp = InverseKinematics.Compute(_model, fkUp.TcpPose, elbowUp);
        ikResultUp.Success.Should().BeTrue();
        for (int i = 0; i < 6; i++)
            AssertAngleApprox((double)ikResultUp.Joints[i], (double)elbowUp[i], 1.0,
                $"Elbow-up: joint {i}");

        // IK seeded with elbow-down returns a config close to elbow-down.
        var ikResultDown = InverseKinematics.Compute(_model, fkUp.TcpPose, elbowDown);
        ikResultDown.Success.Should().BeTrue();
        for (int i = 0; i < 6; i++)
            AssertAngleApprox((double)ikResultDown.Joints[i], (double)elbowDown[i], 1.0,
                $"Elbow-down: joint {i}");

        // Both IK results reach the same TCP position.
        var fkResultUp = ForwardKinematics.Compute(_model, ikResultUp.Joints);
        var fkResultDown = ForwardKinematics.Compute(_model, ikResultDown.Joints);
        AssertPositionEquals(fkResultUp.TcpPose, fkResultDown.TcpPose, 0.1);
    }

    /// <summary>Test 1.19 — IK converges from distant seed (non-singular).</summary>
    [Fact]
    public void IK_Should_Converge_FromDistantSeed()
    {
        var targetPose = FK_CFG_A;
        var ikResult = InverseKinematics.Compute(_model, targetPose, HOME);
        ikResult.Success.Should().BeTrue("IK should converge from HOME to CFG-A pose");

        var fkResult = ForwardKinematics.Compute(_model, ikResult.Joints);
        AssertPoseEquals(targetPose, fkResult.TcpPose, PosTol, RotTol,
            "FK of IK result should match target");

        // Joints should be within limits
        var violations = _model.ValidateJoints(ikResult.Joints);
        violations.Count.Should().Be(0, "IK result should be within joint limits");
    }

    /// <summary>Test 1.20 — Standard waypoints are IK-reachable with chained seeds.</summary>
    [Fact]
    public void StandardWaypoints_ShouldBe_IkReachable_WithChainedSeeds()
    {
        var waypoints = new[] { WP1, WP2, WP3, WP4, WP5 };
        var currentSeed = HOME;

        for (int i = 0; i < waypoints.Length; i++)
        {
            var ikResult = InverseKinematics.Compute(_model, waypoints[i], currentSeed);
            ikResult.Success.Should().BeTrue($"IK should succeed for WP{i + 1}");

            var fkResult = ForwardKinematics.Compute(_model, ikResult.Joints);
            AssertPoseEquals(waypoints[i], fkResult.TcpPose, PosTol, RotTol,
                $"FK of IK result should match WP{i + 1}");

            currentSeed = ikResult.Joints;
        }
    }
}
