using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using RocketWelder.SDK.Protocols;

namespace RocketWelder.SDK;

/// <summary>
/// DOM-style document for segmentation results.
/// Loads entire file into memory and builds an index for O(1) frame access.
/// Parse-on-demand with pooled memory for zero-allocation playback.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// using var doc = await SegmentationDocument.LoadAsync("segmentation.bin");
///
/// // Random access by FrameId
/// using var frame = doc.GetFrame(frameId);
/// foreach (var instance in frame.Instances)
///     RenderPolygon(instance.ClassId, instance.Points);
/// // frame.Dispose() returns buffers to ArrayPool
/// </code>
/// </remarks>
public sealed class SegmentationDocument : IDisposable
{
    private readonly byte[] _data;
    private readonly Dictionary<ulong, FrameLocation> _index;
    private bool _disposed;

    // Same limit as SegmentationResultSource
    private const int MaxPointsPerInstance = 10_000_000;
    private const int MaxInstancesPerFrame = 10_000;

    private readonly record struct FrameLocation(int Offset, int Length);

    private SegmentationDocument(byte[] data, Dictionary<ulong, FrameLocation> index)
    {
        _data = data;
        _index = index;
    }

    /// <summary>
    /// Number of frames in the document.
    /// </summary>
    public int FrameCount => _index.Count;

    /// <summary>
    /// All frame IDs in the document.
    /// </summary>
    public IEnumerable<ulong> FrameIds => _index.Keys;

    /// <summary>
    /// Check if a frame exists in the document.
    /// </summary>
    public bool ContainsFrame(ulong frameId) => _index.ContainsKey(frameId);

    #region Factory Methods

    /// <summary>
    /// Load a segmentation document from a file.
    /// </summary>
    public static async Task<SegmentationDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var data = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var index = BuildIndex(data);
        return new SegmentationDocument(data, index);
    }

    /// <summary>
    /// Load a segmentation document from a stream.
    /// </summary>
    public static async Task<SegmentationDocument> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] data;
        if (stream.CanSeek)
        {
            // Optimize for seekable streams - single allocation
            data = new byte[stream.Length - stream.Position];
            await stream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Non-seekable streams: must buffer to determine size
            // ToArray() unavoidable here - we need a right-sized array we own
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            data = ms.ToArray();
        }
        var index = BuildIndex(data);
        return new SegmentationDocument(data, index);
    }

    /// <summary>
    /// Load a segmentation document from a byte array.
    /// </summary>
    public static SegmentationDocument Load(byte[] data)
    {
        var index = BuildIndex(data);
        return new SegmentationDocument(data, index);
    }

    #endregion

    #region Frame Access

    /// <summary>
    /// Get a frame by ID. Returns a handle that must be disposed to return pooled buffers.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Frame not found</exception>
    public SegmentationFrameHandle GetFrame(ulong frameId)
    {
        ThrowIfDisposed();

        if (!_index.TryGetValue(frameId, out var location))
            throw new KeyNotFoundException($"Frame {frameId} not found in document");

        return ParseFrame(_data, location.Offset, location.Length);
    }

    /// <summary>
    /// Try to get a frame by ID.
    /// </summary>
    public bool TryGetFrame(ulong frameId, out SegmentationFrameHandle? frame)
    {
        ThrowIfDisposed();

        if (!_index.TryGetValue(frameId, out var location))
        {
            frame = null;
            return false;
        }

        frame = ParseFrame(_data, location.Offset, location.Length);
        return true;
    }

    #endregion

    #region Index Building

    private static Dictionary<ulong, FrameLocation> BuildIndex(byte[] data)
    {
        var index = new Dictionary<ulong, FrameLocation>();
        int position = 0;

        using var stream = new MemoryStream(data, writable: false);

        while (position < data.Length)
        {
            int frameStart = position;
            stream.Position = position;

            // Read payload length (varint)
            uint payloadLength = stream.ReadVarint();
            int headerSize = (int)(stream.Position - frameStart);

            // Read FrameId (first 8 bytes of payload)
            if (position + headerSize + 8 > data.Length)
                throw new InvalidDataException("Truncated frame: cannot read FrameId");

            ulong frameId = BinaryPrimitives.ReadUInt64LittleEndian(
                data.AsSpan(position + headerSize, 8));

            // Record frame location (including length prefix)
            int totalFrameSize = headerSize + (int)payloadLength;
            index[frameId] = new FrameLocation(frameStart, totalFrameSize);

            // Skip to next frame
            position += totalFrameSize;
        }

        return index;
    }

    #endregion

    #region Frame Parsing

    private static SegmentationFrameHandle ParseFrame(byte[] data, int offset, int length)
    {
        // Use MemoryStream directly on the data array - no allocation
        using var stream = new MemoryStream(data, offset, length, writable: false);

        // Skip length prefix
        stream.ReadVarint();

        // Read header: [FrameId: 8B LE][Width: varint][Height: varint]
        Span<byte> frameIdBytes = stackalloc byte[8];
        if (stream.Read(frameIdBytes) != 8)
            throw new EndOfStreamException("Failed to read FrameId");

        ulong frameId = BinaryPrimitives.ReadUInt64LittleEndian(frameIdBytes);
        uint width = stream.ReadVarint();
        uint height = stream.ReadVarint();

        // First pass: count instances and total points
        long instancesStart = stream.Position;
        int instanceCount = 0;
        long totalPoints = 0;

        while (stream.Position < stream.Length)
        {
            int classIdByte = stream.ReadByte();
            if (classIdByte == -1) break;

            int instanceIdByte = stream.ReadByte();
            if (instanceIdByte == -1)
                throw new EndOfStreamException("Unexpected end of stream reading instanceId");

            // Skip confidence (2 bytes)
            if (stream.Read(stackalloc byte[2]) != 2)
                throw new EndOfStreamException("Unexpected end of stream reading confidence");

            uint pointCount = stream.ReadVarint();
            if (pointCount > MaxPointsPerInstance)
                throw new InvalidDataException($"Point count {pointCount} exceeds maximum");

            instanceCount++;
            if (instanceCount > MaxInstancesPerFrame)
                throw new InvalidDataException($"Instance count exceeds maximum {MaxInstancesPerFrame}");

            totalPoints += (int)pointCount;

            // Skip point data (2 varints per point)
            for (int i = 0; i < pointCount; i++)
            {
                stream.ReadVarint(); // x or dx
                stream.ReadVarint(); // y or dy
            }
        }

        // Validate totalPoints fits in int before renting from ArrayPool
        if (totalPoints > int.MaxValue)
            throw new InvalidDataException($"Total point count {totalPoints} exceeds maximum array size");

        // Rent pooled arrays with exact capacity
        int totalPointsInt = (int)totalPoints;
        var pointsBuffer = ArrayPool<Point>.Shared.Rent(totalPointsInt);
        var instancesBuffer = ArrayPool<SegmentationInstance>.Shared.Rent(instanceCount);

        try
        {
            // Second pass: parse and fill
            stream.Position = instancesStart;
            int pointOffset = 0;
            int instanceIndex = 0;

            while (stream.Position < stream.Length)
            {
                int classIdByte = stream.ReadByte();
                if (classIdByte == -1) break;

                byte classId = (byte)classIdByte;
                byte instanceId = (byte)stream.ReadByte();

                // Read confidence (2 bytes LE)
                Span<byte> confidenceBytes = stackalloc byte[2];
                stream.Read(confidenceBytes);
                ushort confidenceRaw = BinaryPrimitives.ReadUInt16LittleEndian(confidenceBytes);
                var confidence = (Protocols.Confidence)confidenceRaw;

                uint pointCount = stream.ReadVarint();

                int instancePointStart = pointOffset;

                if (pointCount > 0)
                {
                    // First point (absolute, zigzag encoded)
                    int x = stream.ReadVarint().ZigZagDecode();
                    int y = stream.ReadVarint().ZigZagDecode();
                    pointsBuffer[pointOffset++] = new Point(x, y);

                    // Remaining points (delta encoded)
                    for (int i = 1; i < pointCount; i++)
                    {
                        x += stream.ReadVarint().ZigZagDecode();
                        y += stream.ReadVarint().ZigZagDecode();
                        pointsBuffer[pointOffset++] = new Point(x, y);
                    }
                }

                // Create instance with Memory slice into pooled array
                instancesBuffer[instanceIndex++] = new SegmentationInstance(
                    classId,
                    instanceId,
                    confidence,
                    pointsBuffer.AsMemory(instancePointStart, (int)pointCount));
            }

            return new SegmentationFrameHandle(
                frameId, width, height,
                pointsBuffer, totalPointsInt,
                instancesBuffer, instanceCount);
        }
        catch
        {
            // Return buffers on error
            ArrayPool<Point>.Shared.Return(pointsBuffer);
            ArrayPool<SegmentationInstance>.Shared.Return(instancesBuffer);
            throw;
        }
    }

    #endregion

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SegmentationDocument));
    }

    public void Dispose()
    {
        _disposed = true;
        // _data is managed, will be GC'd
    }
}

/// <summary>
/// Handle to a parsed segmentation frame with pooled buffers.
/// MUST be disposed to return buffers to ArrayPool.
/// </summary>
/// <remarks>
/// This is a class (not struct) to prevent double-dispose issues when copied.
/// Each handle owns its pooled buffers and returns them exactly once on disposal.
/// </remarks>
public sealed class SegmentationFrameHandle : IDisposable
{
    private readonly Point[] _pointsBuffer;
    private readonly int _pointsCount;
    private readonly SegmentationInstance[] _instancesBuffer;
    private readonly int _instanceCount;
    private bool _disposed;

    public ulong FrameId { get; }
    public uint Width { get; }
    public uint Height { get; }

    /// <summary>
    /// Segmentation instances in this frame.
    /// Each instance's Points is a slice of the shared pooled buffer.
    /// </summary>
    public ReadOnlySpan<SegmentationInstance> Instances => _instancesBuffer.AsSpan(0, _instanceCount);

    internal SegmentationFrameHandle(
        ulong frameId, uint width, uint height,
        Point[] pointsBuffer, int pointsCount,
        SegmentationInstance[] instancesBuffer, int instanceCount)
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
    /// Returns pooled buffers. MUST be called when done with the frame.
    /// Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_pointsBuffer != null)
            ArrayPool<Point>.Shared.Return(_pointsBuffer, clearArray: false);
        if (_instancesBuffer != null)
            ArrayPool<SegmentationInstance>.Shared.Return(_instancesBuffer, clearArray: false);
    }
}
