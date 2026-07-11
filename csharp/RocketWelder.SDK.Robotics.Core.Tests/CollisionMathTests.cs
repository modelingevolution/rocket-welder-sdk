using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core.Tests;

/// <summary>
/// Worst-case tests for closed-form segment-to-box and segment-to-cylinder-Z SDF.
/// Compares against a dense (10_001-sample) brute-force minimum; the closed-form
/// result must equal (inside tolerance) or be tighter than the sampled minimum.
/// </summary>
public class CollisionMathTests
{
    private const int BruteSamples = 10_000;
    private const double Tol = 1e-6;

    private static double BruteForceMinBox(Point3<double> p0, Point3<double> p1,
        Point3<double> c, double hx, double hy, double hz)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i <= BruteSamples; i++)
        {
            double t = (double)i / BruteSamples;
            var p = new Point3<double>(
                (double)p0.X + ((double)p1.X - (double)p0.X) * t,
                (double)p0.Y + ((double)p1.Y - (double)p0.Y) * t,
                (double)p0.Z + ((double)p1.Z - (double)p0.Z) * t);
            var (_, d) = CollisionMath.SignedDistanceToBox(p, c, hx, hy, hz);
            if (d < best) best = d;
        }
        return best;
    }

    [Theory]
    [InlineData(0, 0, -1000,   0, 0, 1000)]               // through-center pierce along Z
    [InlineData(-2000, 0, 0,   2000, 0, 0)]               // through-center pierce along X
    [InlineData(500, 500, 500, -500, -500, -500)]         // diagonal pierce
    [InlineData(1000, 1000, 1000, 1100, 1100, 1100)]      // fully outside, corner-adjacent
    [InlineData(500, 0, 200, -500, 400, 200)]             // axial with oblique clip
    [InlineData(100, 100, 100, 100, 100, 100)]            // degenerate (zero-length)
    [InlineData(0, 400, 0, 400, 0, 0)]                    // diagonal, endpoints outside, passes near edge
    public void ClosestPointOnSegmentToBox_MatchesOrBeatsBruteForce(
        double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var p0 = new Point3<double>(x0, y0, z0);
        var p1 = new Point3<double>(x1, y1, z1);
        var c = new Point3<double>(0, 0, 0);
        double hx = 300, hy = 200, hz = 150;

        var (_, closed) = CollisionMath.ClosestPointOnSegmentToBox(p0, p1, c, hx, hy, hz);
        var brute = BruteForceMinBox(p0, p1, c, hx, hy, hz);

        // Closed-form captures break-points exactly → must be ≤ brute (or within tol above).
        closed.Should().BeLessThanOrEqualTo(brute + Tol);
        closed.Should().BeGreaterThan(brute - 1.0, "closed-form should not be pathologically low");
    }

    private static double BruteForceMinCylZ(Point3<double> p0, Point3<double> p1,
        Point3<double> c, double radius, double height)
    {
        double hz = height * 0.5;
        double best = double.PositiveInfinity;
        for (int i = 0; i <= BruteSamples; i++)
        {
            double t = (double)i / BruteSamples;
            double px = (double)p0.X + ((double)p1.X - (double)p0.X) * t;
            double py = (double)p0.Y + ((double)p1.Y - (double)p0.Y) * t;
            double pz = (double)p0.Z + ((double)p1.Z - (double)p0.Z) * t;
            double dx = px - (double)c.X, dy = py - (double)c.Y, dz = pz - (double)c.Z;
            double r = Math.Sqrt(dx * dx + dy * dy);
            double radial = r - radius;
            double axial = Math.Abs(dz) - hz;
            double sdf = (radial > 0 && axial > 0)
                ? Math.Sqrt(radial * radial + axial * axial)
                : Math.Max(radial, axial);
            if (sdf < best) best = sdf;
        }
        return best;
    }

    [Theory]
    [InlineData(0, 0, -500,    0, 0, 500)]                // pierce along axis
    [InlineData(-500, 0, 0,    500, 0, 0)]                // pierce radially
    [InlineData(-500, 50, 0,   500, -50, 0)]              // tangent-like radial scan
    [InlineData(1000, 1000, 1000, 1000, 1000, -1000)]     // fully outside, vertical
    [InlineData(500, 0, 400,   -500, 0, 400)]             // above the cylinder, radial scan
    [InlineData(300, 300, 300, 300, 300, 300)]            // zero-length
    public void ClosestPointOnSegmentToCylinderZ_MatchesOrBeatsBruteForce(
        double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var p0 = new Point3<double>(x0, y0, z0);
        var p1 = new Point3<double>(x1, y1, z1);
        var c = new Point3<double>(0, 0, 0);
        double radius = 200, height = 400;

        var (_, closed) = CollisionMath.ClosestPointOnSegmentToCylinderZ(p0, p1, c, radius, height);
        var brute = BruteForceMinCylZ(p0, p1, c, radius, height);

        closed.Should().BeLessThanOrEqualTo(brute + Tol);
        closed.Should().BeGreaterThan(brute - 1.0);
    }

    [Fact]
    public void ClosestPointOnSegmentToBox_DetectsTangentPierce_BruteForceMayMiss()
    {
        // Regression: this segment grazes the box between uniform samples. The old
        // 17-point sampler returned a positive distance; closed-form must detect pierce.
        var p0 = new Point3<double>(-1000, 0.5, 0);
        var p1 = new Point3<double>( 1000, 0.5, 0);
        var c = new Point3<double>(0, 0, 0);
        var (_, d) = CollisionMath.ClosestPointOnSegmentToBox(p0, p1, c, 100, 1, 50);
        d.Should().BeLessThan(0, "segment pierces box along X; SDF must be negative");
    }
}
