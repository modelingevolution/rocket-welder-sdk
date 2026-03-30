using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// Pure static forward kinematics using Modified DH (Craig convention).
/// Thread-safe, no instance state.
/// </summary>
public static class ForwardKinematics
{
    /// <summary>
    /// Computes the forward kinematics for the given model and joint angles.
    /// Returns a RobotState with TCP pose and all intermediate frame poses.
    /// </summary>
    public static RobotState Compute(RobotModel model, Joints6<double> joints,
        Pose3<double>? toolTransform = null, Pose3<double>? basePose = null)
    {
        var framePoses = new Pose3<double>[7]; // 6 link frames + TCP (which may include tool)
        var cumulativeMatrix = Matrix4x4d.Identity;

        // Apply base pose if provided
        if (basePose.HasValue && !basePose.Value.IsIdentity)
            cumulativeMatrix = PoseToMatrix(basePose.Value);

        for (int i = 0; i < 6; i++)
        {
            var dh = model.DhChain[i];
            var theta = (double)joints[i] * Math.PI / 180.0 + dh.ThetaOffset;
            var jointMatrix = ComputeDhMatrix(dh.Alpha, dh.A, dh.D, theta);
            cumulativeMatrix = Multiply(cumulativeMatrix, jointMatrix);
            framePoses[i] = MatrixToPose(cumulativeMatrix);
        }

        // Frame 6 is the flange frame. TCP = flange * tool (if provided).
        if (toolTransform.HasValue && !toolTransform.Value.IsIdentity)
        {
            var toolMatrix = PoseToMatrix(toolTransform.Value);
            var tcpMatrix = Multiply(cumulativeMatrix, toolMatrix);
            framePoses[6] = MatrixToPose(tcpMatrix);
        }
        else
        {
            framePoses[6] = framePoses[5]; // TCP = flange when no tool
        }

        return RobotState.Create(joints, framePoses[6], framePoses);
    }

    /// <summary>
    /// Computes Modified DH (Craig convention) transform matrix for a single joint.
    /// T_i = Rot_x(alpha_{i-1}) * Trans_x(a_{i-1}) * Rot_z(theta_i) * Trans_z(d_i)
    /// </summary>
    /// <summary>
    /// Computes Modified DH (Craig convention) transform matrix for a single joint.
    /// </summary>
    public static Matrix4x4d ComputeDhMatrix(double alpha, double a, double d, double theta)
    {
        var ca = Math.Cos(alpha);
        var sa = Math.Sin(alpha);
        var ct = Math.Cos(theta);
        var st = Math.Sin(theta);

        return new Matrix4x4d(
            ct,       -st,       0,     a,
            st * ca,   ct * ca, -sa,   -sa * d,
            st * sa,   ct * sa,  ca,    ca * d,
            0,         0,        0,     1
        );
    }

    /// <summary>
    /// Converts a Pose3 (X,Y,Z in mm, Rx,Ry,Rz in degrees as ZYX Euler) to a 4x4 homogeneous matrix.
    /// R = Rz(rz) * Ry(ry) * Rx(rx) — ZYX intrinsic (Tait-Bryan).
    /// </summary>
    /// <summary>
    /// Converts a Pose3 to a 4x4 homogeneous matrix using ZYX Euler convention.
    /// </summary>
    public static Matrix4x4d PoseToMatrix(Pose3<double> pose)
    {
        var rx = (double)pose.Rx * Math.PI / 180.0;
        var ry = (double)pose.Ry * Math.PI / 180.0;
        var rz = (double)pose.Rz * Math.PI / 180.0;

        var cx = Math.Cos(rx); var sx = Math.Sin(rx);
        var cy = Math.Cos(ry); var sy = Math.Sin(ry);
        var cz = Math.Cos(rz); var sz = Math.Sin(rz);

        // R = Rz * Ry * Rx
        return new Matrix4x4d(
            cz * cy,                      cz * sy * sx - sz * cx,       cz * sy * cx + sz * sx,       pose.X,
            sz * cy,                      sz * sy * sx + cz * cx,       sz * sy * cx - cz * sx,       pose.Y,
            -sy,                          cy * sx,                      cy * cx,                      pose.Z,
            0,                            0,                            0,                            1
        );
    }

    /// <summary>
    /// Extracts Pose3 (X,Y,Z, Rx,Ry,Rz in ZYX Euler degrees) from a 4x4 homogeneous matrix.
    /// </summary>
    /// <summary>
    /// Extracts Pose3 from a 4x4 homogeneous matrix using ZYX Euler decomposition.
    /// </summary>
    public static Pose3<double> MatrixToPose(Matrix4x4d m)
    {
        double x = m.M03;
        double y = m.M13;
        double z = m.M23;

        // ZYX Euler decomposition
        double ry, rx, rz;

        var sy = -m.M20;
        if (Math.Abs(sy) >= 1.0 - 1e-10)
        {
            // Gimbal lock
            ry = Math.CopySign(Math.PI / 2.0, sy);
            rx = 0;
            rz = Math.Atan2(-m.M01, m.M11);
        }
        else
        {
            ry = Math.Asin(sy);
            rx = Math.Atan2(m.M21, m.M22);
            rz = Math.Atan2(m.M10, m.M00);
        }

        return new Pose3<double>(
            x, y, z,
            rx * 180.0 / Math.PI,
            ry * 180.0 / Math.PI,
            rz * 180.0 / Math.PI
        );
    }

    /// <summary>
    /// Multiplies two 4x4 matrices.
    /// </summary>
    public static Matrix4x4d Multiply(Matrix4x4d a, Matrix4x4d b)
    {
        return new Matrix4x4d(
            a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20 + a.M03 * b.M30,
            a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21 + a.M03 * b.M31,
            a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22 + a.M03 * b.M32,
            a.M00 * b.M03 + a.M01 * b.M13 + a.M02 * b.M23 + a.M03 * b.M33,

            a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20 + a.M13 * b.M30,
            a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31,
            a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32,
            a.M10 * b.M03 + a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33,

            a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20 + a.M23 * b.M30,
            a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31,
            a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32,
            a.M20 * b.M03 + a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33,

            a.M30 * b.M00 + a.M31 * b.M10 + a.M32 * b.M20 + a.M33 * b.M30,
            a.M30 * b.M01 + a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31,
            a.M30 * b.M02 + a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32,
            a.M30 * b.M03 + a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33
        );
    }
}
