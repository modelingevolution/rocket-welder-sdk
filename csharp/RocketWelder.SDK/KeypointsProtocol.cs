using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using RocketWelder.SDK.Transport;
using RocketWelder.SDK.Protocols;
using RocketWelder.SDK.Vision;

// Import DeltaFrame<KeyPoint> for streaming use - uses Protocols.KeyPoint with ushort confidence
// Use .NormalizedConfidence() extension to get float 0.0-1.0 value
using DeltaKeyPointsFrame = RocketWelder.SDK.Protocols.DeltaFrame<RocketWelder.SDK.Protocols.KeyPoint>;

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
/// A decoded keypoints frame with absolute keypoint values.
/// Used by Document classes after delta decoding is complete.
/// For streaming with delta info, use DeltaFrame&lt;KeyPoint&gt; instead.
/// </summary>
/// <remarks>
/// Uses Protocols.KeyPoint with Confidence type (ushort 0-65535).
/// Confidence implicitly converts to float (0.0-1.0).
/// Uses ReadOnlyMemory for zero-allocation access to keypoint data.
/// </remarks>
public readonly record struct KeyPointsFrame(ulong FrameId, ReadOnlyMemory<KeyPoint> KeyPoints)
{
    /// <summary>
    /// Number of keypoints in this frame.
    /// </summary>
    public int Count => KeyPoints.Length;
}

// KeyPoint type is now consolidated into RocketWelder.SDK.Protocols.KeyPoint
// Confidence implicitly converts to float. Use .Position for Point access.

/// <summary>
/// Streaming reader for keypoints.
/// Reads frames from IFrameSource and yields them via IAsyncEnumerable.
/// Handles master/delta frame decoding automatically using KeyPointsProtocol.
/// Returns DeltaFrame&lt;KeyPoint&gt; with decoded absolute values and IsDelta metadata.
/// </summary>
public class KeyPointsSource : IKeyPointsSource
{
    private readonly IFrameSource _frameSource;
    private Dictionary<int, KeyPoint>? _previousFrame;
    private bool _disposed;

    public KeyPointsSource(IFrameSource frameSource)
    {
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
    }

    public async IAsyncEnumerable<DeltaKeyPointsFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            var frameData = await _frameSource.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            if (frameData.IsEmpty)
                yield break;

            var frame = ParseFrame(frameData.Span);
            yield return frame;
        }
    }

    private DeltaKeyPointsFrame ParseFrame(ReadOnlySpan<byte> data)
    {
        // Use Protocol for decoding - single source of truth
        var result = KeyPointsProtocol.ReadWithPreviousState(data, _previousFrame);

        // Update previous frame state for next delta decoding
        var items = result.Items.Span;
        _previousFrame = new Dictionary<int, KeyPoint>(items.Length);
        foreach (var kp in items)
            _previousFrame[kp.Id] = kp;

        return result;
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

// ============================================================================
// KeyPointsWriter - Writes single frame using KeyPointsProtocol
// ============================================================================

internal class KeyPointsWriter : IKeyPointsWriter
{
    private readonly ulong _frameId;
    private readonly IFrameSink _frameSink;
    private readonly bool _isDelta;
    private readonly IReadOnlyDictionary<int, KeyPoint>? _previousFrame;
    private readonly List<KeyPoint> _keypoints = new();
    private readonly Action<Dictionary<int, KeyPoint>>? _onFrameWritten;
    private bool _disposed = false;

    public KeyPointsWriter(
        ulong frameId,
        IFrameSink frameSink,
        bool isDelta,
        IReadOnlyDictionary<int, KeyPoint>? previousFrame,
        Action<Dictionary<int, KeyPoint>>? onFrameWritten = null)
    {
        _frameId = frameId;
        _frameSink = frameSink ?? throw new ArgumentNullException(nameof(frameSink));
        _isDelta = isDelta;
        _previousFrame = previousFrame;
        _onFrameWritten = onFrameWritten;
    }

    public void Append(int keypointId, int x, int y, float confidence)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(KeyPointsWriter));

        // Convert confidence from float (0.0-1.0) to ushort (0-65535)
        ushort confidenceUshort = (ushort)Math.Clamp(confidence * ushort.MaxValue, 0, ushort.MaxValue);
        _keypoints.Add(new KeyPoint(keypointId, x, y, confidenceUshort));
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

        // Write frame using Protocol - single allocation
        var (buffer, bytesWritten) = WriteFrame();

        // Send complete frame via sink (sliced to actual size)
        _frameSink.WriteFrame(buffer.AsSpan(0, bytesWritten));

        // Update previous frame state
        UpdatePreviousFrameState();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Write frame using Protocol - single allocation
        var (buffer, bytesWritten) = WriteFrame();

        // Send complete frame via sink (sliced to actual size)
        await _frameSink.WriteFrameAsync(buffer.AsMemory(0, bytesWritten)).ConfigureAwait(false);

        // Update previous frame state
        UpdatePreviousFrameState();
    }

    private void UpdatePreviousFrameState()
    {
        if (_onFrameWritten != null)
        {
            var frameState = new Dictionary<int, KeyPoint>();
            foreach (var kp in _keypoints)
            {
                frameState[kp.Id] = kp;
            }
            _onFrameWritten(frameState);
        }
    }

    private (byte[] buffer, int bytesWritten) WriteFrame()
    {
        // Use Protocol for encoding - single source of truth
        var keypointsSpan = CollectionsMarshal.AsSpan(_keypoints);

        int maxSize = _isDelta
            ? KeyPointsProtocol.CalculateDeltaFrameSize(_keypoints.Count)
            : KeyPointsProtocol.CalculateMasterFrameSize(_keypoints.Count);

        var buffer = new byte[maxSize];
        int bytesWritten;

        if (_isDelta && _previousFrame != null)
        {
            bytesWritten = KeyPointsProtocol.WriteDeltaFrame(buffer, _frameId, keypointsSpan, _previousFrame);
        }
        else
        {
            bytesWritten = KeyPointsProtocol.WriteMasterFrame(buffer, _frameId, keypointsSpan);
        }

        // Return buffer with actual length - no copy needed
        return (buffer, bytesWritten);
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
    private Dictionary<int, KeyPoint>? _previousFrame;
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
            _frameSink.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ownsSink)
            await _frameSink.DisposeAsync();
    }
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
