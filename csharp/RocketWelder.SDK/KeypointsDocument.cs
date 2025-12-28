using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RocketWelder.SDK.Protocols;

namespace RocketWelder.SDK;

/// <summary>
/// DOM-style document for keypoints results.
/// Loads entire file and decodes all frames upfront (required due to delta encoding).
/// O(1) random access by frame ID.
/// </summary>
/// <remarks>
/// Unlike SegmentationDocument, keypoints uses delta encoding where each frame
/// may reference the previous frame. This requires sequential decoding on load.
/// The decoded frames are small (~200 bytes each) so keeping them in memory is fine.
/// <code>
/// using var doc = await KeypointsDocument.LoadAsync("keypoints.bin");
///
/// // Random access by FrameId
/// if (doc.TryGetFrame(frameId, out var frame))
/// {
///     foreach (var kp in frame.Keypoints)
///         RenderKeypoint(kp.Id, kp.X, kp.Y, kp.Confidence);
/// }
/// </code>
/// </remarks>
public sealed class KeypointsDocument : IDisposable
{
    private readonly Dictionary<ulong, KeypointsFrame> _frames;
    private bool _disposed;

    private KeypointsDocument(Dictionary<ulong, KeypointsFrame> frames)
    {
        _frames = frames;
    }

    /// <summary>
    /// Number of frames in the document.
    /// </summary>
    public int FrameCount => _frames.Count;

    /// <summary>
    /// All frame IDs in the document.
    /// </summary>
    public IEnumerable<ulong> FrameIds => _frames.Keys;

    /// <summary>
    /// Check if a frame exists in the document.
    /// </summary>
    public bool ContainsFrame(ulong frameId) => _frames.ContainsKey(frameId);

    /// <summary>
    /// All frames in the document (for enumeration).
    /// </summary>
    public IEnumerable<KeypointsFrame> Frames => _frames.Values;

    #region Factory Methods

    /// <summary>
    /// Load a keypoints document from a file.
    /// </summary>
    public static async Task<KeypointsDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var data = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var frames = DecodeAllFrames(data);
        return new KeypointsDocument(frames);
    }

    /// <summary>
    /// Load a keypoints document from a stream.
    /// </summary>
    public static async Task<KeypointsDocument> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
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
        var frames = DecodeAllFrames(data);
        return new KeypointsDocument(frames);
    }

    /// <summary>
    /// Load a keypoints document from a byte array.
    /// </summary>
    public static KeypointsDocument Load(byte[] data)
    {
        var frames = DecodeAllFrames(data);
        return new KeypointsDocument(frames);
    }

    #endregion

    #region Frame Access

    /// <summary>
    /// Get a frame by ID.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Frame not found</exception>
    public KeypointsFrame GetFrame(ulong frameId)
    {
        ThrowIfDisposed();

        if (!_frames.TryGetValue(frameId, out var frame))
            throw new KeyNotFoundException($"Frame {frameId} not found in document");

        return frame;
    }

    /// <summary>
    /// Try to get a frame by ID.
    /// </summary>
    public bool TryGetFrame(ulong frameId, out KeypointsFrame frame)
    {
        ThrowIfDisposed();
        return _frames.TryGetValue(frameId, out frame);
    }

    /// <summary>
    /// Indexer for frame access.
    /// </summary>
    public KeypointsFrame this[ulong frameId] => GetFrame(frameId);

    #endregion

    #region Decoding

    private static Dictionary<ulong, KeypointsFrame> DecodeAllFrames(byte[] data)
    {
        var frames = new Dictionary<ulong, KeypointsFrame>();
        Dictionary<int, Keypoint>? previousFrame = null;

        using var stream = new MemoryStream(data, writable: false);

        while (stream.Position < stream.Length)
        {
            // Read frame length prefix
            uint payloadLength = stream.ReadVarint();
            long payloadStart = stream.Position;

            // Read frame data using Protocol - single source of truth
            var frameSpan = data.AsSpan((int)payloadStart, (int)payloadLength);
            var result = KeypointsProtocol.ReadWithPreviousState(frameSpan, previousFrame);

            // Store decoded frame (IsDelta is wire format detail, not exposed in Document)
            // Zero-allocation: pass ReadOnlyMemory directly instead of converting to array
            frames[result.FrameId] = new KeypointsFrame(result.FrameId, result.Items);

            // Update previous frame state for next delta decoding
            var itemsSpan = result.Items.Span;
            previousFrame = new Dictionary<int, Keypoint>(itemsSpan.Length);
            foreach (var kp in itemsSpan)
                previousFrame[kp.Id] = kp;

            // Move to next frame
            stream.Position = payloadStart + payloadLength;
        }

        return frames;
    }

    #endregion

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(KeypointsDocument));
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
