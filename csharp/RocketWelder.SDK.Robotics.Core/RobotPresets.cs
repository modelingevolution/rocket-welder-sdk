using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Factory for known robot models. Convenience presets — the library has no dependency on any robot brand.
/// DH values derived from FAIR-INNOVATION/frcobot_ros2 URDFs (Modified DH, Craig convention).
/// See docs/epics/epic-021-robot-simulator/fairino-preset-reference.md for provenance.
/// </summary>
public static class RobotPresets
{
    // Shared joint limits for all Fairino v6 6-DOF arms (FR3/FR5/FR10/FR16/FR20/FR30).
    private static readonly JointLimit[] FairinoV6Limits =
    {
        new(-178, 178),
        new(-265,  85),
        new(-162, 162),
        new(-265,  85),
        new(-178, 178),
        new(-360, 360),
    };

    /// <summary>
    /// Builds a Fairino v6 6-DOF RobotModel from the per-model link-length parameters.
    /// d4 is always 0 for v6 arms (j3/j4 axes parallel under MDH Craig).
    /// </summary>
    private static RobotModel FairinoV6(string name, double d1, double a2, double a3, double d5, double d6)
    {
        var chain = new DhJoint[]
        {
            DhJoint.FromDegrees(  0,    0, d1, 0),   // Joint 1
            DhJoint.FromDegrees(-90,    0,  0, 0),   // Joint 2
            DhJoint.FromDegrees(  0,  -a2,  0, 0),   // Joint 3
            DhJoint.FromDegrees(  0,  -a3,  0, 0),   // Joint 4 (d4 = 0)
            DhJoint.FromDegrees(-90,    0, d5, 0),   // Joint 5
            DhJoint.FromDegrees( 90,    0, d6, 0),   // Joint 6
        };
        return new RobotModel(name, chain, FairinoV6Limits, Joints6<double>.Zero);
    }

    /// <summary>Fairino FR3 6-DOF collaborative robot (3 kg payload, 590 mm reach).</summary>
    public static RobotModel FairinoFR3() =>
        FairinoV6("Fairino FR3", d1: 140, a2: 280,  a3: 240.01, d5: 102,    d6: 102);

    /// <summary>Fairino FR5 6-DOF collaborative robot (5 kg payload, 922 mm reach).</summary>
    public static RobotModel FairinoFR5() =>
        FairinoV6("Fairino FR5", d1: 152, a2: 425,  a3: 395.01, d5: 102.1,  d6: 102);

    /// <summary>Fairino FR10 6-DOF collaborative robot (10 kg payload, 1422 mm reach).</summary>
    public static RobotModel FairinoFR10() =>
        FairinoV6("Fairino FR10", d1: 180, a2: 700, a3: 586,    d5: 159,    d6: 114);

    /// <summary>Fairino FR16 6-DOF collaborative robot (16 kg payload, 1052 mm reach).</summary>
    public static RobotModel FairinoFR16() =>
        FairinoV6("Fairino FR16", d1: 180, a2: 520, a3: 400,    d5: 159,    d6: 114);

    /// <summary>Fairino FR20 6-DOF collaborative robot (20 kg payload, 1922 mm reach).</summary>
    public static RobotModel FairinoFR20() =>
        FairinoV6("Fairino FR20", d1: 215, a2: 1000, a3: 716,   d5: 166.01, d6: 138);

    /// <summary>Fairino FR30 6-DOF collaborative robot (30 kg payload, 1442 mm reach).</summary>
    public static RobotModel FairinoFR30() =>
        FairinoV6("Fairino FR30", d1: 215, a2: 700, a3: 536,    d5: 166.01, d6: 138);
}
