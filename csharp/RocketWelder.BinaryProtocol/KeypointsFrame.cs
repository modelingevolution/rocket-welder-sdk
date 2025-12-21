namespace RocketWelder.BinaryProtocol;

/// <summary>
/// Represents a decoded keypoints frame containing pose estimation results.
/// Used for round-trip testing of keypoints protocol encoding/decoding.
/// </summary>
public readonly struct KeypointsFrame
{
    /// <summary>
    /// Frame identifier for temporal ordering.
    /// </summary>
    public ulong FrameId { get; init; }

    /// <summary>
    /// True if this is a master frame (absolute positions),
    /// False if this is a delta frame (positions relative to previous frame).
    /// </summary>
    public bool IsMasterFrame { get; init; }

    /// <summary>
    /// Keypoints detected in this frame.
    /// </summary>
    public Keypoint[] Keypoints { get; init; }

    public KeypointsFrame(ulong frameId, bool isMasterFrame, Keypoint[] keypoints)
    {
        FrameId = frameId;
        IsMasterFrame = isMasterFrame;
        Keypoints = keypoints;
    }
}
