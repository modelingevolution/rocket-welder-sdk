using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// Serializes as JSON array: [fx, fy, cx, cy, k1, k2, p1, p2, k3].
/// </summary>
/// <param name="K">Camera matrix (fx, fy, cx, cy)</param>
/// <param name="D">Distortion coefficients [k1, k2, p1, p2, k3]</param>
[JsonConverter(typeof(CameraIntrinsicsJsonConverter))]
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

        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException("Expected end of array for CameraIntrinsics.");

        return new CameraIntrinsics(
            new Matrix<double>(fx, 0, 0, fy, cx, cy),
            new DistortionCoefficients(k1, k2, p1, p2, k3));
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
        writer.WriteEndArray();
    }
}
