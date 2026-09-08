using NSubstitute;
using RocketWelder.SDK.Devices.Robot;
using static RocketWelder.SDK.Robotics.Core.Tests.TestData;

namespace RocketWelder.SDK.Robotics.Core.Tests;

/// <summary>
/// Contract + SimulatedRobot tests for the vendor-agnostic weave capability (Epic 091, SDK half).
/// Covers the <see cref="WeaveSwingType"/>/<see cref="WeaveInstruction"/> enums, the capability cast-probe
/// pattern the run orchestrator relies on, and SimulatedRobot's weave-state behaviour.
/// </summary>
public class WeavingRobotTests
{
    private static WeaveProfile SampleProfile(
        WeaveInstruction instruction = WeaveInstruction.Swinging,
        WeaveSwingType swingType = WeaveSwingType.VerticalLSine) =>
        new(
            SwingType: swingType,
            Instruction: instruction,
            FrequencyHz: 3.5,
            AmplitudeMm: 6,
            IncludeDwell: true,
            LeftDwellMs: 120,
            RightDwellMs: 80,
            StationaryWait: false,
            AzimuthDeg: 45,
            RollDeg: -15);

    /// <summary>U-4 — WeaveSwingType int values equal the Fairino weaveType codes; none maps to 7.</summary>
    [Fact]
    public void WeaveSwingType_IntValues_ShouldEqual_FairinoWeaveTypeCodes()
    {
        // Arrange & Act & Assert — the ints are load-bearing (== weaveType), so pin each one.
        ((int)WeaveSwingType.Triangular).Should().Be(0);
        ((int)WeaveSwingType.VerticalLTriangular).Should().Be(1);
        ((int)WeaveSwingType.CircularCw).Should().Be(2);
        ((int)WeaveSwingType.CircularCcw).Should().Be(3);
        ((int)WeaveSwingType.Sine).Should().Be(4);
        ((int)WeaveSwingType.VerticalLSine).Should().Be(5);
        ((int)WeaveSwingType.VerticalTriangle).Should().Be(6);

        var codes = Enum.GetValues<WeaveSwingType>().Select(v => (int)v);
        codes.Should().BeEquivalentTo(new[] { 0, 1, 2, 3, 4, 5, 6 });
        codes.Should().NotContain(7, because: "Fairino code 7 (second vertical sine) is not exposed by the editor");
    }

    /// <summary>U-2 — WeaveInstruction has exactly the four in-scope families; Gradient is out of scope.</summary>
    [Fact]
    public void WeaveInstruction_ShouldHave_FourFamilies_WithoutGradient()
    {
        var names = Enum.GetNames<WeaveInstruction>();

        names.Should().BeEquivalentTo("Swinging", "Simulation", "TrajectoryWarning", "FixedPoint");
        names.Should().NotContain("Gradient", because: "swing gradient is deferred (ADR-9) and has no member");
    }

    /// <summary>SimulatedRobot exposes the weave capability; a robot that lacks it probes to null (cast-probe pattern).</summary>
    [Fact]
    public void WeavingCapability_ShouldBeReachable_ByCastProbe_WhenSupported()
    {
        // Arrange
        using var weaving = new SimulatedRobot(CreateFR5());
        var nonWeaving = Substitute.For<IRobot>();

        // Act
        var weavingProbe = weaving as IWeavingRobot;
        var nonWeavingProbe = nonWeaving as IWeavingRobot;

        // Assert — same probe, opposite conditions: one supports weaving, the other does not.
        weavingProbe.Should().NotBeNull();
        nonWeavingProbe.Should().BeNull(because: "a robot that does not implement IWeavingRobot cannot weave (FR-9)");
    }

    /// <summary>BeginWeave records the active profile and turns weaving on.</summary>
    [Fact]
    public void BeginWeave_ShouldSet_ActiveProfile_AndWeavingOn()
    {
        // Arrange
        using var robot = new SimulatedRobot(CreateFR5());
        var profile = SampleProfile();

        // Act
        var code = ((IWeavingRobot)robot).BeginWeave(profile);

        // Assert
        code.Should().Be(0);
        robot.IsWeaving.Should().BeTrue();
        robot.ActiveWeaveProfile.Should().Be(profile);
    }

    /// <summary>EndWeave turns weaving off and clears the active profile.</summary>
    [Fact]
    public void EndWeave_ShouldClear_WeavingState()
    {
        // Arrange
        using var robot = new SimulatedRobot(CreateFR5());
        var weaving = (IWeavingRobot)robot;
        weaving.BeginWeave(SampleProfile());
        robot.IsWeaving.Should().BeTrue(because: "the anchor: weaving must be on before EndWeave can turn it off");

        // Act
        var code = weaving.EndWeave(WeaveInstruction.Swinging);

        // Assert
        code.Should().Be(0);
        robot.IsWeaving.Should().BeFalse();
        robot.ActiveWeaveProfile.Should().BeNull();
    }

    /// <summary>Each instruction family round-trips through the active profile carried by BeginWeave.</summary>
    [Theory]
    [InlineData(WeaveInstruction.Swinging)]
    [InlineData(WeaveInstruction.Simulation)]
    [InlineData(WeaveInstruction.TrajectoryWarning)]
    [InlineData(WeaveInstruction.FixedPoint)]
    public void BeginWeave_ShouldRoundTrip_EachInstructionFamily(WeaveInstruction family)
    {
        // Arrange
        using var robot = new SimulatedRobot(CreateFR5());
        var profile = SampleProfile(instruction: family);

        // Act
        ((IWeavingRobot)robot).BeginWeave(profile);

        // Assert — the profile the robot holds carries the family and every param unchanged.
        robot.ActiveWeaveProfile.Should().NotBeNull();
        robot.ActiveWeaveProfile!.Instruction.Should().Be(family);
        robot.ActiveWeaveProfile.Should().Be(profile);
    }

    /// <summary>BeginWeave rejects a null profile.</summary>
    [Fact]
    public void BeginWeave_ShouldThrow_OnNullProfile()
    {
        using var robot = new SimulatedRobot(CreateFR5());

        var act = () => ((IWeavingRobot)robot).BeginWeave(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
