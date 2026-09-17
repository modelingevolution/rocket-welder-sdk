using System.Reflection;
using FluentAssertions;
using ModelingEvolution.Signals;

namespace RocketWelder.SDK.Devices.Welding.Tests;

/// <summary>
/// Pins the shape of <see cref="ITouchSensingWelder"/> (epic-107 design §1, ADR-2). The Fronius adapter and the
/// program runtime are in other repositories and both bind to THIS declaration, so a rename, a dropped member or a
/// changed signature is a breaking change and must be deliberate. ADR-2's own point — readiness, arm, disarm, fresh
/// read, watch and hold-off are methods owned by the adapter, not a pair of signals — is what the member list pins.
/// </summary>
public sealed class TouchSensingWelderContractTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Fact]
    public void ITouchSensingWelder_IsAWeldingMachine()
    {
        // Probed by cast off IWeldingMachine — capability, never a device-type-name check.
        typeof(IWeldingMachine).IsAssignableFrom(typeof(ITouchSensingWelder)).Should().BeTrue();
    }

    [Fact]
    public void ItDeclaresExactlyTheFiveCapabilityMethods()
    {
        var methods = typeof(ITouchSensingWelder).GetMethods(Declared)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        methods.Should().Equal(
            "ArmTouchSensingAsync",
            "DisarmTouchSensingAsync",
            "ReadTouchAsync",
            "RequestTouchSensingOff",
            "WaitForTouchAsync");
    }

    [Fact]
    public void ItDeclaresExactlyTheThreeCapabilityProperties()
    {
        var properties = typeof(ITouchSensingWelder).GetProperties(Declared)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        properties.Should().Equal("LastSampleTimestamp", "TouchSensingHoldOffRemaining", "TouchSignal");
        typeof(ITouchSensingWelder).GetEvents(Declared).Should().BeEmpty();
    }

    [Theory]
    [InlineData("ArmTouchSensingAsync")]
    [InlineData("ReadTouchAsync")]
    [InlineData("WaitForTouchAsync")]
    public void TheBoundedCallsTakeATimeoutAndAToken(string name)
    {
        var method = typeof(ITouchSensingWelder).GetMethod(name, Declared)!;

        method.GetParameters().Select(p => p.ParameterType)
            .Should().Equal(typeof(TimeSpan), typeof(CancellationToken));
    }

    [Fact]
    public void TheReadsReturnTheContactState()
    {
        typeof(ITouchSensingWelder).GetMethod("ReadTouchAsync", Declared)!.ReturnType.Should().Be(typeof(Task<bool>));
        typeof(ITouchSensingWelder).GetMethod("WaitForTouchAsync", Declared)!.ReturnType.Should().Be(typeof(Task<bool>));
        typeof(ITouchSensingWelder).GetMethod("ArmTouchSensingAsync", Declared)!.ReturnType.Should().Be(typeof(Task));
    }

    [Fact]
    public void DisarmIsAwaitableAndTakesOnlyAToken()
    {
        // Bounded to 500 ms and never throws, so a probe's finally can await it unconditionally.
        var disarm = typeof(ITouchSensingWelder).GetMethod("DisarmTouchSensingAsync", Declared)!;

        disarm.GetParameters().Select(p => p.ParameterType).Should().Equal(typeof(CancellationToken));
        disarm.ReturnType.Should().Be(typeof(Task));
    }

    [Fact]
    public void RequestTouchSensingOff_IsASynchronousParameterlessLatch()
    {
        // ADR-11 rev 5 / FR-2.12: a STOP must never wait on a fieldbus write, so this returns nothing to await.
        var request = typeof(ITouchSensingWelder).GetMethod("RequestTouchSensingOff", Declared)!;

        request.GetParameters().Should().BeEmpty();
        request.ReturnType.Should().Be(typeof(void));
    }

    [Fact]
    public void LastSampleTimestamp_IsALockFreeStopwatchTickReadOnlyValue()
    {
        // A caller's own watchdog reads it independently of every waiter, so it must be a plain getter.
        var property = typeof(ITouchSensingWelder).GetProperty("LastSampleTimestamp", Declared)!;

        property.PropertyType.Should().Be(typeof(long));
        property.CanWrite.Should().BeFalse();
    }

    [Fact]
    public void HoldOffRemaining_IsAReadOnlyTimeSpan()
    {
        var property = typeof(ITouchSensingWelder).GetProperty("TouchSensingHoldOffRemaining", Declared)!;

        property.PropertyType.Should().Be(typeof(TimeSpan));
        property.CanWrite.Should().BeFalse();
    }

    [Fact]
    public void TouchSignal_IsReadOnlyAndNotWritable()
    {
        // UI/catalog surface only: writing it would be the signal-as-command mistake ADR-2 rejects.
        var property = typeof(ITouchSensingWelder).GetProperty("TouchSignal", Declared)!;

        property.PropertyType.Should().Be(typeof(ISignal<bool>));
        property.CanWrite.Should().BeFalse();
        typeof(WritableSignal<bool>).IsAssignableFrom(property.PropertyType).Should().BeFalse();
    }

    [Fact]
    public void NoMemberHasADefaultImplementation()
    {
        // Every member is a promise only the adapter can keep; a default would silently descend with sensing off.
        var members = typeof(ITouchSensingWelder).GetMethods(Declared);

        members.Should().OnlyContain(m => m.IsAbstract);
    }
}
