using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Factory for known robot models. Convenience presets — the library has no dependency on any robot brand.
/// </summary>
public static class RobotPresets
{
    /// <summary>
    /// Creates a RobotModel for the Fairino FR5 6-DOF collaborative robot.
    /// DH parameters use Modified DH (Craig convention) with shifted indexing:
    /// T_i = Rot_x(alpha_{i-1}) * Trans_x(a_{i-1}) * Rot_z(theta_i) * Trans_z(d_i)
    /// </summary>
    public static RobotModel FairinoFR5()
    {
        // Standard DH table from datasheet:
        // Joint | a (mm)   | d (mm)  | alpha (deg) | theta offset (deg)
        // 1     | 0        | 152.0   | -90         | 0
        // 2     | -425.0   | 0       | 0           | 0
        // 3     | -392.25  | 0       | 0           | 0
        // 4     | 0        | 115.7   | -90         | 0
        // 5     | 0        | 92.2    | 90          | 0
        // 6     | 0        | 94.0    | 0           | 0
        //
        // Craig convention mapping: alpha and a use i-1 indexing.
        // For joint i, use alpha_{i-1} and a_{i-1} from the PREVIOUS row.
        // Joint 1: alpha_0=0, a_0=0, d_1=152.0, theta_offset_1=0
        // Joint 2: alpha_1=-90, a_1=0, d_2=0, theta_offset_2=0
        // Joint 3: alpha_2=0, a_2=-425.0, d_3=0, theta_offset_3=0
        // Joint 4: alpha_3=0, a_3=-392.25, d_4=115.7, theta_offset_4=0
        // Joint 5: alpha_4=-90, a_4=0, d_5=92.2, theta_offset_5=0
        // Joint 6: alpha_5=90, a_5=0, d_6=94.0, theta_offset_6=0

        var dhChain = new DhJoint[]
        {
            DhJoint.FromDegrees(0,       0,       152.0,  0),   // Joint 1
            DhJoint.FromDegrees(-90,     0,       0,      0),   // Joint 2
            DhJoint.FromDegrees(0,       -425.0,  0,      0),   // Joint 3
            DhJoint.FromDegrees(0,       -392.25, 115.7,  0),   // Joint 4
            DhJoint.FromDegrees(-90,     0,       92.2,   0),   // Joint 5
            DhJoint.FromDegrees(90,      0,       94.0,   0),   // Joint 6
        };

        var jointLimits = new JointLimit[]
        {
            new(-175, 175),
            new(-175, 175),
            new(-175, 175),
            new(-175, 175),
            new(-175, 175),
            new(-175, 175),
        };

        return new RobotModel("Fairino FR5", dhChain, jointLimits, Joints6<double>.Zero);
    }
}
