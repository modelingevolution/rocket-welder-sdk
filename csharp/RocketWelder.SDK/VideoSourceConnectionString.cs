using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using RocketWelder.SDK.Internal;

namespace RocketWelder.SDK;

/// <summary>
/// Strongly-typed connection string for video source input.
/// Format: protocol://address or simple values like camera index.
///
/// Supported formats:
/// - "0", "1", etc. - Camera device index
/// - file:///path/to/video.mp4 - Video file
/// - /path/to/video.mp4 - Video file (shorthand)
/// - shm://buffer_name - Shared memory buffer
/// - rtsp://host/stream - RTSP stream
/// </summary>
public readonly record struct VideoSourceConnectionString : IParsable<VideoSourceConnectionString>
{
    /// <summary>
    /// The full original connection string.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The source type (camera, file, shm, rtsp).
    /// </summary>
    public VideoSourceType SourceType { get; }

    /// <summary>
    /// Camera index (when SourceType is Camera).
    /// </summary>
    public int? CameraIndex { get; }

    /// <summary>
    /// File path or endpoint (when SourceType is File, Shm, or Rtsp).
    /// </summary>
    public string? Path { get; }

    /// <summary>
    /// Additional parameters from the connection string.
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    private VideoSourceConnectionString(
        string value,
        VideoSourceType sourceType,
        int? cameraIndex,
        string? path,
        IReadOnlyDictionary<string, string> parameters)
    {
        Value = value;
        SourceType = sourceType;
        CameraIndex = cameraIndex;
        Path = path;
        Parameters = parameters;
    }

    /// <summary>
    /// Default video source (camera 0).
    /// </summary>
    public static VideoSourceConnectionString Default => Parse("0", null);

    /// <summary>
    /// Creates a connection string from environment variable or uses default.
    /// </summary>
    public static VideoSourceConnectionString FromEnvironment(string variableName = "VIDEO_SOURCE")
    {
        var value = Environment.GetEnvironmentVariable(variableName)
            ?? Environment.GetEnvironmentVariable("CONNECTION_STRING");
        return string.IsNullOrEmpty(value) ? Default : Parse(value, null);
    }

    public static VideoSourceConnectionString Parse(string s, IFormatProvider? provider)
    {
        if (!TryParse(s, provider, out var result))
            throw new FormatException($"Invalid video source connection string: {s}");
        return result;
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out VideoSourceConnectionString result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ConnectionStringParser.ExtractQueryParameters(s, out var endpointPart, parameters);

        // Check for camera index first
        if (int.TryParse(endpointPart, out var cameraIndex))
        {
            result = new VideoSourceConnectionString(s, VideoSourceType.Camera, cameraIndex, null, parameters);
            return true;
        }

        // Parse protocol
        VideoSourceType sourceType;
        string? path;

        if (endpointPart.StartsWith("file://"))
        {
            sourceType = VideoSourceType.File;
            path = endpointPart["file://".Length..];
        }
        else if (endpointPart.StartsWith("shm://"))
        {
            sourceType = VideoSourceType.SharedMemory;
            path = endpointPart["shm://".Length..];
        }
        else if (endpointPart.StartsWith("rtsp://"))
        {
            sourceType = VideoSourceType.Rtsp;
            path = endpointPart; // Keep full URL for RTSP
        }
        else if (endpointPart.StartsWith("http://") || endpointPart.StartsWith("https://"))
        {
            sourceType = VideoSourceType.Http;
            path = endpointPart;
        }
        else if (!endpointPart.Contains("://"))
        {
            // Assume file path
            sourceType = VideoSourceType.File;
            path = endpointPart;
        }
        else
        {
            return false;
        }

        result = new VideoSourceConnectionString(s, sourceType, null, path, parameters);
        return true;
    }

    public override string ToString() => Value;

    public static implicit operator string(VideoSourceConnectionString cs) => cs.Value;
}

/// <summary>
/// Type of video source.
/// </summary>
public enum VideoSourceType
{
    /// <summary>Camera device (by index).</summary>
    Camera,
    /// <summary>Video file.</summary>
    File,
    /// <summary>Shared memory buffer.</summary>
    SharedMemory,
    /// <summary>RTSP stream.</summary>
    Rtsp,
    /// <summary>HTTP/HTTPS stream.</summary>
    Http
}
