using System;
using System.Drawing;

namespace RocketWelder.SDK.Internal;

/// <summary>
/// Unit of Work implementation for segmentation data.
/// Wraps an <see cref="ISegmentationResultWriter"/> and auto-commits on Commit().
/// </summary>
internal sealed class SegmentationDataContext : ISegmentationDataContext
{
    private readonly ISegmentationResultWriter _writer;

    public SegmentationDataContext(ISegmentationResultWriter writer, ulong frameId)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        FrameId = frameId;
    }

    public ulong FrameId { get; }

    public void Add(SegmentClass segmentClass, byte instanceId, ReadOnlySpan<Point> points)
    {
        _writer.Append(segmentClass.ClassId, instanceId, points);
    }

    /// <summary>
    /// Commits the data context by disposing the underlying writer.
    /// Called automatically when the processing delegate returns.
    /// </summary>
    internal void Commit()
    {
        _writer.Dispose();
    }
}
