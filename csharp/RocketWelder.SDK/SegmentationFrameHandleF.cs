using System;
using System.Buffers;
using ModelingEvolution.Drawing;
using RocketWelder.SDK.Protocols;

namespace RocketWelder.SDK;

/// <summary>
/// Handle to a parsed float-precision segmentation frame with pooled buffers.
/// MUST be disposed to return buffers to ArrayPool.
/// </summary>
/// <remarks>
/// Each instance's <see cref="SegmentationInstanceF.Points"/> is a
/// <see cref="ReadOnlyMemory{T}"/> slice into the shared pooled
/// <see cref="Point{T}"/> buffer — zero-copy polygon construction via
/// <c>new Polygon&lt;float&gt;(instance.Points)</c>.
/// <para>
/// Polygons created from the handle are valid only while the handle is not disposed.
/// </para>
/// </remarks>
public sealed class SegmentationFrameHandleF : IDisposable
{
    private readonly Point<float>[] _pointsBuffer;
    private readonly int _pointsCount;
    private readonly SegmentationInstanceF[] _instancesBuffer;
    private readonly int _instanceCount;
    private bool _disposed;

    public ulong FrameId { get; }
    public uint Width { get; }
    public uint Height { get; }

    /// <summary>
    /// Segmentation instances in this frame.
    /// Each instance's Points is a slice of the shared pooled buffer.
    /// </summary>
    public ReadOnlySpan<SegmentationInstanceF> Instances => _instancesBuffer.AsSpan(0, _instanceCount);

    internal SegmentationFrameHandleF(
        ulong frameId, uint width, uint height,
        Point<float>[] pointsBuffer, int pointsCount,
        SegmentationInstanceF[] instancesBuffer, int instanceCount)
    {
        FrameId = frameId;
        Width = width;
        Height = height;
        _pointsBuffer = pointsBuffer;
        _pointsCount = pointsCount;
        _instancesBuffer = instancesBuffer;
        _instanceCount = instanceCount;
    }

    /// <summary>
    /// Creates a pooled copy of a <see cref="SegmentationFrameF"/>.
    /// The caller owns the returned handle and must dispose it.
    /// </summary>
    public static SegmentationFrameHandleF FromFrame(in SegmentationFrameF frame)
    {
        var instances = frame.Instances;

        int totalPoints = 0;
        for (int i = 0; i < instances.Count; i++)
            totalPoints += instances[i].Points.Length;

        var pointsBuffer = ArrayPool<Point<float>>.Shared.Rent(Math.Max(totalPoints, 1));
        var instancesBuffer = ArrayPool<SegmentationInstanceF>.Shared.Rent(Math.Max(instances.Count, 1));

        try
        {
            int pointOffset = 0;
            for (int i = 0; i < instances.Count; i++)
            {
                var src = instances[i];
                var pts = src.Points;
                pts.Span.CopyTo(pointsBuffer.AsSpan(pointOffset, pts.Length));

                instancesBuffer[i] = new SegmentationInstanceF(
                    src.ClassId,
                    src.InstanceId,
                    src.Confidence,
                    pointsBuffer.AsMemory(pointOffset, pts.Length));

                pointOffset += pts.Length;
            }

            return new SegmentationFrameHandleF(
                frame.FrameId, frame.Width, frame.Height,
                pointsBuffer, totalPoints,
                instancesBuffer, instances.Count);
        }
        catch
        {
            ArrayPool<Point<float>>.Shared.Return(pointsBuffer);
            ArrayPool<SegmentationInstanceF>.Shared.Return(instancesBuffer);
            throw;
        }
    }

    /// <summary>
    /// Creates a pooled copy of this handle. The caller owns the returned handle
    /// and must dispose it independently.
    /// This is the copy-on-read path for <c>RocketWelder.SDK.Runtime.IDataProvider{T}.GetLatest</c>.
    /// </summary>
    public SegmentationFrameHandleF Copy()
    {
        var pointsBuffer = ArrayPool<Point<float>>.Shared.Rent(Math.Max(_pointsCount, 1));
        var instancesBuffer = ArrayPool<SegmentationInstanceF>.Shared.Rent(Math.Max(_instanceCount, 1));

        try
        {
            // Copy all points in one shot
            _pointsBuffer.AsSpan(0, _pointsCount).CopyTo(pointsBuffer);

            // Rebuild instance slices pointing into the NEW buffer
            // The offsets are the same — just re-slice from the new array
            int pointOffset = 0;
            for (int i = 0; i < _instanceCount; i++)
            {
                var src = _instancesBuffer[i];
                int count = src.Points.Length;

                instancesBuffer[i] = new SegmentationInstanceF(
                    src.ClassId,
                    src.InstanceId,
                    src.Confidence,
                    pointsBuffer.AsMemory(pointOffset, count));

                pointOffset += count;
            }

            return new SegmentationFrameHandleF(
                FrameId, Width, Height,
                pointsBuffer, _pointsCount,
                instancesBuffer, _instanceCount);
        }
        catch
        {
            ArrayPool<Point<float>>.Shared.Return(pointsBuffer);
            ArrayPool<SegmentationInstanceF>.Shared.Return(instancesBuffer);
            throw;
        }
    }

    /// <summary>
    /// Finds the first segmentation instance with the given class ID, or null if not found.
    /// </summary>
    public SegmentationInstanceF? FindFirst(byte classId)
    {
        var instances = Instances;
        for (int i = 0; i < instances.Length; i++)
        {
            if (instances[i].ClassId == classId)
                return instances[i];
        }
        return null;
    }

    /// <summary>
    /// Returns pooled buffers. MUST be called when done with the frame.
    /// Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_pointsBuffer != null)
            ArrayPool<Point<float>>.Shared.Return(_pointsBuffer, clearArray: false);
        if (_instancesBuffer != null)
            ArrayPool<SegmentationInstanceF>.Shared.Return(_instancesBuffer, clearArray: false);
    }
}
