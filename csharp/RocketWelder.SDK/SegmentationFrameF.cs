using System.Collections.Generic;

namespace RocketWelder.SDK;

/// <summary>
/// Float-precision segmentation frame containing <see cref="SegmentationInstanceF"/> results.
/// </summary>
/// <param name="FrameId">Frame identifier for temporal ordering.</param>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="Instances">Segmentation instances detected in this frame.</param>
public readonly record struct SegmentationFrameF(
    ulong FrameId,
    uint Width,
    uint Height,
    IReadOnlyList<SegmentationInstanceF> Instances);
