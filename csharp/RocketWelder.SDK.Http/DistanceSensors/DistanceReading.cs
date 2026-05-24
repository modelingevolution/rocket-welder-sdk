namespace RocketWelder.SDK.Http.DistanceSensors;

/// <summary>
/// Single distance measurement from a 1-D distance sensor. Wire shape; the
/// server projects from the SDK's <c>DistanceReading</c> domain record.
/// </summary>
/// <param name="Millimeters">Absolute distance in millimeters at the time of capture.</param>
/// <param name="OffsetFromTargetMm">Signed offset from the sensor's configured target distance.</param>
/// <param name="CapturedAtUtc">When the reading was taken, UTC.</param>
public sealed record DistanceReading(
    double Millimeters,
    double OffsetFromTargetMm,
    DateTime CapturedAtUtc);
