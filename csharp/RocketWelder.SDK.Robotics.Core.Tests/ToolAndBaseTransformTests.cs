using ModelingEvolution.Drawing;
using static RocketWelder.SDK.Robotics.Core.Tests.TestData;

namespace RocketWelder.SDK.Robotics.Core.Tests;

/// <summary>Tests 1.7, 1.8, 1.9, 1.22 — FK with tool transform and base pose.</summary>
public class ToolAndBaseTransformTests
{
    private readonly RobotModel _model = CreateFR5();

    /// <summary>Test 1.7 — FK with tool transform (150mm Z offset).</summary>
    [Fact]
    public void FK_WithTool_ShouldDiffer_FromWithout()
    {
        var tool = new Pose3<double>(0, 0, 150, 0, 0, 0);
        var withTool = ForwardKinematics.Compute(_model, HOME, tool);
        var withoutTool = ForwardKinematics.Compute(_model, HOME);

        // Results should differ
        var dist = Math.Sqrt(
            Math.Pow(withTool.TcpPose.X - withoutTool.TcpPose.X, 2) +
            Math.Pow(withTool.TcpPose.Y - withoutTool.TcpPose.Y, 2) +
            Math.Pow(withTool.TcpPose.Z - withoutTool.TcpPose.Z, 2));
        dist.Should().BeGreaterThan(1.0, "tool transform should change TCP position");
    }

    /// <summary>Test 1.7 — FK(HOME, tool) should displace TCP by +150mm along world Y.</summary>
    [Fact]
    public void FK_HomeWithTool_ShouldDisplace_AlongWorldY()
    {
        var tool = new Pose3<double>(0, 0, 150, 0, 0, 0);
        var expected = new Pose3<double>(-820.0100, 252.0000, 49.9000, -90.0000, 0.0000, 0.0000);
        var state = ForwardKinematics.Compute(_model, HOME, tool);
        AssertPoseEquals(expected, state.TcpPose);
    }

    /// <summary>Test 1.7 — FK(HOME, tool) -> IK -> FK round-trip.</summary>
    [Fact]
    public void FK_IK_FK_RoundTrip_WithTool_AtHome()
    {
        var tool = new Pose3<double>(0, 0, 150, 0, 0, 0);
        var state = ForwardKinematics.Compute(_model, HOME, tool);
        var ikResult = InverseKinematics.Compute(_model, state.TcpPose, HOME, tool);
        ikResult.Success.Should().BeTrue();
        var state2 = ForwardKinematics.Compute(_model, ikResult.Joints, tool);
        AssertPoseEquals(state.TcpPose, state2.TcpPose);
    }

    /// <summary>Test 1.7 — FK(CFG-A, tool) -> IK -> FK round-trip.</summary>
    [Fact]
    public void FK_IK_FK_RoundTrip_WithTool_AtCfgA()
    {
        var tool = new Pose3<double>(0, 0, 150, 0, 0, 0);
        var state = ForwardKinematics.Compute(_model, CFG_A, tool);
        var ikResult = InverseKinematics.Compute(_model, state.TcpPose, CFG_A, tool);
        ikResult.Success.Should().BeTrue();
        var state2 = ForwardKinematics.Compute(_model, ikResult.Joints, tool);
        AssertPoseEquals(state.TcpPose, state2.TcpPose);
    }

    /// <summary>Test 1.8 — FK with rotated tool transform.</summary>
    [Fact]
    public void FK_WithRotatedTool_ShouldDiffer_InPositionAndOrientation()
    {
        var tool = new Pose3<double>(50, 0, 100, 15, -10, 30);
        var withTool = ForwardKinematics.Compute(_model, CFG_A, tool);
        var withoutTool = ForwardKinematics.Compute(_model, CFG_A);

        var posDist = Math.Sqrt(
            Math.Pow(withTool.TcpPose.X - withoutTool.TcpPose.X, 2) +
            Math.Pow(withTool.TcpPose.Y - withoutTool.TcpPose.Y, 2) +
            Math.Pow(withTool.TcpPose.Z - withoutTool.TcpPose.Z, 2));
        posDist.Should().BeGreaterThan(1.0, "rotated tool should change position");

        // Orientation should also differ
        var rotDiff = Math.Abs((double)withTool.TcpPose.Rx - (double)withoutTool.TcpPose.Rx) +
                      Math.Abs((double)withTool.TcpPose.Ry - (double)withoutTool.TcpPose.Ry) +
                      Math.Abs((double)withTool.TcpPose.Rz - (double)withoutTool.TcpPose.Rz);
        rotDiff.Should().BeGreaterThan(1.0, "rotated tool should change orientation");
    }

    /// <summary>Test 1.8 — FK with rotated tool round-trips through IK.</summary>
    [Fact]
    public void FK_IK_FK_RoundTrip_WithRotatedTool()
    {
        var tool = new Pose3<double>(50, 0, 100, 15, -10, 30);
        var state = ForwardKinematics.Compute(_model, CFG_A, tool);
        var ikResult = InverseKinematics.Compute(_model, state.TcpPose, CFG_A, tool);
        ikResult.Success.Should().BeTrue();
        var state2 = ForwardKinematics.Compute(_model, ikResult.Joints, tool);
        AssertPoseEquals(state.TcpPose, state2.TcpPose);
    }

    /// <summary>Test 1.9 — FK with base pose: pure translation.</summary>
    [Fact]
    public void FK_WithBaseTranslation_ShouldOffset_Position()
    {
        var baseT = new Pose3<double>(500, 200, 0, 0, 0, 0);
        var withBase = ForwardKinematics.Compute(_model, CFG_A, basePose: baseT);
        var withoutBase = ForwardKinematics.Compute(_model, CFG_A);

        // Position should be offset by (500, 200, 0)
        withBase.TcpPose.X.Should().BeApproximately(withoutBase.TcpPose.X + 500, PosTol);
        withBase.TcpPose.Y.Should().BeApproximately(withoutBase.TcpPose.Y + 200, PosTol);
        withBase.TcpPose.Z.Should().BeApproximately(withoutBase.TcpPose.Z, PosTol);

        // Orientation should be unchanged for pure translation
        AssertAngleApprox((double)withBase.TcpPose.Rx, (double)withoutBase.TcpPose.Rx, RotTol);
        AssertAngleApprox((double)withBase.TcpPose.Ry, (double)withoutBase.TcpPose.Ry, RotTol);
        AssertAngleApprox((double)withBase.TcpPose.Rz, (double)withoutBase.TcpPose.Rz, RotTol);
    }

    /// <summary>Test 1.9 — IK round-trip with base poses.</summary>
    [Theory]
    [MemberData(nameof(BasePoses))]
    public void FK_IK_FK_RoundTrip_WithBasePose(string name, Pose3<double> basePose)
    {
        var withBase = ForwardKinematics.Compute(_model, CFG_A, basePose: basePose);
        var ikResult = InverseKinematics.Compute(_model, withBase.TcpPose, CFG_A, basePose: basePose);
        ikResult.Success.Should().BeTrue($"IK should succeed with base pose {name}");
        var state2 = ForwardKinematics.Compute(_model, ikResult.Joints, basePose: basePose);
        AssertPoseEquals(withBase.TcpPose, state2.TcpPose, because: $"round-trip with base {name}");
    }

    /// <summary>Test 1.9 — Intermediate frame 3 with base translation offset.</summary>
    [Fact]
    public void FK_IntermediateFrame3_WithBaseTranslation()
    {
        var baseT = new Pose3<double>(500, 200, 0, 0, 0, 0);
        var withBase = ForwardKinematics.Compute(_model, CFG_A, basePose: baseT);
        var withoutBase = ForwardKinematics.Compute(_model, CFG_A);

        withBase.FramePoses[2].X.Should().BeApproximately(withoutBase.FramePoses[2].X + 500, PosTol);
        withBase.FramePoses[2].Y.Should().BeApproximately(withoutBase.FramePoses[2].Y + 200, PosTol);
        withBase.FramePoses[2].Z.Should().BeApproximately(withoutBase.FramePoses[2].Z, PosTol);
    }

    /// <summary>Test 1.22 — FK with combined tool and base pose.</summary>
    [Fact]
    public void FK_WithToolAndBase_ShouldProduceCorrectResult()
    {
        var tool = new Pose3<double>(0, 0, 150, 0, 0, 0);
        var baseT = new Pose3<double>(500, 200, 0, 0, 0, 0);

        var combined = ForwardKinematics.Compute(_model, CFG_A, tool, baseT);
        var expected = new Pose3<double>(42.8149, 247.3297, -230.0585, -59.2660, -32.0812, -63.8352);
        AssertPoseEquals(expected, combined.TcpPose);

        // Verify: combined = FK(CFG-A, tool_only) + base translation
        var toolOnly = ForwardKinematics.Compute(_model, CFG_A, tool);
        combined.TcpPose.X.Should().BeApproximately(toolOnly.TcpPose.X + 500, PosTol);
        combined.TcpPose.Y.Should().BeApproximately(toolOnly.TcpPose.Y + 200, PosTol);
        combined.TcpPose.Z.Should().BeApproximately(toolOnly.TcpPose.Z, PosTol);
    }

    public static IEnumerable<object[]> BasePoses()
    {
        yield return ["Base-T", new Pose3<double>(500, 200, 0, 0, 0, 0)];
        yield return ["Base-R", new Pose3<double>(0, 0, 0, 0, 0, 90)];
        yield return ["Base-TR", new Pose3<double>(300, -100, 50, 15, -10, 45)];
    }
}
