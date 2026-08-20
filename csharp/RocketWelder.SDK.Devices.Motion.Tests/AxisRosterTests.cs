using FluentAssertions;
using RocketWelder.SDK.Abstractions;
using RocketWelder.SDK.Automation;

namespace RocketWelder.SDK.Devices.Motion.Tests;

/// <summary>
/// FR-8 — the axis roster is declared by the plugin, <b>in code</b>: git-versioned, reviewable as a
/// diff, and structural rather than a per-station configuration. The hub stores values keyed by
/// these names; it never stores the structure.
///
/// <para>
/// The roster rides on <see cref="MotionDeviceTypeInfo"/>, a subclass of the registry's
/// <see cref="DeviceTypeInfo"/>. These tests pin both halves of that arrangement: the roster works,
/// and a plain device type is entirely unaffected by it.
/// </para>
/// </summary>
public class AxisRosterTests
{
    private static MotionDeviceTypeInfo Positioner() => new(
        DeviceType: "delta-positioner-2r",
        InterfaceType: nameof(IPositioner),
        DisplayName: "Delta 2-axis positioner",
        InterfaceClrType: typeof(IPositioner),
        PropertySchemas: [],
        Factory: (_, _) => new object());

    private static DeviceTypeInfo Camera() => new(
        DeviceType: "acme-camera",
        InterfaceType: "ICamera",
        DisplayName: "ACME camera",
        InterfaceClrType: typeof(IDevice),
        PropertySchemas: [],
        Factory: (_, _) => new object());

    [Fact]
    public void MotionDeviceTypeInfo_IsADeviceTypeInfo_SoTheRegistryStoresItUnchanged()
    {
        // The subclass is what keeps Automation.Abstractions untouched: the registry, the plugin
        // contract and every non-motion plugin are unaware the roster exists.
        Positioner().Should().BeAssignableTo<DeviceTypeInfo>();
    }

    [Fact]
    public void ANonMotionDeviceType_HasNoRosterAtAll()
    {
        // Not "an empty roster" — no Axes member. Whether a device type has axes is a type test.
        typeof(DeviceTypeInfo).GetProperty("Axes").Should().BeNull();

        (Camera() is MotionDeviceTypeInfo).Should().BeFalse();
    }

    [Fact]
    public void AConsumerFindsTheRosterByPatternMatching()
    {
        DeviceTypeInfo[] registry = [Camera(), Positioner() with { Axes = [new AxisDeclaration("tilt", AxisKind.Rotary, [])] }];

        var declared = registry
            .OfType<MotionDeviceTypeInfo>()
            .SelectMany(m => m.Axes)
            .Select(a => a.Name);

        declared.Should().Equal("tilt");
    }

    [Fact]
    public void MotionDeviceTypeInfo_ConstructorMirrorsTheBaseConstructorExactly()
    {
        // MotionDeviceTypeInfo re-declares DeviceTypeInfo's whole parameter list in order to
        // forward it. That duplication is invisible to the compiler: adding a 10th parameter to
        // DeviceTypeInfo would leave this subclass silently NOT forwarding it, and every motion
        // plugin would quietly lose the new field. Pin the parity so that becomes a failed test.
        var baseCtor = LongestConstructorOf(typeof(DeviceTypeInfo));
        var derivedCtor = LongestConstructorOf(typeof(MotionDeviceTypeInfo));

        Signature(derivedCtor).Should().Equal(Signature(baseCtor),
            "MotionDeviceTypeInfo must forward DeviceTypeInfo's constructor parameter-for-parameter, "
            + "in the same order and with the same optionality");

        static System.Reflection.ConstructorInfo LongestConstructorOf(Type t) =>
            t.GetConstructors().MaxBy(c => c.GetParameters().Length)!;

        static (string Type, string Name, bool Optional)[] Signature(System.Reflection.ConstructorInfo c) =>
            c.GetParameters()
             .Select(p => (Type: p.ParameterType.ToString(), p.Name!, p.IsOptional))
             .ToArray();
    }

    [Fact]
    public void MotionDeviceTypeInfo_ForwardsEveryBaseValue()
    {
        // Parity of the signature is not parity of the forwarding — the arguments could be passed
        // in the wrong order and still typecheck where types repeat (DetailView and
        // ParameterEditorView are both Type?). Check the values actually land.
        var detail = typeof(string);
        var editor = typeof(int);
        var schemas = new[] { new ConfigPropertySchema("Ip", "Drive IP", "ip", Required: true) };

        var info = new MotionDeviceTypeInfo(
            DeviceType: "delta-positioner-2r",
            InterfaceType: nameof(IPositioner),
            DisplayName: "Delta 2-axis positioner",
            InterfaceClrType: typeof(IPositioner),
            PropertySchemas: schemas,
            Factory: (_, _) => new object(),
            GetSignals: null,
            DetailView: detail,
            ParameterEditorView: editor);

        info.DeviceType.Should().Be("delta-positioner-2r");
        info.InterfaceType.Should().Be(nameof(IPositioner));
        info.DisplayName.Should().Be("Delta 2-axis positioner");
        info.InterfaceClrType.Should().Be(typeof(IPositioner));
        info.PropertySchemas.Should().BeSameAs(schemas);
        info.Factory.Should().NotBeNull();
        info.DetailView.Should().Be(detail);
        info.ParameterEditorView.Should().Be(editor, "DetailView and ParameterEditorView must not be swapped");
    }

    [Fact]
    public void MotionDeviceTypeInfo_WithoutAxes_HasAnEmptyRoster()
    {
        Positioner().Axes.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void MotionDeviceTypeInfo_DeclaresItsAxesInOrder()
    {
        var info = Positioner() with
        {
            Axes =
            [
                new AxisDeclaration("tilt", AxisKind.Rotary,
                    [new ConfigPropertySchema("PgRatio", "PG ratio", "double", Required: true, Group: "tilt")]),
                new AxisDeclaration("turntable", AxisKind.Rotary,
                    [new ConfigPropertySchema("PgRatio", "PG ratio", "double", Required: true, Group: "turntable")]),
            ],
        };

        info.Axes.Select(a => a.Name).Should().Equal("tilt", "turntable");
        info.Axes.Should().OnlyContain(a => a.Kind == AxisKind.Rotary);
    }

    [Fact]
    public void TheMarkerInterfaceIsTheDeclaredClrType()
    {
        // FR-3: InterfaceClrType IS the marker — marker-primary classification is how the registry
        // already works, not a new mechanism.
        Positioner().InterfaceClrType.Should().Be(typeof(IPositioner));
        typeof(IMotionDevice).IsAssignableFrom(Positioner().InterfaceClrType).Should().BeTrue();
    }

    [Fact]
    public void AxisRoster_IsHeterogeneous_AndCarriesTheUnitForTheInspector()
    {
        // AC-15: the builder's inspector reads ° or mm from the DECLARATION, not from a constant.
        // A test declaration suffices — epic-065 ships no physical linear axis.
        var info = Positioner() with
        {
            Axes =
            [
                new AxisDeclaration("tilt", AxisKind.Rotary, []),
                new AxisDeclaration("carriage", AxisKind.Linear, []),
            ],
        };

        info.Axes.Select(a => UnitOf(a.Kind)).Should().Equal("°", "mm");

        static string UnitOf(AxisKind kind) => kind switch
        {
            AxisKind.Rotary => "°",
            AxisKind.Linear => "mm",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    [Fact]
    public void AxisDeclaration_CarriesTheAxisOwnConfigSection()
    {
        // The Add-device dialog renders one section per declared axis from that axis's schema —
        // per-machine values (drive IP, PG ratio, limits, calibration) live in the hub, keyed by
        // the declared name.
        var tilt = new AxisDeclaration("tilt", AxisKind.Rotary,
        [
            new ConfigPropertySchema("Ip", "Drive IP", "ip", Required: true, Group: "tilt"),
            new ConfigPropertySchema("SpeedSlope", "°/s per Hz", "double", Required: true, Default: "0.5435", Group: "tilt"),
        ]);

        tilt.PropertySchemas.Select(p => p.Name).Should().Equal("Ip", "SpeedSlope");
        tilt.PropertySchemas.Should().OnlyContain(p => p.Group == "tilt");
    }

    [Fact]
    public void AxisDeclaration_EqualityIsReferenceBasedOverPropertySchemas_SoDoNotBuildOnIt()
    {
        // ⚠ This pins a SHARP EDGE, not a feature. AxisDeclaration is a record, but its
        // PropertySchemas member is ConfigPropertySchema[] — and record-synthesised equality
        // compares an array BY REFERENCE. So two declarations that are structurally identical in
        // every visible way are NOT equal:
        var a = new AxisDeclaration("tilt", AxisKind.Rotary,
            [new ConfigPropertySchema("Ip", "Drive IP", "ip", Required: true)]);
        var b = new AxisDeclaration("tilt", AxisKind.Rotary,
            [new ConfigPropertySchema("Ip", "Drive IP", "ip", Required: true)]);

        a.Should().NotBe(b, "record equality compares ConfigPropertySchema[] by reference");
        a.PropertySchemas.Should().NotBeSameAs(b.PropertySchemas);

        // ...even though the SCHEMA ELEMENTS themselves do have value equality. The array is the
        // only reference-compared link in the chain, which is exactly what makes this easy to miss.
        a.PropertySchemas[0].Should().Be(b.PropertySchemas[0]);

        // AC-17 CONSEQUENCE, stated so nobody rediscovers it the hard way: the program-load check
        // that compares a stored facade against the loaded plugin's declaration must compare
        // (Name, Kind) EXPLICITLY. It must never use == or .Equals on AxisDeclaration, which would
        // report a mismatch on every load and fail programs that are in fact correct.
        AxisIdentity(a).Should().Be(AxisIdentity(b));

        static (string Name, AxisKind Kind) AxisIdentity(AxisDeclaration d) => (d.Name, d.Kind);
    }

    [Fact]
    public void AxisDeclaration_EmptySchemasCompareEqual_ForTheWrongReason()
    {
        // The trap that made the original version of this test vacuous: a collection expression
        // `[]` lowers to the INTERNED Array.Empty<ConfigPropertySchema>(), so both declarations
        // hold the same array reference and equality succeeds — proving nothing about value
        // semantics. Pinned so the next person reading a passing equality assertion here knows
        // which of the two cases they are looking at.
        var a = new AxisDeclaration("tilt", AxisKind.Rotary, []);
        var b = new AxisDeclaration("tilt", AxisKind.Rotary, []);

        a.PropertySchemas.Should().BeSameAs(b.PropertySchemas);
        a.Should().Be(b);

        // Name and Kind do compare by value, which is the half that behaves as expected.
        a.Should().NotBe(new AxisDeclaration("tilt", AxisKind.Linear, []));
        a.Should().NotBe(new AxisDeclaration("turntable", AxisKind.Rotary, []));
    }
}
