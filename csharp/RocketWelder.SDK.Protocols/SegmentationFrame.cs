using System.Collections.Generic;

namespace RocketWelder.SDK.Protocols;

/// <summary>
/// Represents a decoded segmentation frame containing instance segmentation results.
/// Used for round-trip testing of segmentation protocol encoding/decoding.
/// </summary>
/// <param name="FrameId">Frame identifier for temporal ordering.</param>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="Instances">Segmentation instances detected in this frame.</param>
public readonly record struct SegmentationFrame(
    ulong FrameId,
    uint Width,
    uint Height,
    IReadOnlyList<SegmentationInstance> Instances);
