using System.Text.Json;
using System.Text.Json.Serialization;
using ModelingEvolution.Drawing;

namespace RocketWelder.SDK.Robotics.Core;

/// <summary>
/// One recorded pairing of joint angles (degrees) and the TCP pose (mm, degrees) the
/// robot controller reported for those joints. Used for FK validation against a
/// <see cref="RobotModel"/>.
/// </summary>
public sealed record FkValidationRecord(Joints6<double> Joints, Pose3<double> TcpPose);

/// <summary>
/// Per-record FK validation outcome. Deltas are expected minus actual (record minus FK);
/// position units are mm, rotation units are degrees, and <see cref="EuclideanDistance"/>
/// is the positional Euclidean error in mm.
/// </summary>
public sealed record FkValidationResult(
    int Index,
    double DX,
    double DY,
    double DZ,
    double DRx,
    double DRy,
    double DRz,
    double EuclideanDistance);

/// <summary>
/// Pure static utility for importing, exporting, and validating FK records.
/// Thread-safe.
/// </summary>
public static class FkValidator
{
    /// <summary>Current supported JSON schema version.</summary>
    public const string SchemaVersion = "1";

    /// <summary>Serialize a list of records to JSON with schema version.</summary>
    public static string Export(IReadOnlyList<FkValidationRecord> records, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        var items = new RecordDto[records.Count];
        for (int i = 0; i < records.Count; i++)
            items[i] = new RecordDto { Joints = records[i].Joints, TcpPose = records[i].TcpPose };
        var dto = new FkValidationFileDto
        {
            SchemaVersion = SchemaVersion,
            Units = new UnitsDto(),
            Records = items
        };
        return JsonSerializer.Serialize(dto, options ?? DefaultJson);
    }

    /// <summary>Parse records from JSON. Throws on unsupported schema version.</summary>
    public static IReadOnlyList<FkValidationRecord> Import(string json, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        var dto = JsonSerializer.Deserialize<FkValidationFileDto>(json, options ?? DefaultJson)
                  ?? throw new InvalidOperationException("FK validation JSON deserialised to null.");
        if (!string.Equals(dto.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
            throw new NotSupportedException(
                $"Unsupported FK validation schema version '{dto.SchemaVersion}'. Expected '{SchemaVersion}'.");
        var records = dto.Records ?? Array.Empty<RecordDto>();
        var result = new FkValidationRecord[records.Length];
        for (int i = 0; i < records.Length; i++)
            result[i] = new FkValidationRecord(records[i].Joints, records[i].TcpPose);
        return result;
    }

    /// <summary>
    /// Compute FK for each record using <paramref name="model"/> and compare to the
    /// recorded TCP pose. Rotation deltas are wrapped to (-180, 180] degrees so that
    /// 359° vs 1° reports a 2° error instead of 358°.
    /// </summary>
    public static IReadOnlyList<FkValidationResult> Validate(
        RobotModel model,
        IReadOnlyList<FkValidationRecord> records,
        Pose3<double>? toolTransform = null,
        Pose3<double>? basePose = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(records);

        var results = new FkValidationResult[records.Count];
        for (int i = 0; i < records.Count; i++)
        {
            var record = records[i];
            var state = ForwardKinematics.Compute(model, record.Joints, toolTransform, basePose);
            var actual = state.TcpPose;
            var expected = record.TcpPose;

            var dx = expected.X - actual.X;
            var dy = expected.Y - actual.Y;
            var dz = expected.Z - actual.Z;
            var dRx = WrapDegrees((double)expected.Rx - (double)actual.Rx);
            var dRy = WrapDegrees((double)expected.Ry - (double)actual.Ry);
            var dRz = WrapDegrees((double)expected.Rz - (double)actual.Rz);
            var euclid = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            results[i] = new FkValidationResult(i, dx, dy, dz, dRx, dRy, dRz, euclid);
        }
        return results;
    }

    private static double WrapDegrees(double d)
    {
        d = ((d + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
        return d == -180.0 ? 180.0 : d;
    }

    internal static readonly JsonSerializerOptions DefaultJson = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class FkValidationFileDto
    {
        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; set; } = "1";

        [JsonPropertyName("units")]
        public UnitsDto Units { get; set; } = new();

        [JsonPropertyName("records")]
        public RecordDto[]? Records { get; set; }
    }

    private sealed class UnitsDto
    {
        [JsonPropertyName("joints")] public string Joints { get; set; } = "degrees";
        [JsonPropertyName("position")] public string Position { get; set; } = "mm";
        [JsonPropertyName("rotation")] public string Rotation { get; set; } = "degrees";
    }

    private sealed class RecordDto
    {
        [JsonPropertyName("joints")]
        public Joints6<double> Joints { get; set; }

        [JsonPropertyName("tcpPose")]
        public Pose3<double> TcpPose { get; set; }
    }
}
