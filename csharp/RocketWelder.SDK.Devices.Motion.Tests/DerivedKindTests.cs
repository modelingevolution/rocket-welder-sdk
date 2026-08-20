using FluentAssertions;

namespace RocketWelder.SDK.Devices.Motion.Tests;

/// <summary>
/// There is exactly ONE classification mechanism at each level: the interface is the truth and the
/// enum is derived from it. These tests pin that, including the case the derivation deliberately
/// refuses — an implementation outside the contract's closed set.
/// </summary>
public class DerivedKindTests
{
    [Fact]
    public void RotaryLeaf_ReportsRotaryKind()
    {
        IMotionAxis axis = new RotaryAxisDouble();

        axis.Kind.Should().Be(AxisKind.Rotary);
    }

    [Fact]
    public void LinearLeaf_ReportsLinearKind()
    {
        IMotionAxis axis = new LinearAxisDouble();

        axis.Kind.Should().Be(AxisKind.Linear);
    }

    [Fact]
    public void AxisWithoutTypedLeaf_Throws()
    {
        IMotionAxis axis = new LeaflessAxisDouble();

        var act = () => axis.Kind;

        act.Should().Throw<NotSupportedException>()
           .WithMessage("*LeaflessAxisDouble*")
           .WithMessage("*no typed axis leaf*");
    }

    [Fact]
    public void PositionerMarker_ReportsPositionerKind()
    {
        IMotionDevice device = new PositionerDouble();

        device.Kind.Should().Be(MotionDeviceKind.Positioner);
    }

    [Fact]
    public void LinearTrackMarker_ReportsTrackKind()
    {
        IMotionDevice device = new LinearTrackDouble();

        device.Kind.Should().Be(MotionDeviceKind.Track);
    }

    [Fact]
    public void DeviceWithoutMarker_Throws()
    {
        IMotionDevice device = new MarkerlessDeviceDouble();

        var act = () => device.Kind;

        act.Should().Throw<NotSupportedException>()
           .WithMessage("*MarkerlessDeviceDouble*")
           .WithMessage("*no device marker*");
    }

    [Fact]
    public void HeterogeneousDevice_ExposesBothKindsThroughTheUnitFreeBase()
    {
        // FR-3: a column-and-boom mixes a linear and a rotary axis in one mechanism, so every
        // device-level consumer must work through IMotionAxis without knowing which is which.
        IMotionDevice device = new PositionerDouble(
            new RotaryAxisDouble { Name = "tilt" },
            new LinearAxisDouble { Name = "column" });

        device.Axes.Select(a => a.Kind).Should().Equal(AxisKind.Rotary, AxisKind.Linear);
    }
}
