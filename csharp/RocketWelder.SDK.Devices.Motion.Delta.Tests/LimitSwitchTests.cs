namespace RocketWelder.SDK.Devices.Motion.Delta.Tests;

/// <summary>
/// The travel limits: how the normally-closed inputs are read, what the contract says about the
/// impossible reading, and when a limit stops a move versus when it must not.
///
/// <para>
/// <c>architecture.md</c>: "<c>Min|Max</c> together = wiring fault, reported not masked". The whole
/// point of the flags shape is that the impossible reading stays <b>visible</b> — collapsing it to
/// one side, or to <see cref="LimitSwitchState.None"/>, would hide a broken machine behind a
/// plausible-looking status.
/// </para>
/// </summary>
public class LimitSwitchTests
{
    // Tilt's limit inputs, from the machine's wiring: X5 lower, X6 upper.
    private const int LowerInput = 5;
    private const int UpperInput = 6;

    [Fact]
    public async Task InputsAreNormallyClosed_SoAQuietAxisReportsNoLimits()
    {
        using var bed = await AxisTestBed.HomedAsync(DeltaPositionerDefaults.Tilt);

        (await bed.Axis.ReadStatusAsync()).Limits.Should().Be(LimitSwitchState.None);
    }

    [Theory]
    [InlineData(LowerInput, LimitSwitchState.Min)]
    [InlineData(UpperInput, LimitSwitchState.Max)]
    public async Task AZeroOnALimitInputIsATrippedLimit(int input, LimitSwitchState expected)
    {
        // Normally closed: 0 means tripped. Getting this inversion backwards reports a healthy axis
        // as resting on both limits and a tripped one as clear.
        using var bed = await AxisTestBed.HomedAsync(DeltaPositionerDefaults.Tilt,
            arrange: d => d.Inputs[input] = false);

        (await bed.Axis.ReadStatusAsync()).Limits.Should().Be(expected);
    }

    [Fact]
    public async Task BothLimitsAtOnceIsReportedAsBoth_NotMaskedToOneSideOrToNone()
    {
        // A machine cannot rest on both ends of its travel. That reading is a wiring fault, and the
        // contract requires it to survive to the caller intact.
        using var bed = await AxisTestBed.HomedAsync(DeltaPositionerDefaults.Tilt, arrange: d =>
        {
            d.Inputs[LowerInput] = false;
            d.Inputs[UpperInput] = false;
        });

        var limits = (await bed.Axis.ReadStatusAsync()).Limits;

        limits.Should().Be(LimitSwitchState.Min | LimitSwitchState.Max);
        limits.Should().HaveFlag(LimitSwitchState.Min);
        limits.Should().HaveFlag(LimitSwitchState.Max);
        limits.Should().NotBe(LimitSwitchState.None, "the impossible reading must stay visible");
    }

    [Fact]
    public async Task AnAxisWithoutLimitSwitchesReportsNone_WhateverThoseInputsHappenToSay()
    {
        // X5/X6 carry nothing on the turntable, so reading a limit off them would invent one.
        using var bed = await AxisTestBed.HomedAsync(DeltaPositionerDefaults.Turntable, arrange: d =>
        {
            d.Inputs[LowerInput] = false;
            d.Inputs[UpperInput] = false;
        });

        (await bed.Axis.ReadStatusAsync()).Limits.Should().Be(LimitSwitchState.None);
    }

    [Fact]
    public async Task DrivingIntoATrippedLimitFaultsWithLimitTripped()
    {
        // AC-19's other named error, on the path that actually raises it. The tilt axis is mounted
        // inverted, so a NEGATIVE angular velocity drives DOWN — into the lower limit.
        using var bed = await AxisTestBed.HomedAsync(DeltaPositionerDefaults.Tilt,
            arrange: d => d.Inputs[LowerInput] = false);

        await bed.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(-5));

        var faulted = await WaitForStateAsync(bed.Axis, AxisState.ErrorStop);

        faulted.Should().BeTrue("the supervisor is the only thing watching the limits during a jog");
        bed.Axis.Status.Error.Should().Be(MotionError.LimitTripped);
    }

    [Fact]
    public async Task RetreatingFromATrippedLimitIsAllowed()
    {
        // The exemption that makes recovery possible at all: a limit only stops motion heading INTO
        // it. Without this an axis resting on a switch could never be driven off it, and the machine
        // would need a human with a crank.
        using var bed = await AxisTestBed.HomedAsync(DeltaPositionerDefaults.Tilt,
            arrange: d => d.Inputs[LowerInput] = false);

        // Positive angle = away from the lower limit.
        await bed.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(5));

        var faulted = await WaitForStateAsync(bed.Axis, AxisState.ErrorStop);

        faulted.Should().BeFalse("moving away from a tripped limit is exactly how you leave it");
        bed.Axis.State.Should().Be(AxisState.ContinuousMotion);
        await bed.Axis.StopAsync();
    }

    [Fact]
    public async Task TheUpperLimitBehavesTheSameWayRoundTheOtherWay()
    {
        // The mirror image, so neither direction is passing by accident.
        using var into = await AxisTestBed.HomedAsync(DeltaPositionerDefaults.Tilt,
            arrange: d => d.Inputs[UpperInput] = false);
        await into.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(5));
        (await WaitForStateAsync(into.Axis, AxisState.ErrorStop)).Should().BeTrue();
        into.Axis.Status.Error.Should().Be(MotionError.LimitTripped);

        using var away = await AxisTestBed.HomedAsync(DeltaPositionerDefaults.Tilt,
            arrange: d => d.Inputs[UpperInput] = false);
        await away.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(-5));
        (await WaitForStateAsync(away.Axis, AxisState.ErrorStop)).Should().BeFalse();
        await away.Axis.StopAsync();
    }

    [Fact]
    public async Task AFaultedLimitIsLeftOnlyByReset()
    {
        using var bed = await AxisTestBed.HomedAsync(DeltaPositionerDefaults.Tilt,
            arrange: d => d.Inputs[LowerInput] = false);

        await bed.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(-5));
        (await WaitForStateAsync(bed.Axis, AxisState.ErrorStop)).Should().BeTrue();

        var act = () => bed.Axis.MoveVelocityAsync(AxisTestBed.DegPerSecond(5));
        (await act.Should().ThrowAsync<MotionException>()).Which.Error.Should().Be(MotionError.Busy);

        await bed.Axis.ResetAsync();
        bed.Axis.State.Should().Be(AxisState.Standstill);
        bed.Axis.Status.Error.Should().BeNull();
    }

    /// <summary>
    /// Waits for the supervisor to notice, or reports that it did not. Polling a state rather than
    /// sleeping a guessed interval, so the test says what it is waiting for.
    /// </summary>
    private static async Task<bool> WaitForStateAsync(IMotionAxis axis, AxisState wanted)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (axis.State == wanted) return true;
            await Task.Delay(25);
        }

        return axis.State == wanted;
    }
}
