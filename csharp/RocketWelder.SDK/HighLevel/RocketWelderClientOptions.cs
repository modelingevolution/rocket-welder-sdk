using System;

namespace RocketWelder.SDK.HighLevel;

/// <summary>
/// Configuration options for RocketWelderClient.
/// </summary>
public class RocketWelderClientOptions
{
    /// <summary>
    /// Video source (file path, camera index, or URL).
    /// Default: "0" (default camera)
    /// </summary>
    public string VideoSource { get; set; } = "0";

    /// <summary>
    /// KeyPoints transport endpoint.
    /// Default: "ipc:///tmp/rocket-welder-keypoints"
    /// </summary>
    public string KeyPointsEndpoint { get; set; } = "ipc:///tmp/rocket-welder-keypoints";

    /// <summary>
    /// Segmentation transport endpoint.
    /// Default: "ipc:///tmp/rocket-welder-segmentation"
    /// </summary>
    public string SegmentationEndpoint { get; set; } = "ipc:///tmp/rocket-welder-segmentation";

    /// <summary>
    /// Frames between master keypoint frames.
    /// Default: 300
    /// </summary>
    public int MasterFrameInterval { get; set; } = 300;

    /// <summary>
    /// Transport type: "nng", "tcp", "websocket", "file".
    /// Default: "nng"
    /// </summary>
    public string Transport { get; set; } = "nng";

    /// <summary>
    /// Creates options from environment variables.
    /// </summary>
    public static RocketWelderClientOptions FromEnvironment()
    {
        return new RocketWelderClientOptions
        {
            VideoSource = Environment.GetEnvironmentVariable("ROCKET_WELDER_VIDEO_SOURCE") ?? "0",
            KeyPointsEndpoint = Environment.GetEnvironmentVariable("ROCKET_WELDER_KEYPOINTS_ENDPOINT")
                ?? "ipc:///tmp/rocket-welder-keypoints",
            SegmentationEndpoint = Environment.GetEnvironmentVariable("ROCKET_WELDER_SEGMENTATION_ENDPOINT")
                ?? "ipc:///tmp/rocket-welder-segmentation",
            MasterFrameInterval = int.TryParse(
                Environment.GetEnvironmentVariable("ROCKET_WELDER_MASTER_FRAME_INTERVAL"),
                out var interval) ? interval : 300,
            Transport = Environment.GetEnvironmentVariable("ROCKET_WELDER_TRANSPORT") ?? "nng"
        };
    }
}
