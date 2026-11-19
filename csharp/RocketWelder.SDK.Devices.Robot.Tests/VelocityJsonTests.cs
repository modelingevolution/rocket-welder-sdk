using System.Globalization;
using System.Text.Json;
using RocketWelder.SDK.Devices.Robot;

namespace RocketWelder.SDK.Devices.Robot.Tests;

/// <summary>Velocity's wire: three read forms (text, object with a numeric unit, object with a named unit), one write
/// form (text).</summary>
public class VelocityJsonTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private sealed record Body(Velocity Velocity);
    private sealed record OptionalBody(Velocity? Velocity = null);

    [Theory]
    [InlineData("\"20%\"", 20, VelocityUnit.Percentage)]
    [InlineData("\"100mm/s\"", 100, VelocityUnit.MmPerSecond)]
    [InlineData("\"48cm/min\"", 48, VelocityUnit.CmPerMinute)]
    [InlineData("\"7.5mm/s\"", 7.5, VelocityUnit.MmPerSecond)]
    public void Read_TextForm_BindsTheValueObject(string json, double value, VelocityUnit unit)
    {
        var v = JsonSerializer.Deserialize<Velocity>(json, Web);

        v.Value.Should().Be(value);
        v.Unit.Should().Be(unit);
    }

    [Theory]
    [InlineData("{\"value\":50,\"unit\":0}", 50, VelocityUnit.Percentage)]
    [InlineData("{\"value\":8,\"unit\":1}", 8, VelocityUnit.MmPerSecond)]
    [InlineData("{\"Value\":48,\"Unit\":2}", 48, VelocityUnit.CmPerMinute)]
    public void Read_ObjectFormWithNumericUnit_BindsTheValueObject(string json, double value, VelocityUnit unit)
    {
        var v = JsonSerializer.Deserialize<Velocity>(json, Web);

        v.Value.Should().Be(value);
        v.Unit.Should().Be(unit);
    }

    [Theory]
    [InlineData("{\"value\":50,\"unit\":\"percent\"}", 50, VelocityUnit.Percentage)]
    [InlineData("{\"value\":50,\"unit\":\"Percentage\"}", 50, VelocityUnit.Percentage)]
    [InlineData("{\"value\":8,\"unit\":\"mmPerSecond\"}", 8, VelocityUnit.MmPerSecond)]
    [InlineData("{\"unit\":\"cmPerMinute\",\"value\":48}", 48, VelocityUnit.CmPerMinute)]
    public void Read_ObjectFormWithNamedUnit_BindsTheValueObject(string json, double value, VelocityUnit unit)
    {
        var v = JsonSerializer.Deserialize<Velocity>(json, Web);

        v.Value.Should().Be(value);
        v.Unit.Should().Be(unit);
    }

    [Theory]
    [InlineData("\"fast\"")]
    [InlineData("{\"value\":50}")]
    [InlineData("{\"value\":50,\"unit\":7}")]
    [InlineData("{\"value\":50,\"unit\":\"knots\"}")]
    [InlineData("50")]
    public void Read_UnknownForm_IsRefused(string json)
    {
        var act = () => JsonSerializer.Deserialize<Velocity>(json, Web);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Write_IsTheTextForm()
    {
        JsonSerializer.Serialize(new Body(Velocity.MmPerSecond(8)), Web).Should().Be("{\"velocity\":\"8mm/s\"}");
        JsonSerializer.Serialize(Velocity.Percentage(20), Web).Should().Be("\"20%\"");
        JsonSerializer.Serialize(Velocity.CmPerMinute(48), Web).Should().Be("\"48cm/min\"");
    }

    [Fact]
    public void Write_UsesTheInvariantCulture_WhateverTheThreadCulture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            JsonSerializer.Serialize(Velocity.MmPerSecond(7.5), Web).Should().Be("\"7.5mm/s\"");
            Velocity.Percentage(12.5).ToString().Should().Be("12.5%");
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void Nullable_Member_ReadsBothFormsAndAbsent()
    {
        JsonSerializer.Deserialize<OptionalBody>("{}", Web)!.Velocity.Should().BeNull();
        JsonSerializer.Deserialize<OptionalBody>("{\"velocity\":\"20%\"}", Web)!.Velocity.Should().Be(Velocity.Percentage(20));
        JsonSerializer.Deserialize<OptionalBody>("{\"velocity\":{\"value\":20,\"unit\":0}}", Web)!.Velocity
            .Should().Be(Velocity.Percentage(20));
    }

    [Fact]
    public void RoundTrip_TextForm()
    {
        var v = Velocity.CmPerMinute(42.25);
        JsonSerializer.Deserialize<Velocity>(JsonSerializer.Serialize(v, Web), Web).Should().Be(v);
    }
}
