using ModelingEvolution.Drawing;
using static RocketWelder.SDK.Robotics.Core.Tests.TestData;

namespace RocketWelder.SDK.Robotics.Core.Tests;

/// <summary>Tests 1.2, 1.3, 1.4, 1.18, 1.21 — Forward Kinematics.</summary>
public class ForwardKinematicsTests
{
    private readonly RobotModel _model = CreateFR5();

    /// <summary>Test 1.2 — FK at home position.</summary>
    [Fact]
    public void FK_Home_Should_Produce_Correct_TcpPose()
    {
        var state = ForwardKinematics.Compute(_model, HOME);
        AssertPoseEquals(FK_HOME, state.TcpPose);
    }

    /// <summary>Test 1.2 — FK -> IK -> FK round-trip at HOME.</summary>
    [Fact]
    public void FK_IK_FK_RoundTrip_AtHome()
    {
        var state = ForwardKinematics.Compute(_model, HOME);
        var ikResult = InverseKinematics.Compute(_model, state.TcpPose, HOME);
        ikResult.Success.Should().BeTrue();

        var state2 = ForwardKinematics.Compute(_model, ikResult.Joints);
        AssertPoseEquals(state.TcpPose, state2.TcpPose);
    }

    /// <summary>Test 1.3 — FK at CFG-A.</summary>
    [Fact]
    public void FK_CFGA_Should_Produce_Correct_TcpPose()
    {
        var state = ForwardKinematics.Compute(_model, CFG_A);
        AssertPoseEquals(FK_CFG_A, state.TcpPose);
    }

    /// <summary>Test 1.3 — FK at CFG-D.</summary>
    [Fact]
    public void FK_CFGD_Should_Produce_Correct_TcpPose()
    {
        var state = ForwardKinematics.Compute(_model, CFG_D);
        AssertPoseEquals(FK_CFG_D, state.TcpPose);
    }

    /// <summary>Test 1.3 — FK -> IK -> FK round-trip at CFG-A.</summary>
    [Fact]
    public void FK_IK_FK_RoundTrip_AtCfgA()
    {
        var state = ForwardKinematics.Compute(_model, CFG_A);
        var ikResult = InverseKinematics.Compute(_model, state.TcpPose, CFG_A);
        ikResult.Success.Should().BeTrue();

        var state2 = ForwardKinematics.Compute(_model, ikResult.Joints);
        AssertPoseEquals(state.TcpPose, state2.TcpPose);
    }

    /// <summary>Test 1.4 — FK ground-truth at all standard configurations.</summary>
    [Theory]
    [MemberData(nameof(AllConfigurations))]
    public void FK_GroundTruth_AllConfigs(string name, Joints6<double> joints, Pose3<double> expected)
    {
        var state = ForwardKinematics.Compute(_model, joints);
        AssertPoseEquals(expected, state.TcpPose, because: $"FK at {name}");
    }

    /// <summary>Test 1.4 — FK -> IK -> FK round-trip at all standard configurations.</summary>
    [Theory]
    [MemberData(nameof(AllConfigurations))]
    public void FK_IK_FK_RoundTrip_AllConfigs(string name, Joints6<double> joints, Pose3<double> _)
    {
        var state = ForwardKinematics.Compute(_model, joints);
        var ikResult = InverseKinematics.Compute(_model, state.TcpPose, joints);
        ikResult.Success.Should().BeTrue($"IK should succeed for {name}");

        // Check IK joints are close to original
        for (int i = 0; i < 6; i++)
            AssertAngleApprox((double)ikResult.Joints[i], (double)joints[i], JointTol,
                $"Joint {i} for {name}");

        var state2 = ForwardKinematics.Compute(_model, ikResult.Joints);
        AssertPoseEquals(state.TcpPose, state2.TcpPose, because: $"FK round-trip at {name}");
    }

    /// <summary>Test 1.18 — Euler angle decomposition uses ZYX convention.</summary>
    [Fact]
    public void EulerDecomposition_Should_Use_ZYX_Convention()
    {
        // Build R = Rz(45) * Ry(30) * Rx(15)
        var pose = new Pose3<double>(0, 0, 0, 15, 30, 45);
        var matrix = ForwardKinematics.PoseToMatrix(pose);
        var decomposed = ForwardKinematics.MatrixToPose(matrix);

        AssertAngleApprox((double)decomposed.Rx, 15.0, RotTol, "Rx should be 15");
        AssertAngleApprox((double)decomposed.Ry, 30.0, RotTol, "Ry should be 30");
        AssertAngleApprox((double)decomposed.Rz, 45.0, RotTol, "Rz should be 45");

        // Verify it's NOT XYZ order (which would give different values)
        // If someone used XYZ, Rx would be 45, not 15
        Math.Abs((double)decomposed.Rx - 45.0).Should().BeGreaterThan(1.0, "Must not be XYZ convention");
    }

    /// <summary>Test 1.21 — FK produces correct intermediate frame poses.</summary>
    [Fact]
    public void FK_Home_Should_Produce_Correct_IntermediateFrames()
    {
        var state = ForwardKinematics.Compute(_model, HOME);

        // Frame 3 (elbow) at HOME
        state.FramePoses[2].X.Should().BeApproximately(-425.0, PosTol, "Frame 3 X at HOME");
        state.FramePoses[2].Y.Should().BeApproximately(0.0, PosTol, "Frame 3 Y at HOME");
        state.FramePoses[2].Z.Should().BeApproximately(152.0, PosTol, "Frame 3 Z at HOME");
    }

    [Fact]
    public void FK_CfgA_Should_Produce_Correct_IntermediateFrame3()
    {
        var state = ForwardKinematics.Compute(_model, CFG_A);

        state.FramePoses[2].X.Should().BeApproximately(-295.9548, PosTol, "Frame 3 X at CFG-A");
        state.FramePoses[2].Y.Should().BeApproximately(-52.1848, PosTol, "Frame 3 Y at CFG-A");
        state.FramePoses[2].Z.Should().BeApproximately(-148.5204, PosTol, "Frame 3 Z at CFG-A");
    }

    [Fact]
    public void FK_Should_Populate_All_7_FramePoses()
    {
        var state = ForwardKinematics.Compute(_model, CFG_A);
        state.FramePoses.Count.Should().Be(7, "6 link frames + TCP");

        for (int i = 0; i < 7; i++)
        {
            double.IsNaN(state.FramePoses[i].X).Should().BeFalse($"Frame {i} X should not be NaN");
            double.IsNaN(state.FramePoses[i].Y).Should().BeFalse($"Frame {i} Y should not be NaN");
            double.IsNaN(state.FramePoses[i].Z).Should().BeFalse($"Frame {i} Z should not be NaN");
        }
    }

    [Fact]
    public void FK_Frame6_Should_Match_TcpPose_WhenNoTool()
    {
        var state = ForwardKinematics.Compute(_model, CFG_A);
        AssertPoseEquals(FK_CFG_A, state.FramePoses[6], because: "Frame 6 should match TCP when no tool");
    }

    public static IEnumerable<object[]> AllConfigurations()
    {
        yield return ["HOME", HOME, FK_HOME];
        yield return ["CFG-A", CFG_A, FK_CFG_A];
        yield return ["CFG-B", CFG_B, FK_CFG_B];
        yield return ["CFG-C", CFG_C, FK_CFG_C];
        yield return ["CFG-D", CFG_D, FK_CFG_D];
        yield return ["CFG-E", CFG_E, FK_CFG_E];
    }
}
