using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Shared;

/// <summary>
/// Uniquely identifies a specific frame within a video recording.
/// Format: {VideoRecordingIdentifier}/{FrameNumber}
/// </summary>
[DataContract]
[JsonConverter(typeof(JsonParsableConverter<FrameId>))]
public readonly record struct FrameId : IComparable<FrameId>, IParsable<FrameId>
{
    [DataMember(Order = 1)]
    public VideoRecordingIdentifier Recording
    {
        get => _recording;
        init => _recording = value;
    }

    [DataMember(Order = 2)]
    public ulong FrameNumber
    {
        get => _frameNumber;
        init => _frameNumber = value;
    }

    public static readonly FrameId Empty = new();
    private readonly VideoRecordingIdentifier _recording;
    private readonly ulong _frameNumber;

    public FrameId() { }

    public FrameId(VideoRecordingIdentifier recording, ulong frameNumber)
    {
        Recording = recording;
        FrameNumber = frameNumber;
    }

    public static FrameId From(VideoRecordingIdentifier identifier, ulong frameNumber) =>
        new(identifier, frameNumber);

    /// <summary>
    /// Converts this frame ID to a deterministic GUID based on its string representation.
    /// </summary>
    public static implicit operator Guid(FrameId frame) => frame.ToGuid();

    /// <summary>
    /// Converts this frame ID to a deterministic GUID using MD5 hash of string representation.
    /// </summary>
    public Guid ToGuid()
    {
        var bytes = Encoding.UTF8.GetBytes(ToString());
        var hash = MD5.HashData(bytes);
        return new Guid(hash);
    }

    public static implicit operator string(FrameId frameId) => frameId.ToString();

    public static FrameId From(string frameId) => Parse(frameId);

    public override string ToString() => $"{Recording}/{FrameNumber}";

    public string ToStringFileName()
    {
        string recordingPart = Recording.ToStringFileName();
        return $"{recordingPart}.{FrameNumber}";
    }

    public static bool TryParseFileName(string fileName, out FrameId result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        int lastDotIndex = fileName.LastIndexOf('.');
        if (lastDotIndex == -1)
            return false;

        string recordingPart = fileName[..lastDotIndex];
        string frameNumberPart = fileName[(lastDotIndex + 1)..];

        if (!VideoRecordingIdentifier.TryParseFileName(recordingPart, out var recording))
            return false;

        if (!ulong.TryParse(frameNumberPart, out var frameNumber))
            return false;

        result = new FrameId(recording, frameNumber);
        return true;
    }

    public static FrameId Parse(string input, IFormatProvider? formatProvider = null)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentNullException(nameof(input), "Input string cannot be null or empty.");

        try
        {
            int lastIndex = input.LastIndexOf('/');
            string recording = input[..lastIndex];
            string number = input[(lastIndex + 1)..];
            return new FrameId(VideoRecordingIdentifier.Parse(recording, null), ulong.Parse(number));
        }
        catch (Exception ex) when (ex is not FormatException)
        {
            throw new FormatException("Failed to parse input string", ex);
        }
    }

    public static bool TryParse([NotNullWhen(true)] string? input, IFormatProvider? formatProvider, out FrameId result)
    {
        try
        {
            result = Parse(input!, formatProvider);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    public int CompareTo(FrameId other)
    {
        var recordingComparison = _recording.CompareTo(other._recording);
        if (recordingComparison != 0) return recordingComparison;
        return _frameNumber.CompareTo(other._frameNumber);
    }
}
