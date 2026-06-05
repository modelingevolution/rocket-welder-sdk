using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Vision;

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
/// Camera intrinsic parameters: matrix K, distortion coefficients D, and image dimensions.
/// Serializes as JSON array: [fx, fy, cx, cy, k1, k2, p1, p2, k3] or [fx, fy, cx, cy, k1, k2, p1, p2, k3, width, height].
/// </summary>
/// <param name="K">Camera matrix (fx, fy, cx, cy)</param>
/// <param name="D">Distortion coefficients [k1, k2, p1, p2, k3]</param>
/// <param name="Width">Image width in pixels (0 if unknown)</param>
/// <param name="Height">Image height in pixels (0 if unknown)</param>
[JsonConverter(typeof(CameraIntrinsicsJsonConverter))]
public readonly record struct CameraIntrinsics(
    Matrix<double> K,
    DistortionCoefficients D,
    int Width = 0,
    int Height = 0
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

    /// <summary>
    /// Projects a 3D point in camera frame to a 2D pixel coordinate
    /// using the pinhole model with Brown-Conrady distortion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Point<double> PointToPixel(Point3<double> cameraPoint)
    {
        double xn = cameraPoint.X / cameraPoint.Z;
        double yn = cameraPoint.Y / cameraPoint.Z;

        double r2 = xn * xn + yn * yn;
        double r4 = r2 * r2;
        double r6 = r4 * r2;
        double radial = 1.0 + K1 * r2 + K2 * r4 + K3 * r6;

        double xd = xn * radial + 2.0 * P1 * xn * yn + P2 * (r2 + 2.0 * xn * xn);
        double yd = yn * radial + P1 * (r2 + 2.0 * yn * yn) + 2.0 * P2 * xn * yn;

        return new Point<double>(Fx * xd + Cx, Fy * yd + Cy);
    }

    /// <summary>
    /// Converts a pixel coordinate to a normalized, undistorted ray direction in camera frame.
    /// Applies iterative Brown-Conrady undistortion (same algorithm as cv::undistortPoints).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public Vector3<double> PixelToRay(Point<double> pixel)
    {
        double xd = (pixel.X - Cx) / Fx;
        double yd = (pixel.Y - Cy) / Fy;

        bool hasDistortion = K1 != 0 || K2 != 0 || K3 != 0 || P1 != 0 || P2 != 0;
        if (!hasDistortion)
            return new Vector3<double>(xd, yd, 1.0).Normalize();

        double xu = xd, yu = yd;
        for (int i = 0; i < 10; i++)
        {
            double r2 = xu * xu + yu * yu;
            double r4 = r2 * r2;
            double r6 = r4 * r2;
            double radial = 1.0 + K1 * r2 + K2 * r4 + K3 * r6;
            double dx = 2.0 * P1 * xu * yu + P2 * (r2 + 2.0 * xu * xu);
            double dy = P1 * (r2 + 2.0 * yu * yu) + 2.0 * P2 * xu * yu;
            xu = (xd - dx) / radial;
            yu = (yd - dy) / radial;
        }

        return new Vector3<double>(xu, yu, 1.0).Normalize();
    }
}

/// <summary>
/// Serializes CameraIntrinsics as a compact JSON array: [fx, fy, cx, cy, k1, k2, p1, p2, k3].
/// Required because DistortionCoefficients uses [InlineArray] which System.Text.Json cannot handle.
/// </summary>
public class CameraIntrinsicsJsonConverter : JsonConverter<CameraIntrinsics>
{
    public override CameraIntrinsics Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected start of array for CameraIntrinsics.");

        reader.Read(); var fx = reader.GetDouble();
        reader.Read(); var fy = reader.GetDouble();
        reader.Read(); var cx = reader.GetDouble();
        reader.Read(); var cy = reader.GetDouble();
        reader.Read(); var k1 = reader.GetDouble();
        reader.Read(); var k2 = reader.GetDouble();
        reader.Read(); var p1 = reader.GetDouble();
        reader.Read(); var p2 = reader.GetDouble();
        reader.Read(); var k3 = reader.GetDouble();

        // Backward-compatible: read optional Width, Height if present
        int width = 0, height = 0;
        reader.Read();
        if (reader.TokenType == JsonTokenType.Number)
        {
            width = reader.GetInt32();
            reader.Read(); height = reader.GetInt32();
            reader.Read();
        }

        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException("Expected end of array for CameraIntrinsics.");

        return new CameraIntrinsics(
            new Matrix<double>(fx, 0, 0, fy, cx, cy),
            new DistortionCoefficients(k1, k2, p1, p2, k3),
            width, height);
    }

    public override void Write(Utf8JsonWriter writer, CameraIntrinsics value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Fx);
        writer.WriteNumberValue(value.Fy);
        writer.WriteNumberValue(value.Cx);
        writer.WriteNumberValue(value.Cy);
        writer.WriteNumberValue(value.K1);
        writer.WriteNumberValue(value.K2);
        writer.WriteNumberValue(value.P1);
        writer.WriteNumberValue(value.P2);
        writer.WriteNumberValue(value.K3);
        writer.WriteNumberValue(value.Width);
        writer.WriteNumberValue(value.Height);
        writer.WriteEndArray();
    }
}
