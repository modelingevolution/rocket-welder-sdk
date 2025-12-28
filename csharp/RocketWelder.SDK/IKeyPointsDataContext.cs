using RocketWelder.SDK.Protocols;

namespace RocketWelder.SDK;

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
    /// <param name="point">Keypoint from schema definition (uses Id property)</param>
    /// <param name="x">X coordinate in pixels</param>
    /// <param name="y">Y coordinate in pixels</param>
    /// <param name="confidence">Detection confidence (0.0 - 1.0)</param>
    void Add(Keypoint point, int x, int y, float confidence);
}
