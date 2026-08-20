using FluentAssertions;

namespace RocketWelder.SDK.Devices.Motion.Tests;

/// <summary>
/// The string accessors exist for generated code only (FR-10) — which is exactly why their failure
/// modes must be machine-readable: this is the runtime residue of an error the facade catches at
/// compile time.
/// </summary>
public class MotionDeviceExtensionsTests
{
    private static readonly IMotionDevice Device = new PositionerDouble(
        new RotaryAxisDouble { Name = "tilt" },
        new LinearAxisDouble { Name = "column" });

    [Fact]
    public void Rotary_BindsTheDeclaredRotaryAxis()
    {
        Device.Rotary("tilt").Name.Should().Be("tilt");
    }

    [Fact]
    public void Linear_BindsTheDeclaredLinearAxis()
    {
        Device.Linear("column").Name.Should().Be("column");
    }

    [Fact]
    public void Rotary_OnALinearAxis_ThrowsWrongAxisKind()
    {
        var act = () => Device.Rotary("column");

        act.Should().Throw<MotionException>()
           .Which.Should().Match<MotionException>(e => e.Error == MotionError.WrongAxisKind && e.AxisName == "column");
    }

    [Fact]
    public void Linear_OnARotaryAxis_ThrowsWrongAxisKind()
    {
        var act = () => Device.Linear("tilt");

        act.Should().Throw<MotionException>()
           .Which.Should().Match<MotionException>(e => e.Error == MotionError.WrongAxisKind && e.AxisName == "tilt");
    }

    [Fact]
    public void UnknownName_ThrowsUnknownAxis()
    {
        var act = () => Device.Rotary("turntable");

        act.Should().Throw<MotionException>()
           .Which.Should().Match<MotionException>(e => e.Error == MotionError.UnknownAxis && e.AxisName == "turntable");
    }

    [Fact]
    public void Indexer_UnknownName_ThrowsUnknownAxis()
    {
        var act = () => Device["nope"];

        act.Should().Throw<MotionException>()
           .Which.Error.Should().Be(MotionError.UnknownAxis);
    }
}
