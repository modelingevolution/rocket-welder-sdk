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

    // Standard test waypoints — FK outputs of selected joint configs, chosen so
    // consecutive targets stay on the same IK branch (regression fixtures only).
    // Source configs (see docs/iterations/iteration-3/dev-log.md TASK-010):
    //   WP1: ( 0, -30, 30,   0, 60,   0)    WP2: (15, -40, 45, -15, 55, -20)
    //   WP3: (30, -50, 55, -30, 50, -40)    WP4: (10, -35, 40, -10, 60, -10)
    //   WP5: (-10, -25, 35,  10, 65,  10)
    public static readonly Pose3<double> WP1 = new(-674.7362,   51.0000, -162.6000,  -90.0000,   0.0000,  -60.0000);
    public static readonly Pose3<double> WP2 = new(-613.1103, -103.7138, -172.7973,  -80.9330, -25.4941,  -43.5161);
    public static readonly Pose3<double> WP3 = new(-511.4574, -219.5829, -198.6536,  -58.0772, -52.2474,  -43.4291);
    public static readonly Pose3<double> WP4 = new(-643.8103,  -61.7344, -151.3552,  -85.5665, -12.4685,  -50.8644);
    public static readonly Pose3<double> WP5 = new(-703.7837,  167.8681,  -86.5801, -108.9984,  17.7897,  -79.6127);

    // Ground-truth FK values: regression baselines regenerated from our own FK output
    // after the FR5 DH correction (d4=0, a4=-395.01, d5=102.1, d6=102.0) per
    // docs/epics/epic-021-robot-simulator/fairino-preset-reference.md.
    // These are NOT independent ground truth — they pin current behaviour against drift.
    public static readonly Pose3<double> FK_HOME = new(-820.0100, 102.0000, 49.9000, -90.0000, 0.0000, 0.0000);
    public static readonly Pose3<double> FK_CFG_A = new(-554.9524, -46.0663, -295.0104, -59.2660, -32.0812, -63.8352);
    public static readonly Pose3<double> FK_CFG_B = new(-465.1283, -363.1283, 148.4170, -39.2315, -37.7612, -18.4349);
    public static readonly Pose3<double> FK_CFG_C = new(-542.5297, 396.5123, -70.9145, 64.4386, 34.9753, 175.5614);
    public static readonly Pose3<double> FK_CFG_D = new(-395.0100, 102.0000, -375.1000, -90.0000, 0.0000, 0.0000);
    public static readonly Pose3<double> FK_CFG_E = new(0.0000, -551.2099, -148.5911, 0.0000, -45.0000, -90.0000);

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
