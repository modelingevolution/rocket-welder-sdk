using ModelingEvolution.Drawing;
using RocketWelder.SDK.Vision;

namespace RocketWelder.SDK.Automation.Tests;

using Pose3d = Pose3<double>;
using Point3d = Point3<double>;
using Pointd = Point<double>;

public class CameraProjectorTryProjectToPixelTests
{
    private const double Fx = 800.0;
    private const double Fy = 820.0;
    private const double Cx = 640.0;
    private const double Cy = 360.0;

    private static CameraIntrinsics NoDistortion() => new(
        new Matrix<double>(Fx, 0, 0, Fy, Cx, Cy),
        new DistortionCoefficients(0, 0, 0, 0, 0),
        1280, 720);

    private static CameraIntrinsics WithDistortion() => new(
        new Matrix<double>(Fx, 0, 0, Fy, Cx, Cy),
        new DistortionCoefficients(-0.28, 0.10, 0.0012, -0.0009, -0.02),
        1280, 720);

    private static CameraProjector Projector(CameraIntrinsics intr, Pose3d handEye, Pose3d gripper)
        => new(intr, handEye, () => gripper);

    private static void AssertPixel(Pointd expected, Pointd actual, double tol)
    {
        Assert.True(Math.Abs(expected.X - actual.X) < tol,
            $"X: expected {expected.X}, actual {actual.X}, |Δ|={Math.Abs(expected.X - actual.X)} >= {tol}");
        Assert.True(Math.Abs(expected.Y - actual.Y) < tol,
            $"Y: expected {expected.Y}, actual {actual.Y}, |Δ|={Math.Abs(expected.Y - actual.Y)} >= {tol}");
    }

    [Fact]
    public void OpticalAxisPoint_ProjectsToPrincipalPoint()
    {
        var p = Projector(NoDistortion(), Pose3d.Identity, Pose3d.Identity);

        // Identity camera-in-base: a base point straight ahead lands on the principal point.
        var ok = p.TryProjectToPixel(new Point3d(0, 0, 1000), out var pixel);

        Assert.True(ok);
        AssertPixel(new Pointd(Cx, Cy), pixel, 1e-9);
    }

    [Theory]
    [InlineData(50, 0, 1000)]
    [InlineData(0, -75, 1000)]
    [InlineData(120, -64, 1500)]
    [InlineData(-200, 90, 800)]
    public void OffAxisPoint_NoDistortion_MatchesPinholeFormula(double x, double y, double z)
    {
        var p = Projector(NoDistortion(), Pose3d.Identity, Pose3d.Identity);

        var ok = p.TryProjectToPixel(new Point3d(x, y, z), out var pixel);

        Assert.True(ok);
        var expected = new Pointd(Fx * x / z + Cx, Fy * y / z + Cy);
        AssertPixel(expected, pixel, 1e-9);
    }

    [Theory]
    [InlineData(0.0)]    // exactly on the image plane
    [InlineData(-0.001)] // a hair behind
    [InlineData(-500.0)] // well behind
    public void PointOnOrBehindImagePlane_ReturnsFalse(double z)
    {
        var p = Projector(NoDistortion(), Pose3d.Identity, Pose3d.Identity);

        var ok = p.TryProjectToPixel(new Point3d(10, 10, z), out var pixel);

        Assert.False(ok);
        Assert.Equal(default, pixel);
    }

    [Theory]
    [InlineData(640, 360)]   // principal point
    [InlineData(100, 100)]
    [InlineData(1200, 680)]
    [InlineData(640, 50)]
    [InlineData(20, 700)]
    public void RoundTrip_NoDistortion_RecoversOriginalPixel(double px, double py)
    {
        var intr = NoDistortion();
        var p = Projector(intr, Pose3d.Identity, Pose3d.Identity);

        // Un-project the pixel onto a plane 750mm ahead, then project the 3D point back.
        var surface = new Pose3d(0, 0, 750, 0, 0, 0);
        var point3d = p.ProjectPoint(new Pointd(px, py), surface);

        var ok = p.TryProjectToPixel(point3d, out var roundTripped);

        Assert.True(ok);
        AssertPixel(new Pointd(px, py), roundTripped, 1e-6);
    }

    [Theory]
    [InlineData(640, 360)]
    [InlineData(150, 130)]
    [InlineData(1150, 640)]
    [InlineData(900, 200)]
    public void RoundTrip_WithDistortion_RecoversOriginalPixel(double px, double py)
    {
        // PixelToRay (un-distort) and PointToPixel (distort) are mutual inverses, so the
        // un-project → project round trip must return to the source pixel.
        var intr = WithDistortion();
        var p = Projector(intr, Pose3d.Identity, Pose3d.Identity);

        var surface = new Pose3d(0, 0, 900, 0, 0, 0);
        var point3d = p.ProjectPoint(new Pointd(px, py), surface);

        var ok = p.TryProjectToPixel(point3d, out var roundTripped);

        Assert.True(ok);
        AssertPixel(new Pointd(px, py), roundTripped, 1e-3);
    }

    [Fact]
    public void NonIdentityPoseChain_MatchesHandComputedGroundTruth()
    {
        var intr = NoDistortion();
        var gripper = new Pose3d(100, 200, 300, 10, 20, 30);
        var handEye = new Pose3d(5, -3, 50, 0, 0, 90);
        var p = Projector(intr, handEye, gripper);

        // Independent ground truth: pick a point Q in the camera frame, map it into base
        // frame with the SAME composition the projector uses, then assert the projector
        // recovers the pinhole pixel of Q.
        var cameraToBase = gripper * handEye;
        var qCamera = new Point3d(12, -7, 250);
        var basePoint = cameraToBase.TransformPoint(qCamera);

        var ok = p.TryProjectToPixel(basePoint, out var pixel);

        Assert.True(ok);
        var expected = new Pointd(Fx * qCamera.X / qCamera.Z + Cx, Fy * qCamera.Y / qCamera.Z + Cy);
        AssertPixel(expected, pixel, 1e-6);
    }

    [Fact]
    public void NonIdentityPoseChain_PointBehindCamera_ReturnsFalse()
    {
        var intr = NoDistortion();
        var gripper = new Pose3d(100, 200, 300, 10, 20, 30);
        var handEye = new Pose3d(5, -3, 50, 0, 0, 90);
        var p = Projector(intr, handEye, gripper);

        var cameraToBase = gripper * handEye;
        var behind = cameraToBase.TransformPoint(new Point3d(0, 0, -50));

        var ok = p.TryProjectToPixel(behind, out var pixel);

        Assert.False(ok);
        Assert.Equal(default, pixel);
    }

    [Fact]
    public void ProjectionTracksLiveGripperPose()
    {
        var intr = NoDistortion();
        var gripper = new[] { Pose3d.Identity };
        var p = new CameraProjector(intr, Pose3d.Identity, () => gripper[0]);

        var basePoint = new Point3d(0, 0, 1000);

        Assert.True(p.TryProjectToPixel(basePoint, out var before));
        AssertPixel(new Pointd(Cx, Cy), before, 1e-9);

        // Move the camera +100mm along base X: the fixed base point now sits at camera-frame
        // x = -100, so its pixel must shift left by Fx * 100 / 1000 = 80px.
        gripper[0] = new Pose3d(100, 0, 0, 0, 0, 0);

        Assert.True(p.TryProjectToPixel(basePoint, out var after));
        AssertPixel(new Pointd(Cx - Fx * 100.0 / 1000.0, Cy), after, 1e-9);
    }
}
