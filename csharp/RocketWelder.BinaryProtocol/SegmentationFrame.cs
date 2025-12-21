namespace RocketWelder.BinaryProtocol;

/// <summary>
/// Represents a decoded segmentation frame containing instance segmentation results.
/// Used for round-trip testing of segmentation protocol encoding/decoding.
/// </summary>
public readonly struct SegmentationFrame
{
    /// <summary>
    /// Frame identifier for temporal ordering.
    /// </summary>
    public ulong FrameId { get; init; }

    /// <summary>
    /// Frame width in pixels.
    /// </summary>
    public uint Width { get; init; }

    /// <summary>
    /// Frame height in pixels.
    /// </summary>
    public uint Height { get; init; }

    /// <summary>
    /// Segmentation instances detected in this frame.
    /// </summary>
    public SegmentationInstance[] Instances { get; init; }

    public SegmentationFrame(ulong frameId, uint width, uint height, SegmentationInstance[] instances)
    {
        FrameId = frameId;
        Width = width;
        Height = height;
        Instances = instances;
    }
}
