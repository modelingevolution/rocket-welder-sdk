using System.Runtime.CompilerServices;
using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Automation.Vision;

/// <summary>
/// Distortion coefficients [k1, k2, p1, p2, k3].
/// </summary>
[InlineArray(5)]
public struct DistortionCoefficients
{
    private double _element;

    public DistortionCoefficients(double k1, double k2, double p1, double p2, double k3)
    {
        this[0] = k1;
        this[1] = k2;
        this[2] = p1;
        this[3] = p2;
        this[4] = k3;
    }
}

/// <summary>
/// Camera intrinsic parameters: matrix K and distortion coefficients D.
/// </summary>
/// <param name="K">Camera matrix (fx, fy, cx, cy)</param>
/// <param name="D">Distortion coefficients [k1, k2, p1, p2, k3]</param>
public readonly record struct CameraIntrinsics(
    Matrix<double> K,
    DistortionCoefficients D
)
{
    /// <summary>Focal length X (pixels)</summary>
    public double Fx => K.M11;

    /// <summary>Focal length Y (pixels)</summary>
    public double Fy => K.M22;

    /// <summary>Principal point X (pixels)</summary>
    public double Cx => K.OffsetX;

    /// <summary>Principal point Y (pixels)</summary>
    public double Cy => K.OffsetY;

    /// <summary>Radial distortion coefficient 1</summary>
    public double K1 => D[0];

    /// <summary>Radial distortion coefficient 2</summary>
    public double K2 => D[1];

    /// <summary>Tangential distortion coefficient 1</summary>
    public double P1 => D[2];

    /// <summary>Tangential distortion coefficient 2</summary>
    public double P2 => D[3];

    /// <summary>Radial distortion coefficient 3</summary>
    public double K3 => D[4];
}
