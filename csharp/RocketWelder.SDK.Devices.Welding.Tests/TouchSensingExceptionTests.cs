using FluentAssertions;

namespace RocketWelder.SDK.Devices.Welding.Tests;

/// <summary>
/// The exception types of the <see cref="ITouchSensingWelder"/> capability (epic-107 design §1, §3 "Exception →
/// text"). A host maps each to its own operator wording, and tells the two losses apart by
/// <see cref="TouchSensingLostException.Reason"/> rather than by comparing message text written in this package.
/// </summary>
public sealed class TouchSensingExceptionTests
{
    [Fact]
    public void AllThreeShareOneBase()
    {
        // So a teardown path can catch "touch sensing failed" once.
        typeof(TouchSensingNotReadyException).Should().BeDerivedFrom<TouchSensingException>();
        typeof(TouchSignalStaleException).Should().BeDerivedFrom<TouchSensingException>();
        typeof(TouchSensingLostException).Should().BeDerivedFrom<TouchSensingException>();
    }

    [Fact]
    public void TheThreeAreDistinctTypes_NotOneWithAReasonCode()
    {
        // The probe picks a different operator text and a different recovery for each, so they must be catchable apart.
        typeof(TouchSignalStaleException).Should().NotBeAssignableTo<TouchSensingNotReadyException>();
        typeof(TouchSensingLostException).Should().NotBeAssignableTo<TouchSensingNotReadyException>();
        typeof(TouchSensingLostException).Should().NotBeAssignableTo<TouchSignalStaleException>();
    }

    [Fact]
    public void NotReady_CarriesTheVendorsReasonSeparatelyFromTheMessage()
    {
        var ex = new TouchSensingNotReadyException("Robot ready is low");

        ex.Reason.Should().Be("Robot ready is low");
        ex.Message.Should().Be("Robot ready is low");
    }

    [Fact]
    public void NotReady_ReasonIsNeverNull()
    {
        new TouchSensingNotReadyException().Reason.Should().NotBeNull();
    }

    [Fact]
    public void Lost_DefaultsToSwitchedOff()
    {
        // FR-2.12: the common case is another fieldbus master clearing the command bit.
        new TouchSensingLostException().Reason.Should().Be(TouchSensingLostReason.SwitchedOff);
        new TouchSensingLostException("cleared").Reason.Should().Be(TouchSensingLostReason.SwitchedOff);
    }

    [Fact]
    public void Lost_CarriesDeviceChangedWithoutRelyingOnMessageText()
    {
        // FR-2.16 / EC-28: the adapter faults its waiters with this on Dispose, and the host reads the enum.
        var ex = new TouchSensingLostException(TouchSensingLostReason.DeviceChanged);

        ex.Reason.Should().Be(TouchSensingLostReason.DeviceChanged);
        ex.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Stale_HasADefaultMessage()
    {
        new TouchSignalStaleException().Message.Should().NotBeNullOrWhiteSpace();
    }
}
