using FluentAssertions;
using RocketWelder.SDK.Abstractions;
using RocketWelder.SDK.Automation;
using RocketWelder.SDK.Devices.Motion;

namespace RocketWelder.SDK.Automation.Tests;

/// <summary>
/// FR-8 — the axis roster is declared by the plugin, <b>in code</b>: git-versioned, reviewable as a
/// diff, and structural rather than a per-station configuration. The hub stores values keyed by
/// these names; it never stores the structure.
/// </summary>
public class AxisRosterTests
{
    private static DeviceTypeInfo NonMotionDeviceType() => new(
        DeviceType: "acme-camera",
        InterfaceType: "ICamera",
        DisplayName: "ACME camera",
        InterfaceClrType: typeof(IDevice),
        PropertySchemas: [],
        Factory: (_, _) => new object());

    [Fact]
    public void DeviceTypeInfo_WithoutAxes_HasAnEmptyRoster()
    {
        // The member is additive: an existing plugin's constructor call is untouched and its
        // device simply has no axes — never null, so a consumer never needs a null check.
        var info = NonMotionDeviceType();

        info.Axes.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void DeviceTypeInfo_DeclaresItsAxesInOrder()
    {
        var info = NonMotionDeviceType() with
        {
            DeviceType = "delta-positioner-2r",
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
    public void AxisRoster_IsHeterogeneous_AndCarriesTheUnitForTheInspector()
    {
        // AC-15: the builder's inspector reads ° or mm from the DECLARATION, not from a constant.
        // A test declaration suffices — epic-065 ships no physical linear axis.
        var info = NonMotionDeviceType() with
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
    public void AxisDeclaration_HasValueEquality()
    {
        // Roster equality is how a program-load check compares a stored facade against the loaded
        // plugin's declaration (AC-17).
        var a = new AxisDeclaration("tilt", AxisKind.Rotary, []);
        var b = new AxisDeclaration("tilt", AxisKind.Rotary, []);

        a.Should().Be(b);
        a.Should().NotBe(new AxisDeclaration("tilt", AxisKind.Linear, []));
        a.Should().NotBe(new AxisDeclaration("turntable", AxisKind.Rotary, []));
    }
}
