using System.Text.Json;
using ModelingEvolution.Drawing;
using RocketWelder.SDK.Vision;

namespace RocketWelder.SDK.Automation.Tests;

public class CameraIntrinsicsTests
{
    [Fact]
    public void Json_RoundTrip_WithWidthHeight()
    {
        var original = new CameraIntrinsics(
            new Matrix<double>(500, 0, 0, 500, 320, 240),
            new DistortionCoefficients(0.1, -0.2, 0.001, -0.001, 0.05),
            640, 480);

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<CameraIntrinsics>(json);

        Assert.Equal(original.Fx, deserialized.Fx);
        Assert.Equal(original.Fy, deserialized.Fy);
        Assert.Equal(original.Cx, deserialized.Cx);
        Assert.Equal(original.Cy, deserialized.Cy);
        Assert.Equal(original.K1, deserialized.K1);
        Assert.Equal(original.K2, deserialized.K2);
        Assert.Equal(original.P1, deserialized.P1);
        Assert.Equal(original.P2, deserialized.P2);
        Assert.Equal(original.K3, deserialized.K3);
        Assert.Equal(640, deserialized.Width);
        Assert.Equal(480, deserialized.Height);
    }

    [Fact]
    public void Json_BackwardCompatible_ReadsOld9ElementFormat()
    {
        // Old format: [fx, fy, cx, cy, k1, k2, p1, p2, k3]
        var json = "[500,500,320,240,0.1,-0.2,0.001,-0.001,0.05]";

        var deserialized = JsonSerializer.Deserialize<CameraIntrinsics>(json);

        Assert.Equal(500, deserialized.Fx);
        Assert.Equal(500, deserialized.Fy);
        Assert.Equal(320, deserialized.Cx);
        Assert.Equal(240, deserialized.Cy);
        Assert.Equal(0.1, deserialized.K1);
        Assert.Equal(0, deserialized.Width);
        Assert.Equal(0, deserialized.Height);
    }

    [Fact]
    public void Json_WritesNew11ElementFormat()
    {
        var intrinsics = new CameraIntrinsics(
            new Matrix<double>(500, 0, 0, 500, 320, 240),
            new DistortionCoefficients(0, 0, 0, 0, 0),
            1920, 1080);

        var json = JsonSerializer.Serialize(intrinsics);

        // Should have 11 elements: 9 doubles + 2 ints
        Assert.Equal("[500,500,320,240,0,0,0,0,0,1920,1080]", json);
    }

    [Fact]
    public void Json_RoundTrip_WithoutWidthHeight_DefaultsToZero()
    {
        var original = new CameraIntrinsics(
            new Matrix<double>(500, 0, 0, 500, 320, 240),
            new DistortionCoefficients(0, 0, 0, 0, 0));

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<CameraIntrinsics>(json);

        Assert.Equal(0, deserialized.Width);
        Assert.Equal(0, deserialized.Height);
    }

    [Fact]
    public void WidthHeight_DefaultToZero()
    {
        var intrinsics = new CameraIntrinsics(
            new Matrix<double>(500, 0, 0, 500, 320, 240),
            new DistortionCoefficients(0, 0, 0, 0, 0));

        Assert.Equal(0, intrinsics.Width);
        Assert.Equal(0, intrinsics.Height);
    }
}
