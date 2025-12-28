using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Types;

/// <summary>
/// Represents a video recording device in the form of {HostName}/cam-{CameraNumber} or {HostName}/file-{FileName}.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<VideoRecordingDevice>))]
[DataContract]
public readonly struct VideoRecordingDevice : IParsable<VideoRecordingDevice>
{
    [DataMember(Order = 1)]
    public required HostName HostName { get; init; }

    [DataMember(Order = 2)]
    public int? CameraNumber { get; init; }

    [DataMember(Order = 3)]
    public string? FileName { get; init; }

    public override string ToString() =>
        !string.IsNullOrEmpty(FileName)
            ? $"{HostName}/file-{FileName}"
            : $"{HostName}/cam-{CameraNumber ?? 0}";

    public static implicit operator VideoRecordingDevice(CameraAddress addr) =>
        new() { HostName = addr.HostName, CameraNumber = addr.CameraNumber, FileName = null };

    public static VideoRecordingDevice Parse(string s, IFormatProvider? provider = null)
    {
        if (string.IsNullOrEmpty(s))
            throw new ArgumentNullException(nameof(s));

        var parts = s.Split('/');
        if (parts.Length != 2)
            throw new FormatException("Invalid format. Expected: {HostName}/cam-{CameraNumber} or {HostName}/file-{FileName}");

        var hostName = HostName.Parse(parts[0], provider);
        var secondPart = parts[1];

        if (secondPart.StartsWith("cam-"))
        {
            if (int.TryParse(secondPart[4..], out var cameraNumber))
                return new VideoRecordingDevice { HostName = hostName, CameraNumber = cameraNumber };
            throw new FormatException("Invalid camera number format.");
        }

        if (secondPart.StartsWith("file-"))
        {
            var fileName = secondPart[5..];
            if (!string.IsNullOrEmpty(fileName))
                return new VideoRecordingDevice { HostName = hostName, FileName = fileName };
            throw new FormatException("Invalid file name format.");
        }

        throw new FormatException("Invalid format. Expected: {HostName}/cam-{CameraNumber} or {HostName}/file-{FileName}");
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out VideoRecordingDevice result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;

        var parts = s.Split('/');
        if (parts.Length != 2) return false;

        if (!HostName.TryParse(parts[0], provider, out var hostName)) return false;

        var secondPart = parts[1];
        if (secondPart.StartsWith("cam-"))
        {
            if (int.TryParse(secondPart[4..], out var cameraNumber))
            {
                result = new VideoRecordingDevice { HostName = hostName, CameraNumber = cameraNumber };
                return true;
            }
        }
        else if (secondPart.StartsWith("file-"))
        {
            var fileName = secondPart[5..];
            if (!string.IsNullOrEmpty(fileName))
            {
                result = new VideoRecordingDevice { HostName = hostName, FileName = fileName };
                return true;
            }
        }

        return false;
    }
}
