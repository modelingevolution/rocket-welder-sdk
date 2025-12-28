using System;
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
/// DOM-style document for keypoints results.
/// Loads entire file and decodes all frames upfront (required due to delta encoding).
/// O(1) random access by frame ID.
/// </summary>
/// <remarks>
/// Unlike SegmentationDocument, keypoints uses delta encoding where each frame
/// may reference the previous frame. This requires sequential decoding on load.
/// The decoded frames are small (~200 bytes each) so keeping them in memory is fine.
/// <code>
/// using var doc = await KeyPointsDocument.LoadAsync("keypoints.bin");
///
/// // Random access by FrameId
/// if (doc.TryGetFrame(frameId, out var frame))
/// {
///     foreach (var kp in frame.KeyPoints)
///         RenderKeypoint(kp.Id, kp.X, kp.Y, kp.Confidence);
/// }
/// </code>
/// </remarks>
public sealed class KeyPointsDocument : IDisposable
{
    private readonly Dictionary<ulong, KeyPointsFrame> _frames;
    private bool _disposed;

    private KeyPointsDocument(Dictionary<ulong, KeyPointsFrame> frames)
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
    public IEnumerable<KeyPointsFrame> Frames => _frames.Values;

    #region Factory Methods

    /// <summary>
    /// Load a keypoints document from a file.
    /// </summary>
    public static async Task<KeyPointsDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var data = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var frames = DecodeAllFrames(data);
        return new KeyPointsDocument(frames);
    }

    /// <summary>
    /// Load a keypoints document from a stream.
    /// </summary>
    public static async Task<KeyPointsDocument> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
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
        return new KeyPointsDocument(frames);
    }

    /// <summary>
    /// Load a keypoints document from a byte array.
    /// </summary>
    public static KeyPointsDocument Load(byte[] data)
    {
        var frames = DecodeAllFrames(data);
        return new KeyPointsDocument(frames);
    }

    #endregion

    #region Frame Access

    /// <summary>
    /// Get a frame by ID.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Frame not found</exception>
    public KeyPointsFrame GetFrame(ulong frameId)
    {
        ThrowIfDisposed();

        if (!_frames.TryGetValue(frameId, out var frame))
            throw new KeyNotFoundException($"Frame {frameId} not found in document");

        return frame;
    }

    /// <summary>
    /// Try to get a frame by ID.
    /// </summary>
    public bool TryGetFrame(ulong frameId, out KeyPointsFrame frame)
    {
        ThrowIfDisposed();
        return _frames.TryGetValue(frameId, out frame);
    }

    /// <summary>
    /// Indexer for frame access.
    /// </summary>
    public KeyPointsFrame this[ulong frameId] => GetFrame(frameId);

    #endregion

    #region Decoding

    private static Dictionary<ulong, KeyPointsFrame> DecodeAllFrames(byte[] data)
    {
        var frames = new Dictionary<ulong, KeyPointsFrame>();
        Dictionary<int, (Point point, ushort confidence)>? previousFrame = null;

        using var outerStream = new MemoryStream(data, writable: false);

        while (outerStream.Position < outerStream.Length)
        {
            // Read frame length
            uint payloadLength = outerStream.ReadVarint();
            long payloadStart = outerStream.Position;

            // Read frame type
            int frameTypeByte = outerStream.ReadByte();
            if (frameTypeByte == -1)
                throw new EndOfStreamException("Unexpected end of frame");

            byte frameType = (byte)frameTypeByte;
            bool isDelta = frameType == KeypointsProtocol.DeltaFrameType;

            // Read frame ID (8 bytes LE)
            Span<byte> frameIdBytes = stackalloc byte[8];
            if (outerStream.Read(frameIdBytes) != 8)
                throw new EndOfStreamException("Failed to read FrameId");

            ulong frameId = BinaryPrimitives.ReadUInt64LittleEndian(frameIdBytes);

            // Read keypoint count
            uint keypointCount = outerStream.ReadVarint();

            // Read keypoints - use array for efficiency
            var keypoints = new Keypoint[(int)keypointCount];
            var currentFrame = new Dictionary<int, (Point point, ushort confidence)>();

            if (isDelta && previousFrame != null)
            {
                // Delta frame - read deltas from previous frame
                for (int i = 0; i < keypointCount; i++)
                {
                    int keypointId = (int)outerStream.ReadVarint();
                    int deltaX = outerStream.ReadVarint().ZigZagDecode();
                    int deltaY = outerStream.ReadVarint().ZigZagDecode();
                    int deltaConfidence = outerStream.ReadVarint().ZigZagDecode();

                    int x, y;
                    ushort confidence;

                    if (previousFrame.TryGetValue(keypointId, out var prev))
                    {
                        x = prev.point.X + deltaX;
                        y = prev.point.Y + deltaY;
                        // Clamp to prevent overflow (confidence is 0-65535 representing 0.0-1.0)
                        confidence = (ushort)Math.Clamp(prev.confidence + deltaConfidence, 0, ushort.MaxValue);
                    }
                    else
                    {
                        x = deltaX;
                        y = deltaY;
                        confidence = (ushort)Math.Clamp(deltaConfidence, 0, ushort.MaxValue);
                    }

                    keypoints[i] = new Keypoint(keypointId, x, y, confidence);
                    currentFrame[keypointId] = (new Point(x, y), confidence);
                }
            }
            else
            {
                // Master frame - read absolute values
                for (int i = 0; i < keypointCount; i++)
                {
                    int keypointId = (int)outerStream.ReadVarint();

                    // Read coordinates (4 bytes each, LE)
                    Span<byte> coordBytes = stackalloc byte[4];
                    outerStream.Read(coordBytes);
                    int x = BinaryPrimitives.ReadInt32LittleEndian(coordBytes);
                    outerStream.Read(coordBytes);
                    int y = BinaryPrimitives.ReadInt32LittleEndian(coordBytes);

                    // Read confidence (2 bytes, LE)
                    Span<byte> confBytes = stackalloc byte[2];
                    outerStream.Read(confBytes);
                    ushort confidence = BinaryPrimitives.ReadUInt16LittleEndian(confBytes);

                    keypoints[i] = new Keypoint(keypointId, x, y, confidence);
                    currentFrame[keypointId] = (new Point(x, y), confidence);
                }
            }

            // Store decoded frame (IsDelta is wire format detail, not stored in Document)
            frames[frameId] = new KeyPointsFrame(frameId, keypoints);
            previousFrame = currentFrame;

            // Ensure we're at the right position for next frame
            outerStream.Position = payloadStart + payloadLength;
        }

        return frames;
    }

    #endregion

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(KeyPointsDocument));
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
