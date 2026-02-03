using System;
using System.Drawing;

namespace RocketWelder.SDK;

/// <summary>
/// Unit of Work for segmentation data, scoped to a single frame.
/// Dispose to commit data.
/// </summary>
public interface ISegmentationDataContext : IDisposable
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
    /// <param name="confidence">Detection confidence score (0.0-1.0)</param>
    /// <param name="points">Contour points defining the instance boundary</param>
    void Add(SegmentClass segmentClass, byte instanceId, float confidence, ReadOnlySpan<Point> points);
}
