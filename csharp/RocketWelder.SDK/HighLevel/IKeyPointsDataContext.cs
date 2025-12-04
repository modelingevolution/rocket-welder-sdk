namespace RocketWelder.SDK.HighLevel;

/// <summary>
/// Unit of Work for keypoints data, scoped to a single frame.
/// Auto-commits when the delegate returns.
/// </summary>
public interface IKeyPointsDataContext
{
    /// <summary>
    /// Current frame ID.
    /// </summary>
    ulong FrameId { get; }

    /// <summary>
    /// Adds a keypoint detection for this frame.
    /// </summary>
    /// <param name="point">KeyPoint from schema definition</param>
    /// <param name="x">X coordinate in pixels</param>
    /// <param name="y">Y coordinate in pixels</param>
    /// <param name="confidence">Detection confidence (0.0 - 1.0)</param>
    void Add(KeyPoint point, int x, int y, float confidence);
}
