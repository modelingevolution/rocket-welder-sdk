using System;
using System.Buffers;
using RocketWelder.SDK.Protocols;

namespace RocketWelder.SDK.Vision;

/// <summary>
/// Handle to a parsed keypoints frame with pooled buffers.
/// MUST be disposed to return buffers to ArrayPool.
/// </summary>
/// <remarks>
/// This is a class (not struct) to prevent double-dispose issues when copied.
/// Each handle owns its pooled buffer and returns it exactly once on disposal.
/// </remarks>
public sealed class KeyPointsFrameHandle : IDisposable
{
    private readonly KeyPoint[] _buffer;
    private readonly int _count;
    private bool _disposed;

    /// <summary>
    /// Frame identifier.
    /// </summary>
    public ulong FrameId { get; }

    /// <summary>
    /// Keypoints in this frame.
    /// </summary>
    public ReadOnlySpan<KeyPoint> KeyPoints => _buffer.AsSpan(0, _count);

    /// <summary>
    /// Number of keypoints in this frame.
    /// </summary>
    public int Count => _count;

    internal KeyPointsFrameHandle(ulong frameId, KeyPoint[] buffer, int count)
    {
        FrameId = frameId;
        _buffer = buffer;
        _count = count;
    }

    /// <summary>
    /// Returns pooled buffer. MUST be called when done with the frame.
    /// Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_buffer != null)
            ArrayPool<KeyPoint>.Shared.Return(_buffer, clearArray: false);
    }
}
