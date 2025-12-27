using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Shared;

/// <summary>
/// Uniquely identifies a video recording by hostname, camera number, and creation time.
/// Format: {HostName}:{CameraNumber}/{CreatedTime:o} or {HostName}/{CreatedTime:o}
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<VideoRecordingIdentifier>))]
[DataContract]
public readonly record struct VideoRecordingIdentifier : IParsable<VideoRecordingIdentifier>, IComparable<VideoRecordingIdentifier>
{
    [DataMember(Order = 1)]
    public HostName HostName { get; init; }

    [DataMember(Order = 2)]
    public int? CameraNumber { get; init; }

    [DataMember(Order = 3)]
    public DateTimeOffset CreatedTime { get; init; }

    public VideoRecordingIdentifier() { }

    public VideoRecordingIdentifier(HostName hostName, DateTimeOffset createdTime)
    {
        HostName = hostName;
        CameraNumber = null;
        CreatedTime = createdTime;
    }

    public VideoRecordingIdentifier(HostName hostName, int cameraNumber, DateTimeOffset createdTime)
    {
        HostName = hostName;
        CameraNumber = cameraNumber;
        CreatedTime = createdTime;
    }

    public enum FileNamingConvention
    {
        Iso8601,
        ZoneOffset
    }

    [JsonIgnore]
    public FileNamingConvention Convention =>
        CreatedTime.Offset == TimeSpan.Zero ? FileNamingConvention.Iso8601 : FileNamingConvention.ZoneOffset;

    public string ToStringFileName(FileNamingConvention? convention = null)
    {
        if ((convention == null && CreatedTime.Offset == TimeSpan.Zero) || convention == FileNamingConvention.Iso8601)
        {
            var utcStr = CreatedTime.UtcDateTime.ToString("yyyyMMddTHHmmss.ffffff") + "Z";
            return CameraNumber.HasValue && CameraNumber.Value != 0
                ? $"{HostName}.{CameraNumber}.{utcStr}"
                : $"{HostName}.{utcStr}";
        }
        else
        {
            var dateStr = CreatedTime.ToString("yyyyMMddTHHmmss.ffffff");
            var offsetStr = CreatedTime.ToString("zzz").Replace(":", "");
            var fullDateStr = dateStr + offsetStr;

            return CameraNumber.HasValue && CameraNumber.Value != 0
                ? $"{HostName}.{CameraNumber}.{fullDateStr}"
                : $"{HostName}.{fullDateStr}";
        }
    }

    public static bool TryParseFileName(string fileName, out VideoRecordingIdentifier result)
    {
        result = default;
        if (string.IsNullOrEmpty(fileName)) return false;

        var parts = fileName.Split('.');
        if (parts.Length < 3 || parts.Length > 4) return false;

        if (!HostName.TryParse(parts[0], null, out var hostName)) return false;

        string dateTimePart = $"{parts[^2]}.{parts[^1]}";
        if (dateTimePart.Length < 20) return false;

        try
        {
            DateTimeOffset parsedTime;
            if (dateTimePart.EndsWith("Z"))
            {
                var utcDateTime = DateTime.ParseExact(
                    dateTimePart.TrimEnd('Z'),
                    "yyyyMMddTHHmmss.ffffff",
                    null,
                    System.Globalization.DateTimeStyles.AssumeUniversal |
                    System.Globalization.DateTimeStyles.AdjustToUniversal);
                parsedTime = new DateTimeOffset(utcDateTime, TimeSpan.Zero);
            }
            else
            {
                int signPos = dateTimePart.LastIndexOfAny(['+', '-']);
                if (signPos == -1) return false;

                string dtPart = dateTimePart[..signPos];
                string offsetPart = dateTimePart[signPos..];

                var dt = DateTime.ParseExact(
                    dtPart,
                    "yyyyMMddTHHmmss.ffffff",
                    null,
                    System.Globalization.DateTimeStyles.None);

                if (offsetPart.Length != 5) return false;

                int offsetHours = int.Parse(offsetPart.Substring(1, 2));
                int offsetMinutes = int.Parse(offsetPart.Substring(3, 2));
                var offset = new TimeSpan(offsetHours, offsetMinutes, 0);
                if (offsetPart[0] == '-') offset = -offset;

                parsedTime = new DateTimeOffset(dt, offset);
            }

            if (parts.Length == 4)
            {
                if (!int.TryParse(parts[1], out int cameraNumber)) return false;
                result = new VideoRecordingIdentifier(hostName, cameraNumber, parsedTime);
            }
            else
            {
                result = new VideoRecordingIdentifier(hostName, parsedTime);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static VideoRecordingIdentifier Parse(string s, IFormatProvider? provider = null)
    {
        if (string.IsNullOrEmpty(s)) throw new ArgumentNullException(nameof(s));

        var parts = s.Split('/');
        if (parts.Length != 2) throw new FormatException("Invalid format for VideoRecordingIdentifier.");

        var sourceInfo = parts[0].Split(':');
        if (sourceInfo.Length == 1)
        {
            var hostName = HostName.Parse(sourceInfo[0], provider);
            var createdTime = DateTimeOffset.ParseExact(parts[1], "o", provider);
            return new VideoRecordingIdentifier(hostName, createdTime);
        }
        else
        {
            var hostName = HostName.Parse(sourceInfo[0], provider);
            var cameraId = int.Parse(sourceInfo[1]);
            var createdTime = DateTimeOffset.ParseExact(parts[1], "o", provider);
            return new VideoRecordingIdentifier(hostName, cameraId, createdTime);
        }
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out VideoRecordingIdentifier result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;

        var parts = s.Split('/');
        if (parts.Length != 2) return false;

        var sourceInfo = parts[0].Split(':');
        if (sourceInfo.Length != 2) return false;

        if (!HostName.TryParse(sourceInfo[0], provider, out var hostName)) return false;
        if (!int.TryParse(sourceInfo[1], out var cameraId)) return false;
        if (!DateTime.TryParseExact(parts[1], "o", provider, System.Globalization.DateTimeStyles.None, out var createdTime))
            return false;

        result = new VideoRecordingIdentifier(hostName, cameraId, createdTime);
        return true;
    }

    public override string ToString() =>
        CameraNumber.HasValue ? $"{HostName}:{CameraNumber}/{CreatedTime:o}" : $"{HostName}/{CreatedTime:o}";

    /// <summary>
    /// Converts this identifier to a deterministic GUID based on its string representation.
    /// </summary>
    public static implicit operator Guid(VideoRecordingIdentifier addr) => addr.ToGuid();

    /// <summary>
    /// Converts this identifier to a deterministic GUID using MD5 hash of string representation.
    /// </summary>
    public Guid ToGuid()
    {
        var bytes = Encoding.UTF8.GetBytes(ToString());
        var hash = MD5.HashData(bytes);
        return new Guid(hash);
    }

    public static implicit operator VideoRecordingDevice(VideoRecordingIdentifier addr) =>
        new() { HostName = addr.HostName, CameraNumber = addr.CameraNumber };

    public static implicit operator VideoRecordingIdentifier(CameraAddress addr) =>
        new(addr.HostName, addr.CameraNumber ?? 0, DateTimeOffset.Now);

    public int CompareTo(VideoRecordingIdentifier other)
    {
        var hostNameComparison = HostName.CompareTo(other.HostName);
        if (hostNameComparison != 0) return hostNameComparison;
        var cameraNumberComparison = Nullable.Compare(CameraNumber, other.CameraNumber);
        if (cameraNumberComparison != 0) return cameraNumberComparison;
        return CreatedTime.CompareTo(other.CreatedTime);
    }
}
