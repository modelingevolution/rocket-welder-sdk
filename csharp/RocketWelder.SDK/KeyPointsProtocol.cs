using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RocketWelder.SDK.Transport;

namespace RocketWelder.SDK;

// ============================================================================
// KeyPoints Protocol - Binary format for efficient keypoint storage
// Supports master/delta frame compression for temporal sequences
// ============================================================================

/// <summary>
/// Sink for writing keypoints and reading keypoints data.
/// Transport-agnostic: works with files, TCP, WebSocket, NNG, etc.
/// </summary>
public interface IKeyPointsSink : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Create a writer for the current frame.
    /// Sink decides whether to write master or delta frame.
    /// </summary>
    IKeyPointsWriter CreateWriter(ulong frameId);

    /// <summary>
    /// Read entire keypoints series into memory for efficient querying.
    /// </summary>
    /// <param name="json">JSON definition string mapping keypoint names to IDs</param>
    /// <param name="frameSource">Frame source to read frames from (handles transport-specific framing)</param>
    Task<KeyPointsSeries> Read(string json, IFrameSource frameSource);
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

        // Send complete frame via sink (atomic operation)
        _buffer.Seek(0, SeekOrigin.Begin);
        _frameSink.WriteFrame(_buffer.ToArray());

        // Update previous frame state
        if (_onFrameWritten != null)
        {
            var frameState = new Dictionary<int, (Point, ushort)>();
            foreach (var (id, point, confidence) in _keypoints)
            {
                frameState[id] = (point, confidence);
            }
            _onFrameWritten(frameState);
        }

        _buffer.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Write frame to buffer asynchronously
        await WriteFrameAsync();

        // Send complete frame via sink (atomic operation)
        _buffer.Seek(0, SeekOrigin.Begin);
        await _frameSink.WriteFrameAsync(_buffer.ToArray());

        // Update previous frame state
        if (_onFrameWritten != null)
        {
            var frameState = new Dictionary<int, (Point, ushort)>();
            foreach (var (id, point, confidence) in _keypoints)
            {
                frameState[id] = (point, confidence);
            }
            _onFrameWritten(frameState);
        }

        await _buffer.DisposeAsync();
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

    private async Task WriteFrameAsync()
    {
        // Write frame type
        byte frameType = _isDelta ? DeltaFrameType : MasterFrameType;
        await _buffer.WriteAsync(new byte[] { frameType }, 0, 1);

        // Write frame ID
        byte[] frameIdBytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(frameIdBytes, _frameId);
        await _buffer.WriteAsync(frameIdBytes, 0, 8);

        // Write keypoint count
        await _buffer.WriteVarintAsync((uint)_keypoints.Count);

        if (_isDelta && _previousFrame != null)
        {
            await WriteDeltaKeypointsAsync();
        }
        else
        {
            await WriteMasterKeypointsAsync();
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

    private async Task WriteMasterKeypointsAsync()
    {
        foreach (var (id, point, confidence) in _keypoints)
        {
            // Write keypoint ID
            await _buffer.WriteVarintAsync((uint)id);

            // Write absolute coordinates
            byte[] coords = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(coords, point.X);
            BinaryPrimitives.WriteInt32LittleEndian(coords.AsSpan(4), point.Y);
            await _buffer.WriteAsync(coords, 0, 8);

            // Write confidence
            byte[] confBytes = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(confBytes, confidence);
            await _buffer.WriteAsync(confBytes, 0, 2);
        }
    }

    private async Task WriteDeltaKeypointsAsync()
    {
        foreach (var (id, point, confidence) in _keypoints)
        {
            // Write keypoint ID
            await _buffer.WriteVarintAsync((uint)id);

            // Calculate deltas
            if (_previousFrame!.TryGetValue(id, out var prev))
            {
                int deltaX = point.X - prev.point.X;
                int deltaY = point.Y - prev.point.Y;
                int deltaConf = confidence - prev.confidence;

                await _buffer.WriteVarintAsync(deltaX.ZigZagEncode());
                await _buffer.WriteVarintAsync(deltaY.ZigZagEncode());
                await _buffer.WriteVarintAsync(deltaConf.ZigZagEncode());
            }
            else
            {
                // Keypoint didn't exist in previous frame - write as absolute
                await _buffer.WriteVarintAsync(point.X.ZigZagEncode());
                await _buffer.WriteVarintAsync(point.Y.ZigZagEncode());
                await _buffer.WriteVarintAsync(((int)confidence).ZigZagEncode());
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

    public async Task<KeyPointsSeries> Read(string json, IFrameSource frameSource)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(KeyPointsSink));

        // Parse JSON definition
        var definition = JsonSerializer.Deserialize<KeyPointsDefinition>(json)
            ?? throw new InvalidDataException("Invalid keypoints definition JSON");

        // Read all frames from frame source (handles transport-specific framing)
        var index = new Dictionary<ulong, SortedDictionary<int, (Point, float)>>();
        var currentFrame = new Dictionary<int, (Point point, ushort confidence)>();

        while (frameSource.HasMoreFrames)
        {
            // Read complete frame (frame source handles length prefixes, etc.)
            var frameBytes = await frameSource.ReadFrameAsync();
            if (frameBytes.Length == 0) break;

            using var frameStream = new MemoryStream(frameBytes.ToArray());

            // Read frame type
            int frameTypeByte = frameStream.ReadByte();
            if (frameTypeByte == -1) break;

            byte frameType = (byte)frameTypeByte;

            // Read frame ID
            Span<byte> frameIdBytes = stackalloc byte[8];
            frameStream.Read(frameIdBytes);
            ulong frameId = BinaryPrimitives.ReadUInt64LittleEndian(frameIdBytes);

            // Read keypoint count
            uint keypointCount = frameStream.ReadVarint();

            var frameKeypoints = new SortedDictionary<int, (Point, float)>();

            if (frameType == 0x00) // Master frame
            {
                currentFrame.Clear();
                for (uint i = 0; i < keypointCount; i++)
                {
                    int id = (int)frameStream.ReadVarint();

                    Span<byte> coords = stackalloc byte[8];
                    frameStream.Read(coords);
                    int x = BinaryPrimitives.ReadInt32LittleEndian(coords);
                    int y = BinaryPrimitives.ReadInt32LittleEndian(coords[4..]);

                    Span<byte> confBytes = stackalloc byte[2];
                    frameStream.Read(confBytes);
                    ushort confUshort = BinaryPrimitives.ReadUInt16LittleEndian(confBytes);

                    var point = new Point(x, y);
                    currentFrame[id] = (point, confUshort);
                    frameKeypoints[id] = (point, confUshort / 10000f);
                }
            }
            else if (frameType == 0x01) // Delta frame
            {
                for (uint i = 0; i < keypointCount; i++)
                {
                    int id = (int)frameStream.ReadVarint();

                    int deltaX = frameStream.ReadVarint().ZigZagDecode();
                    int deltaY = frameStream.ReadVarint().ZigZagDecode();
                    int deltaConf = frameStream.ReadVarint().ZigZagDecode();

                    if (currentFrame.TryGetValue(id, out var prev))
                    {
                        int x = prev.point.X + deltaX;
                        int y = prev.point.Y + deltaY;
                        ushort conf = (ushort)Math.Clamp(prev.confidence + deltaConf, 0, 10000);

                        var point = new Point(x, y);
                        currentFrame[id] = (point, conf);
                        frameKeypoints[id] = (point, conf / 10000f);
                    }
                    else
                    {
                        // New keypoint - deltas are absolute values
                        var point = new Point(deltaX, deltaY);
                        ushort conf = (ushort)Math.Clamp(deltaConf, 0, 10000);
                        currentFrame[id] = (point, conf);
                        frameKeypoints[id] = (point, conf / 10000f);
                    }
                }
            }

            index[frameId] = frameKeypoints;
        }

        return new KeyPointsSeries(
            definition.Version,
            definition.ComputeModuleName,
            definition.Points,
            index);
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
