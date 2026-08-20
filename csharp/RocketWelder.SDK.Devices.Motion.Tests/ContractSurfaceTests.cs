using System.Reflection;
using FluentAssertions;

namespace RocketWelder.SDK.Devices.Motion.Tests;

/// <summary>
/// The contract's shape, pinned. Names in these enums reach an event store, a weld program and a
/// generated facade, so accidental drift — a renamed member, a quietly added kind — is a breaking
/// change that must fail here first.
/// </summary>
public class ContractSurfaceTests
{
    [Fact]
    public void AxisState_HasExactlyThePlcOpenStates()
    {
        Enum.GetNames<AxisState>().Should().Equal(
            "Disabled", "Standstill", "Homing", "DiscreteMotion",
            "ContinuousMotion", "SynchronisedMotion", "Stopping", "ErrorStop");
    }

    [Fact]
    public void AxisKind_IsTheClosedSetOfTwo()
    {
        Enum.GetNames<AxisKind>().Should().Equal("Rotary", "Linear");
    }

    [Fact]
    public void MotionDeviceKind_HasNoKindWithoutAMarker()
    {
        // Every member must be reachable from IMotionDevice.Kind, i.e. must have a marker
        // interface. "Manipulator" was dropped for exactly this reason — a kind is additive only
        // together with its marker.
        Enum.GetNames<MotionDeviceKind>().Should().Equal("Positioner", "Track");

        var markers = typeof(IMotionDevice).Assembly.GetExportedTypes()
            .Where(t => t.IsInterface && t != typeof(IMotionDevice) && typeof(IMotionDevice).IsAssignableFrom(t))
            .ToArray();

        markers.Should().HaveCount(Enum.GetValues<MotionDeviceKind>().Length);
    }

    [Fact]
    public void AxisCapabilities_IsFlagsWithTheDeclaredValues()
    {
        typeof(AxisCapabilities).GetCustomAttribute<FlagsAttribute>().Should().NotBeNull();

        ((int)AxisCapabilities.None).Should().Be(0);
        ((int)AxisCapabilities.Homing).Should().Be(1);
        ((int)AxisCapabilities.ContinuousRotation).Should().Be(2);
        ((int)AxisCapabilities.Synchronised).Should().Be(4);
        Enum.GetNames<AxisCapabilities>().Should().Equal("None", "Homing", "ContinuousRotation", "Synchronised");
    }

    [Fact]
    public void LimitSwitchState_IsFlags_SoMinAndMaxTogetherStaysVisible()
    {
        typeof(LimitSwitchState).GetCustomAttribute<FlagsAttribute>().Should().NotBeNull();

        Enum.GetNames<LimitSwitchState>().Should().Equal("None", "Min", "Max");

        var wiringFault = LimitSwitchState.Min | LimitSwitchState.Max;
        wiringFault.HasFlag(LimitSwitchState.Min).Should().BeTrue();
        wiringFault.HasFlag(LimitSwitchState.Max).Should().BeTrue();
    }

    [Fact]
    public void RotationSense_HasThreeSenses_ShortestFirst()
    {
        Enum.GetNames<RotationSense>().Should().Equal("Shortest", "Positive", "Negative");

        // Shortest is the default value, so an omitted argument is the only sense a non-wrapping
        // axis accepts.
        default(RotationSense).Should().Be(RotationSense.Shortest);
    }

    [Fact]
    public void MotionError_HasExactlyTheTwelveDeclaredReasons()
    {
        // Twelve, not thirteen: architecture.md's enum block lists twelve members. Pinning the
        // real count here rather than a remembered one is the point of the test.
        Enum.GetNames<MotionError>().Should().Equal(
            "Busy", "NotHomed", "OutOfRange", "UnreachableSpeed", "UnsupportedSense",
            "LimitTripped", "DriveFault", "CommunicationLost", "WatchdogTripped",
            "UnknownAxis", "WrongAxisKind", "LeaseHeld");
    }

    [Fact]
    public void MotionException_CarriesTheErrorAndTheAxisName()
    {
        var ex = new MotionException(MotionError.Busy, "Axis is homing.", "tilt");

        ex.Error.Should().Be(MotionError.Busy);
        ex.AxisName.Should().Be("tilt");
        ex.Message.Should().Be("Axis is homing.");
    }

    [Fact]
    public void MotionException_AxisNameIsOptional_ForDeviceLevelFailures()
    {
        var ex = new MotionException(MotionError.CommunicationLost, "Link dropped.");

        ex.AxisName.Should().BeNull();
    }

    [Fact]
    public void MotionException_LetsACallerBranchWithoutReadingTheMessage()
    {
        // AC-19, for at least Busy and UnreachableSpeed.
        MotionException[] failures =
        [
            new(MotionError.Busy, "any text at all"),
            new(MotionError.UnreachableSpeed, "any other text"),
        ];

        failures.Select(Classify).Should().Equal("retry-later", "reject-input");

        static string Classify(MotionException e) => e.Error switch
        {
            MotionError.Busy => "retry-later",
            MotionError.UnreachableSpeed => "reject-input",
            _ => "abort",
        };
    }

    [Fact]
    public void AxisStatus_IsAReadonlyRecordStructWithTheDocumentedMembers()
    {
        var t = typeof(AxisStatus);

        t.IsValueType.Should().BeTrue();
        t.GetCustomAttributes().Any(a => a.GetType().Name == "IsReadOnlyAttribute").Should().BeTrue();
        // record struct ⇒ compiler-generated value equality via the synthesised Equals/op_Equality
        t.GetMethod("op_Equality").Should().NotBeNull();

        var status = new AxisStatus(AxisState.DiscreteMotion, 45.0, -3.5, LimitSwitchState.None, null);

        status.State.Should().Be(AxisState.DiscreteMotion);
        status.Position.Should().Be(45.0);
        status.Speed.Should().Be(-3.5, "the speed is SIGNED — the sign is the direction, there is no direction field");
        status.Limits.Should().Be(LimitSwitchState.None);
        status.Error.Should().BeNull();

        status.Should().Be(new AxisStatus(AxisState.DiscreteMotion, 45.0, -3.5, LimitSwitchState.None, null));
    }

    [Fact]
    public void AxisStatus_PositionIsNullable_ForAnAxisThatDoesNotKnowWhereItIs()
    {
        var unhomed = new AxisStatus(AxisState.Standstill, null, 0, LimitSwitchState.None, null);

        unhomed.Position.Should().BeNull();
    }

    [Fact]
    public void AxisStatus_HasNoDirectionField()
    {
        // P-2: RotationDirection was deleted at both sites. Its return would silently re-introduce
        // the bug where "direction" meant two different things on an inverted axis.
        typeof(AxisStatus).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo("State", "Position", "Speed", "Limits", "Error");
    }

    [Fact]
    public void SelfCheck_IsAnOptionalCapability_NotPartOfTheMotionAxisBase()
    {
        // FR-7's direction check is a commissioning diagnostic, not a motion verb. Keeping it off
        // IMotionAxis is what keeps FR-12's block palette closed (the builder's verb list is the
        // base's own verbs) and stops every axis that cannot self-check from having to declare a
        // method it can only throw from.
        var verify = typeof(ISelfCheckingAxis).GetMethod(nameof(ISelfCheckingAxis.VerifyDirectionAsync));

        verify.Should().NotBeNull();
        verify!.ReturnType.Should().Be<Task>();

        typeof(IMotionAxis).IsAssignableFrom(typeof(ISelfCheckingAxis)).Should().BeFalse(
            "the capability must not drag the whole axis contract in with it");
        typeof(IMotionAxis).GetMethod(nameof(ISelfCheckingAxis.VerifyDirectionAsync)).Should().BeNull(
            "an axis that cannot self-check must not be forced to declare that it can");
    }

    [Fact]
    public void UnitFreeBase_DeclaresNoSpeedOrPositionMember()
    {
        // FR-2: speed bounds and typed reads live on the LEAVES. If a unit-bearing member ever
        // lands on the base, IMotionDevice.Axes stops being able to hold both kinds.
        typeof(IMotionAxis).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo("Name", "DisplayName", "State", "Capabilities", "Status", "Kind");
    }
}
