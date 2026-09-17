using System.Reflection;
using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Devices.Robot.Tests;

/// <summary>
/// Pins the shape of <see cref="INonBlockingPoseRobot"/> (epic-107 design §1, M-4). Every host adapter that
/// implements it binds to THIS declaration — a rename, an added member, or a change to the try-pattern signature is
/// a breaking change and must be deliberate. The point of the capability is that
/// <see cref="IRobot.TryGetActualPose"/> keeps its "after the motion ended" behaviour, so the two must stay
/// separate members.
/// </summary>
public sealed class NonBlockingPoseRobotContractTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Fact]
    public void INonBlockingPoseRobot_DeclaresExactlyTryGetActualPoseNow()
    {
        var methods = typeof(INonBlockingPoseRobot).GetMethods(Declared)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        methods.Should().Equal("TryGetActualPoseNow");
        typeof(INonBlockingPoseRobot).GetProperties(Declared).Should().BeEmpty();
        typeof(INonBlockingPoseRobot).GetEvents(Declared).Should().BeEmpty();
    }

    [Fact]
    public void TryGetActualPoseNow_IsATryPatternOverPose3()
    {
        var read = typeof(INonBlockingPoseRobot).GetMethod("TryGetActualPoseNow", Declared)!;
        var parameters = read.GetParameters();

        read.ReturnType.Should().Be(typeof(bool));
        parameters.Should().ContainSingle();
        parameters[0].IsOut.Should().BeTrue();
        parameters[0].ParameterType.Should().Be(typeof(Pose3<double>).MakeByRefType());
    }

    [Fact]
    public void INonBlockingPoseRobot_IsAnIRobot()
    {
        // Design §1: the non-blocking read is an extra on a robot, not a standalone device surface.
        typeof(IRobot).IsAssignableFrom(typeof(INonBlockingPoseRobot)).Should().BeTrue();
    }

    [Fact]
    public void ItDoesNotRedeclareTheBlockingRead()
    {
        // M-4: TryGetActualPose (flag 0, "after the motion ended") is left alone for its 57 call sites.
        typeof(INonBlockingPoseRobot).GetMethod("TryGetActualPose", Declared).Should().BeNull();
        typeof(INonBlockingPoseRobot).GetMethod("GetActualPose", Declared).Should().BeNull();
    }
}
