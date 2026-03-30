using ModelingEvolution.Drawing;
using RocketWelder.SDK.Automation;

namespace RocketWelder.SDK.Robotics.Core.Tests;

/// <summary>
/// Shared test data: standard configurations, waypoints, and tolerances.
/// </summary>
internal static class TestData
{
    // Standard test configurations (joint angles in degrees)
    public static readonly Joints6<double> HOME = Joints6<double>.Zero;
    public static readonly Joints6<double> CFG_A = new(10, -45, 30, -15, 60, -20);
    public static readonly Joints6<double> CFG_B = new(45, -30, 60, -90, 45, 0);
    public static readonly Joints6<double> CFG_C = new(-30, -60, 90, 0, -45, 120);
    public static readonly Joints6<double> CFG_D = new(0, -90, 90, 0, 0, 0);
    public static readonly Joints6<double> CFG_E = new(90, -45, 45, -45, 90, -90);

    // Standard test waypoints (TCP poses in robot base frame)
    public static readonly Pose3<double> WP1 = new(400.0, 0.0, 300.0, 180.0, 0.0, 0.0);
    public static readonly Pose3<double> WP2 = new(350.0, 150.0, 250.0, 175.0, 5.0, -10.0);
    public static readonly Pose3<double> WP3 = new(300.0, -200.0, 400.0, 170.0, -5.0, 15.0);
    public static readonly Pose3<double> WP4 = new(450.0, 100.0, 200.0, -175.0, 3.0, -5.0);
    public static readonly Pose3<double> WP5 = new(250.0, -100.0, 350.0, 180.0, 0.0, 0.0);

    // Ground-truth FK values from test-scenarios.md
    public static readonly Pose3<double> FK_HOME = new(-817.2500, 209.7000, 59.8000, -90.0000, 0.0000, 0.0000);
    public static readonly Pose3<double> FK_CFG_A = new(-582.5071, 62.4982, -289.1865, -59.2660, -32.0812, -63.8352);
    public static readonly Pose3<double> FK_CFG_B = new(-549.3129, -291.6884, 147.0880, -39.2315, -37.7612, -18.4349);
    public static readonly Pose3<double> FK_CFG_C = new(-476.9087, 485.6929, -66.5493, 64.4386, 34.9753, 175.5614);
    public static readonly Pose3<double> FK_CFG_D = new(-392.2500, 209.7000, -365.2000, -90.0000, 0.0000, 0.0000);
    public static readonly Pose3<double> FK_CFG_E = new(-115.7000, -561.1071, -147.2476, 0.0000, -45.0000, -90.0000);

    // Tolerances
    public const double PosTol = 0.01;       // mm
    public const double RotTol = 0.001;      // degrees
    public const double JointTol = 0.01;     // degrees
    public const double SingPosTol = 0.1;    // mm (relaxed for singularity)
    public const double SingRotTol = 0.01;   // degrees (relaxed for singularity)

    public static readonly Velocity DefaultVelocity = Velocity.Percentage(50);

    public static RobotModel CreateFR5() => RobotPresets.FairinoFR5();

    public static void AssertPoseEquals(Pose3<double> expected, Pose3<double> actual,
        double posTol = PosTol, double rotTol = RotTol, string? because = null)
    {
        actual.X.Should().BeApproximately(expected.X, posTol, because ?? $"X: expected {expected.X}");
        actual.Y.Should().BeApproximately(expected.Y, posTol, because ?? $"Y: expected {expected.Y}");
        actual.Z.Should().BeApproximately(expected.Z, posTol, because ?? $"Z: expected {expected.Z}");

        AssertAngleApprox((double)actual.Rx, (double)expected.Rx, rotTol, because ?? $"Rx: expected {expected.Rx}");
        AssertAngleApprox((double)actual.Ry, (double)expected.Ry, rotTol, because ?? $"Ry: expected {expected.Ry}");
        AssertAngleApprox((double)actual.Rz, (double)expected.Rz, rotTol, because ?? $"Rz: expected {expected.Rz}");
    }

    /// <summary>
    /// Compares two angles accounting for wraparound (e.g., 180 and -180 are the same).
    /// </summary>
    public static void AssertAngleApprox(double actual, double expected, double tol, string? because = null)
    {
        var diff = actual - expected;
        // Normalize to [-180, 180]
        while (diff > 180) diff -= 360;
        while (diff < -180) diff += 360;
        Math.Abs(diff).Should().BeLessThanOrEqualTo(tol, because ?? $"angle diff: actual={actual}, expected={expected}");
    }

    public static void AssertPositionEquals(Pose3<double> expected, Pose3<double> actual,
        double posTol = PosTol, string? because = null)
    {
        actual.X.Should().BeApproximately(expected.X, posTol, because ?? $"X: expected {expected.X}");
        actual.Y.Should().BeApproximately(expected.Y, posTol, because ?? $"Y: expected {expected.Y}");
        actual.Z.Should().BeApproximately(expected.Z, posTol, because ?? $"Z: expected {expected.Z}");
    }
}
