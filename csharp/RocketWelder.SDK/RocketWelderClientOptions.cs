using System;

namespace RocketWelder.SDK;

/// <summary>
/// Configuration options for RocketWelderClient.
/// Uses strongly-typed connection strings implementing IParsable.
/// </summary>
public class RocketWelderClientOptions
{
    /// <summary>
    /// Video source connection string.
    /// Examples: "0" (camera), "file:///path/to/video.mp4", "shm://buffer"
    /// Default: "0" (default camera)
    /// </summary>
    public VideoSourceConnectionString VideoSource { get; set; } = VideoSourceConnectionString.Default;

    /// <summary>
    /// KeyPoints output connection string.
    /// Supports parameters: masterFrameInterval
    /// Default: "nng+push://ipc:///tmp/rocket-welder-keypoints?masterFrameInterval=300"
    /// </summary>
    public KeyPointsConnectionString KeyPoints { get; set; } = KeyPointsConnectionString.Default;

    /// <summary>
    /// Segmentation output connection string.
    /// Default: "nng+push://ipc:///tmp/rocket-welder-segmentation"
    /// </summary>
    public SegmentationConnectionString Segmentation { get; set; } = SegmentationConnectionString.Default;

    /// <summary>
    /// Creates options from environment variables.
    /// Environment variables:
    /// - VIDEO_SOURCE or CONNECTION_STRING: Video input
    /// - KEYPOINTS_CONNECTION_STRING: KeyPoints output
    /// - SEGMENTATION_CONNECTION_STRING: Segmentation output
    /// </summary>
    public static RocketWelderClientOptions FromEnvironment()
    {
        return new RocketWelderClientOptions
        {
            VideoSource = VideoSourceConnectionString.FromEnvironment(),
            KeyPoints = KeyPointsConnectionString.FromEnvironment(),
            Segmentation = SegmentationConnectionString.FromEnvironment()
        };
    }
}
