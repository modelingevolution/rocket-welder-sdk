using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RocketWelder.SDK.Transport;
using RocketWelder.BinaryProtocol;

namespace RocketWelder.SDK;

// ============================================================================
// KeyPoints Protocol - Binary format for efficient keypoint storage
// Supports master/delta frame compression for temporal sequences
// ============================================================================

/// <summary>
/// Sink for writing keypoints data.
/// Transport-agnostic: works with files, TCP, WebSocket, NNG, etc.
/// </summary>
public interface IKeyPointsSink : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Create a writer for the current frame.
    /// Sink decides whether to write master or delta frame.
    /// </summary>
    IKeyPointsWriter CreateWriter(ulong frameId);
}

/// <summary>
/// Writes keypoints data for a single frame.
/// Lightweight writer - create one per frame via IKeyPointsSink.
/// </summary>
public interface IKeyPointsWriter : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Append a keypoint to this frame.
    /// </summary>
    void Append(int keypointId, int x, int y, float confidence);

    /// <summary>
    /// Append a keypoint to this frame.
    /// </summary>
    void Append(int keypointId, Point p, float confidence);

    /// <summary>
    /// Append a keypoint to this frame asynchronously.
    /// </summary>
    Task AppendAsync(int keypointId, int x, int y, float confidence);

    /// <summary>
    /// Append a keypoint to this frame asynchronously.
    /// </summary>
    Task AppendAsync(int keypointId, Point p, float confidence);
}

/// <summary>
/// Streaming reader for keypoints via IAsyncEnumerable.
/// Designed for real-time streaming over TCP/WebSocket/NNG.
/// </summary>
public interface IKeyPointsSource : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Stream frames as they arrive from the transport.
    /// Supports cancellation and backpressure.
    /// </summary>
    IAsyncEnumerable<KeyPointsFrame> ReadFramesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A single keypoints frame with all keypoints.
/// </summary>
public readonly struct KeyPointsFrame
{
    public ulong FrameId { get; }
    public bool IsDelta { get; }
    public IReadOnlyList<KeyPoint> KeyPoints { get; }

    public KeyPointsFrame(ulong frameId, bool isDelta, IReadOnlyList<KeyPoint> keyPoints)
    {
        FrameId = frameId;
        IsDelta = isDelta;
        KeyPoints = keyPoints;
    }
}

/// <summary>
/// A single keypoint with ID, position, and confidence.
/// </summary>
public readonly struct KeyPoint
{
    public int Id { get; }
    public int X { get; }
    public int Y { get; }
    public float Confidence { get; }

    public KeyPoint(int id, int x, int y, float confidence)
    {
        Id = id;
        X = x;
        Y = y;
        Confidence = confidence;
    }

    public Point ToPoint() => new Point(X, Y);
}

/// <summary>
/// Streaming reader for keypoints.
/// Reads frames from IFrameSource and yields them via IAsyncEnumerable.
/// Handles master/delta frame decoding automatically.
/// </summary>
public class KeyPointsSource : IKeyPointsSource
{
    private const byte MasterFrameType = 0x00;
    private const byte DeltaFrameType = 0x01;

    private readonly IFrameSource _frameSource;
    private Dictionary<int, (Point point, ushort confidence)>? _previousFrame;
    private bool _disposed;

    public KeyPointsSource(IFrameSource frameSource)
    {
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
    }

    public async IAsyncEnumerable<KeyPointsFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            var frameData = await _frameSource.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            if (frameData.IsEmpty)
                yield break;

            var frame = ParseFrame(frameData);
            yield return frame;
        }
    }

    private KeyPointsFrame ParseFrame(ReadOnlyMemory<byte> frameData)
    {
        // Zero-copy: get underlying array segment without allocation
        if (!MemoryMarshal.TryGetArray(frameData, out var segment))
            throw new InvalidOperationException("Cannot get array segment from memory");

        using var stream = new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false);

        // Read frame type
        int frameTypeByte = stream.ReadByte();
        if (frameTypeByte == -1)
            throw new EndOfStreamException("Unexpected end of frame");

        byte frameType = (byte)frameTypeByte;
        bool isDelta = frameType == DeltaFrameType;

        // Read frame ID (8 bytes LE)
        Span<byte> frameIdBytes = stackalloc byte[8];
        if (stream.Read(frameIdBytes) != 8)
            throw new EndOfStreamException("Failed to read FrameId");

        ulong frameId = BinaryPrimitives.ReadUInt64LittleEndian(frameIdBytes);

        // Read keypoint count
        uint keypointCount = stream.ReadVarint();

        // Read keypoints
        var keypoints = new List<KeyPoint>((int)keypointCount);
        var currentFrame = new Dictionary<int, (Point point, ushort confidence)>();

        if (isDelta && _previousFrame != null)
        {
            // Delta frame - read deltas from previous frame
            for (int i = 0; i < keypointCount; i++)
            {
                int keypointId = (int)stream.ReadVarint();
                int deltaX = stream.ReadVarint().ZigZagDecode();
                int deltaY = stream.ReadVarint().ZigZagDecode();
                int deltaConfidence = stream.ReadVarint().ZigZagDecode();

                // Apply delta to previous value (or use absolute if new keypoint)
                int x, y;
                ushort confidence;

                if (_previousFrame.TryGetValue(keypointId, out var prev))
                {
                    x = prev.point.X + deltaX;
                    y = prev.point.Y + deltaY;
                    confidence = (ushort)(prev.confidence + deltaConfidence);
                }
                else
                {
                    // New keypoint - delta is actually absolute value
                    x = deltaX;
                    y = deltaY;
                    confidence = (ushort)deltaConfidence;
                }

                keypoints.Add(new KeyPoint(keypointId, x, y, confidence / 10000f));
                currentFrame[keypointId] = (new Point(x, y), confidence);
            }
        }
        else
        {
            // Master frame - read absolute values
            for (int i = 0; i < keypointCount; i++)
            {
                int keypointId = (int)stream.ReadVarint();

                // Read coordinates (4 bytes each, LE)
                Span<byte> coordBytes = stackalloc byte[4];
                stream.Read(coordBytes);
                int x = BinaryPrimitives.ReadInt32LittleEndian(coordBytes);
                stream.Read(coordBytes);
                int y = BinaryPrimitives.ReadInt32LittleEndian(coordBytes);

                // Read confidence (2 bytes, LE)
                Span<byte> confBytes = stackalloc byte[2];
                stream.Read(confBytes);
                ushort confidence = BinaryPrimitives.ReadUInt16LittleEndian(confBytes);

                keypoints.Add(new KeyPoint(keypointId, x, y, confidence / 10000f));
                currentFrame[keypointId] = (new Point(x, y), confidence);
            }
        }

        // Update previous frame for next delta decoding
        _previousFrame = currentFrame;

        return new KeyPointsFrame(frameId, isDelta, keypoints);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _frameSource.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _frameSource.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// In-memory representation of keypoints series for efficient querying.
/// </summary>
public class KeyPointsSeries
{
    private readonly Dictionary<ulong, SortedDictionary<int, (Point point, float confidence)>> _index;

    /// <summary>
    /// Version of the keypoints algorithm or model.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Name of AI model or assembly that generated the keypoints.
    /// </summary>
    public string ComputeModuleName { get; }

    /// <summary>
    /// Definition mapping: keypoint name -> keypoint ID
    /// </summary>
    public IReadOnlyDictionary<string, int> Points { get; }

    /// <summary>
    /// Get all frame IDs in the series.
    /// </summary>
    public IReadOnlyCollection<ulong> FrameIds => _index.Keys;

    internal KeyPointsSeries(
        string version,
        string computeModuleName,
        IReadOnlyDictionary<string, int> points,
        Dictionary<ulong, SortedDictionary<int, (Point, float)>> index)
    {
        Version = version;
        ComputeModuleName = computeModuleName;
        Points = points;
        _index = index;
    }

    /// <summary>
    /// Get all keypoints for a specific frame.
    /// Returns null if frame not found.
    /// </summary>
    public IReadOnlyDictionary<int, (Point point, float confidence)>? GetFrame(ulong frameId)
    {
        return _index.TryGetValue(frameId, out var frame) ? frame : null;
    }

    /// <summary>
    /// Get trajectory of a specific keypoint across all frames.
    /// Returns enumerable of (frameId, point, confidence) tuples.
    /// Lazily evaluated - efficient for large series.
    /// </summary>
    public IEnumerable<(ulong frameId, Point point, float confidence)> GetKeyPointTrajectory(int keypointId)
    {
        foreach (var (frameId, keypoints) in _index)
        {
            if (keypoints.TryGetValue(keypointId, out var data))
            {
                yield return (frameId, data.point, data.confidence);
            }
        }
    }

    /// <summary>
    /// Get trajectory of a specific keypoint by name across all frames.
    /// Returns enumerable of (frameId, point, confidence) tuples.
    /// Lazily evaluated - efficient for large series.
    /// </summary>
    public IEnumerable<(ulong frameId, Point point, float confidence)> GetKeyPointTrajectory(string keypointName)
    {
        if (!Points.TryGetValue(keypointName, out var keypointId))
        {
            yield break;
        }

        foreach (var item in GetKeyPointTrajectory(keypointId))
        {
            yield return item;
        }
    }

    /// <summary>
    /// Check if a frame exists in the series.
    /// </summary>
    public bool ContainsFrame(ulong frameId) => _index.ContainsKey(frameId);

    /// <summary>
    /// Get keypoint position and confidence at specific frame.
    /// Returns null if frame or keypoint not found.
    /// </summary>
    public (Point point, float confidence)? GetKeyPoint(ulong frameId, int keypointId)
    {
        if (_index.TryGetValue(frameId, out var keypoints) &&
            keypoints.TryGetValue(keypointId, out var data))
        {
            return data;
        }
        return null;
    }

    /// <summary>
    /// Get keypoint position and confidence at specific frame by name.
    /// Returns null if frame or keypoint not found.
    /// </summary>
    public (Point point, float confidence)? GetKeyPoint(ulong frameId, string keypointName)
    {
        if (Points.TryGetValue(keypointName, out var keypointId))
        {
            return GetKeyPoint(frameId, keypointId);
        }
        return null;
    }
}

// ============================================================================
// KeyPointsWriter - Writes single frame (buffered, then sent via IFrameSink)
// ============================================================================

internal class KeyPointsWriter : IKeyPointsWriter
{
    // Frame types
    private const byte MasterFrameType = 0x00;
    private const byte DeltaFrameType = 0x01;

    private readonly ulong _frameId;
    private readonly IFrameSink _frameSink;
    private readonly MemoryStream _buffer;
    private readonly bool _isDelta;
    private readonly Dictionary<int, (Point point, ushort confidence)>? _previousFrame;
    private readonly List<(int id, Point point, ushort confidence)> _keypoints = new();
    private readonly Action<Dictionary<int, (Point, ushort)>>? _onFrameWritten;
    private bool _disposed = false;

    public KeyPointsWriter(
        ulong frameId,
        IFrameSink frameSink,
        bool isDelta,
        Dictionary<int, (Point, ushort)>? previousFrame,
        Action<Dictionary<int, (Point, ushort)>>? onFrameWritten = null)
    {
        _frameId = frameId;
        _frameSink = frameSink ?? throw new ArgumentNullException(nameof(frameSink));
        _buffer = new MemoryStream();
        _isDelta = isDelta;
        _previousFrame = previousFrame;
        _onFrameWritten = onFrameWritten;
    }

    public void Append(int keypointId, int x, int y, float confidence)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(KeyPointsWriter));

        // Convert confidence from float (0.0-1.0) to ushort (0-10000)
        ushort confidenceUshort = (ushort)Math.Clamp(confidence * 10000f, 0, 10000);
        _keypoints.Add((keypointId, new Point(x, y), confidenceUshort));
    }

    public void Append(int keypointId, Point p, float confidence)
    {
        Append(keypointId, p.X, p.Y, confidence);
    }

    public Task AppendAsync(int keypointId, int x, int y, float confidence)
    {
        Append(keypointId, x, y, confidence);
        return Task.CompletedTask;
    }

    public Task AppendAsync(int keypointId, Point p, float confidence)
    {
        Append(keypointId, p, confidence);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Write frame to buffer
        WriteFrame();

        // Send complete frame via sink (zero-copy using GetBuffer)
        _frameSink.WriteFrame(new ReadOnlySpan<byte>(_buffer.GetBuffer(), 0, (int)_buffer.Length));

        // Update previous frame state
        UpdatePreviousFrameState();

        _buffer.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Write frame to buffer (sync - buffer writes are fast)
        WriteFrame();

        // Send complete frame via sink (zero-copy using GetBuffer)
        await _frameSink.WriteFrameAsync(new ReadOnlyMemory<byte>(_buffer.GetBuffer(), 0, (int)_buffer.Length)).ConfigureAwait(false);

        // Update previous frame state
        UpdatePreviousFrameState();

        await _buffer.DisposeAsync().ConfigureAwait(false);
    }

    private void UpdatePreviousFrameState()
    {
        if (_onFrameWritten != null)
        {
            var frameState = new Dictionary<int, (Point, ushort)>();
            foreach (var (id, point, confidence) in _keypoints)
            {
                frameState[id] = (point, confidence);
            }
            _onFrameWritten(frameState);
        }
    }

    private void WriteFrame()
    {
        // Write frame type
        _buffer.WriteByte(_isDelta ? DeltaFrameType : MasterFrameType);

        // Write frame ID
        Span<byte> frameIdBytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(frameIdBytes, _frameId);
        _buffer.Write(frameIdBytes);

        // Write keypoint count
        _buffer.WriteVarint((uint)_keypoints.Count);

        if (_isDelta && _previousFrame != null)
        {
            WriteDeltaKeypoints();
        }
        else
        {
            WriteMasterKeypoints();
        }
    }

    private void WriteMasterKeypoints()
    {
        foreach (var (id, point, confidence) in _keypoints)
        {
            // Write keypoint ID
            _buffer.WriteVarint((uint)id);

            // Write absolute coordinates
            Span<byte> coords = stackalloc byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(coords, point.X);
            BinaryPrimitives.WriteInt32LittleEndian(coords[4..], point.Y);
            _buffer.Write(coords);

            // Write confidence
            Span<byte> confBytes = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(confBytes, confidence);
            _buffer.Write(confBytes);
        }
    }

    private void WriteDeltaKeypoints()
    {
        foreach (var (id, point, confidence) in _keypoints)
        {
            // Write keypoint ID
            _buffer.WriteVarint((uint)id);

            // Calculate deltas
            if (_previousFrame!.TryGetValue(id, out var prev))
            {
                int deltaX = point.X - prev.point.X;
                int deltaY = point.Y - prev.point.Y;
                int deltaConf = confidence - prev.confidence;

                _buffer.WriteVarint(deltaX.ZigZagEncode());
                _buffer.WriteVarint(deltaY.ZigZagEncode());
                _buffer.WriteVarint(deltaConf.ZigZagEncode());
            }
            else
            {
                // Keypoint didn't exist in previous frame - write as absolute
                _buffer.WriteVarint(point.X.ZigZagEncode());
                _buffer.WriteVarint(point.Y.ZigZagEncode());
                _buffer.WriteVarint(((int)confidence).ZigZagEncode());
            }
        }
    }
}

// ============================================================================
// KeyPointsSink - Transport-agnostic keypoints sink
// ============================================================================

/// <summary>
/// KeyPoints sink supporting any transport (file, TCP, WebSocket, NNG, etc.).
/// Uses IFrameSink for transport independence.
/// </summary>
public class KeyPointsSink : IKeyPointsSink
{
    private readonly IFrameSink _frameSink;
    private readonly int _masterFrameInterval;
    private readonly bool _ownsSink;
    private Dictionary<int, (Point, ushort)>? _previousFrame;
    private ulong _frameCount = 0;
    private bool _disposed = false;

    /// <summary>
    /// Creates a keypoints sink from a Stream (convenience constructor).
    /// Internally creates a StreamFrameSink.
    /// </summary>
    /// <param name="stream">Stream to write to</param>
    /// <param name="masterFrameInterval">Frames between master frames (default: 300)</param>
    /// <param name="leaveOpen">If true, doesn't dispose stream on disposal</param>
    public KeyPointsSink(Stream stream, int masterFrameInterval = 300, bool leaveOpen = false)
        : this(new StreamFrameSink(stream, leaveOpen), masterFrameInterval, ownsSink: true)
    {
    }

    /// <summary>
    /// Creates a keypoints sink from any frame sink transport.
    /// </summary>
    /// <param name="frameSink">Transport sink (StreamFrameSink, TcpFrameSink, etc.)</param>
    /// <param name="masterFrameInterval">Frames between master frames (default: 300)</param>
    /// <param name="ownsSink">If true, disposes sink on disposal (default: false)</param>
    public KeyPointsSink(IFrameSink frameSink, int masterFrameInterval = 300, bool ownsSink = false)
    {
        _frameSink = frameSink ?? throw new ArgumentNullException(nameof(frameSink));
        _masterFrameInterval = masterFrameInterval;
        _ownsSink = ownsSink;
    }

    public IKeyPointsWriter CreateWriter(ulong frameId)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(KeyPointsSink));

        bool isDelta = _frameCount > 0 && (_frameCount % (ulong)_masterFrameInterval) != 0;
        var writer = new KeyPointsWriter(
            frameId,
            _frameSink,
            isDelta,
            isDelta ? _previousFrame : null,
            frameState => _previousFrame = frameState);
        _frameCount++;
        return writer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ownsSink)
            _frameSink?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ownsSink && _frameSink != null)
            await _frameSink.DisposeAsync();
    }
}

// ============================================================================
// Legacy Alias - For backward compatibility (will be removed in future)
// ============================================================================

/// <summary>
/// [DEPRECATED] Use KeyPointsSink instead.
/// Legacy alias for backward compatibility.
/// </summary>
[Obsolete("Use KeyPointsSink instead. This alias will be removed in a future version.")]
public class FileKeyPointsStorage : KeyPointsSink
{
    public FileKeyPointsStorage(Stream stream, int masterFrameInterval = 300)
        : base(stream, masterFrameInterval, leaveOpen: false)
    {
    }
}

/// <summary>
/// [DEPRECATED] Use IKeyPointsSink instead.
/// Legacy alias for backward compatibility.
/// </summary>
[Obsolete("Use IKeyPointsSink instead. This alias will be removed in a future version.")]
public interface IKeyPointsStorage : IKeyPointsSink
{
}

// ============================================================================
// KeyPointsDefinition - JSON structure for keypoints definition file
// ============================================================================

internal class KeyPointsDefinition
{
    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [System.Text.Json.Serialization.JsonPropertyName("compute_module_name")]
    public string ComputeModuleName { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("points")]
    public Dictionary<string, int> Points { get; set; } = new();
}
