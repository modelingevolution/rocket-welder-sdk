namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// 4x4 homogeneous transformation matrix using double precision.
/// Row-major layout: M[row][col].
/// </summary>
public readonly record struct Matrix4x4d(
    double M00, double M01, double M02, double M03,
    double M10, double M11, double M12, double M13,
    double M20, double M21, double M22, double M23,
    double M30, double M31, double M32, double M33
)
{
    public static readonly Matrix4x4d Identity = new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1
    );

    /// <summary>
    /// Returns the transpose of this matrix.
    /// </summary>
    public Matrix4x4d Transpose() => new(
        M00, M10, M20, M30,
        M01, M11, M21, M31,
        M02, M12, M22, M32,
        M03, M13, M23, M33
    );

    /// <summary>
    /// Inverts a homogeneous transformation matrix [R|t; 0 0 0 1].
    /// Inverse is [R^T | -R^T*t; 0 0 0 1].
    /// </summary>
    public Matrix4x4d InvertRigid()
    {
        // R^T
        var r00 = M00; var r01 = M10; var r02 = M20;
        var r10 = M01; var r11 = M11; var r12 = M21;
        var r20 = M02; var r21 = M12; var r22 = M22;

        // -R^T * t
        var tx = -(r00 * M03 + r01 * M13 + r02 * M23);
        var ty = -(r10 * M03 + r11 * M13 + r12 * M23);
        var tz = -(r20 * M03 + r21 * M13 + r22 * M23);

        return new Matrix4x4d(
            r00, r01, r02, tx,
            r10, r11, r12, ty,
            r20, r21, r22, tz,
            0, 0, 0, 1
        );
    }
}
