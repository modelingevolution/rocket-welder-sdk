using FluentAssertions;

namespace RocketWelder.SDK.Devices.Welding.Tests;

/// <summary>
/// design.md "SDK — IWeldingMachine delta" / test-scenarios.md U-01..U-03: the wire-inch surface's
/// default interface members, exercised through an implementation that overrides none of them.
/// </summary>
public class WireInchDefaultsTests
{
    [Fact]
    public void CanWireInch_Should_Be_False_On_An_Unsupported_Welder()
    {
        // Arrange
        IWeldingMachine welder = new UnsupportedWeldingMachine();

        // Act & Assert
        welder.CanWireInch.Should().BeFalse();
    }

    [Fact]
    public async Task WireInchOn_Should_Throw_NotSupportedException_On_An_Unsupported_Welder()
    {
        // Arrange
        IWeldingMachine welder = new UnsupportedWeldingMachine();

        // Act
        var act = () => welder.WireInchOn().AsTask();

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task WireInchOff_Should_Complete_Without_Throwing_When_Unsupported_And_Disconnected()
    {
        // Arrange
        IWeldingMachine welder = new UnsupportedWeldingMachine { IsConnected = false };

        // Act
        var act = () => welder.WireInchOff().AsTask();

        // Assert
        await act.Should().NotThrowAsync();
    }
}
