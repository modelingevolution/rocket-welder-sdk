using System;
using System.Drawing;

namespace RocketWelder.SDK.HighLevel;

/// <summary>
/// Unit of Work for segmentation data, scoped to a single frame.
/// Auto-commits when the delegate returns.
/// </summary>
public interface ISegmentationDataContext
{
    /// <summary>
    /// Current frame ID.
    /// </summary>
    ulong FrameId { get; }

    /// <summary>
    /// Adds a segmentation instance for this frame.
    /// </summary>
    /// <param name="segmentClass">SegmentClass from schema definition</param>
    /// <param name="instanceId">Instance ID (for multiple instances of same class)</param>
    /// <param name="points">Contour points defining the instance boundary</param>
    void Add(SegmentClass segmentClass, byte instanceId, ReadOnlySpan<Point> points);
}
