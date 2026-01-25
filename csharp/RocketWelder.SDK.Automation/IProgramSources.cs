namespace RocketWelder.SDK.Automation;

/// <summary>
/// Provides access to keypoint detection results from ML pipeline.
/// </summary>
public interface IKeyPointsSource
{
    /// <summary>
    /// Gets the most recent keypoint frame.
    /// </summary>
    Task<KeyPointFrame?> GetLatestAsync(CancellationToken ct = default);

    /// <summary>
    /// Streams keypoint frames as they become available.
    /// </summary>
    IAsyncEnumerable<KeyPointFrame> StreamAsync(CancellationToken ct = default);
}

/// <summary>
/// Provides access to segmentation results from ML pipeline.
/// </summary>
public interface ISegmentationSource
{
    /// <summary>
    /// Gets the most recent segmentation result.
    /// </summary>
    Task<SegmentationResult?> GetLatestAsync(CancellationToken ct = default);

    /// <summary>
    /// Streams segmentation results as they become available.
    /// </summary>
    IAsyncEnumerable<SegmentationResult> StreamAsync(CancellationToken ct = default);
}

/// <summary>
/// Provides access to graphics overlays from ML pipeline.
/// </summary>
public interface IGraphicsSource
{
    /// <summary>
    /// Gets the most recent graphics frame.
    /// </summary>
    Task<GraphicsFrame?> GetLatestAsync(CancellationToken ct = default);
}

/// <summary>
/// Represents a frame of detected keypoints.
/// </summary>
public record KeyPointFrame(
    long FrameNumber,
    IReadOnlyList<KeyPoint> Points,
    DateTime Timestamp
);

/// <summary>
/// Represents a single detected keypoint.
/// </summary>
public record KeyPoint(
    string Name,
    float X,
    float Y,
    float Confidence
);

/// <summary>
/// Represents a segmentation result.
/// </summary>
public record SegmentationResult(
    long FrameNumber,
    IReadOnlyList<SegmentationMask> Masks,
    DateTime Timestamp
);

/// <summary>
/// Represents a segmentation mask for a detected object.
/// </summary>
public record SegmentationMask(
    string Label,
    float Confidence,
    byte[] MaskData,
    int Width,
    int Height
);

/// <summary>
/// Represents a graphics overlay frame.
/// </summary>
public record GraphicsFrame(
    long FrameNumber,
    byte[] Data,
    DateTime Timestamp
);
