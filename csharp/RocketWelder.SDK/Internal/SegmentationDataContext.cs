using System;
using System.Drawing;

namespace RocketWelder.SDK.Internal;

/// <summary>
/// Unit of Work implementation for segmentation data.
/// Wraps an <see cref="ISegmentationResultWriter"/> and commits on Dispose.
/// </summary>
internal sealed class SegmentationDataContext : ISegmentationDataContext
{
    private readonly ISegmentationResultWriter _writer;
    private bool _disposed;

    public SegmentationDataContext(ISegmentationResultWriter writer, ulong frameId)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        FrameId = frameId;
    }

    public ulong FrameId { get; }

    public void Add(SegmentClass segmentClass, byte instanceId, float confidence, ReadOnlySpan<Point> points)
    {
        _writer.Append(segmentClass.ClassId, instanceId, confidence, points);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writer.Dispose();
    }
}
