using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorBlaze.Server;
using BlazorBlaze.VectorGraphics;
using BlazorBlaze.VectorGraphics.Protocol;
using RocketWelder.SDK.Transport;
using SkiaSharp;

namespace RocketWelder.SDK.Graphics;

/// <summary>
/// Factory for creating per-frame stage writers.
/// Follows the same pattern as SegmentationResultSink and KeyPointsSink.
/// </summary>
public sealed class StageSink : IStageSink
{
    /// <summary>
    /// Hard ceiling for one layer's encoded bytes (bug 021). Far above any legitimate frame (a 4K JPEG is ~2 MB; the
    /// program overlay that overflowed the old fixed 256 KB was ~0.5 MB) — it exists so a runaway caller, or one
    /// oversized <c>DrawJpeg</c>, fails with an <see cref="InvalidOperationException"/> naming the layer instead of
    /// exhausting memory. Note the browser side of rw2 receives frames into an 8 MB buffer; a frame between that and
    /// this ceiling encodes fine here and is the receiver's problem.
    /// </summary>
    public const int MaxLayerBytes = 64 * 1024 * 1024;

    private readonly IFrameSink _frameSink;
    private readonly bool _ownsSink;
    private readonly int _bufferSize;
    private bool _disposed;

    /// <summary>
    /// Creates a StageSink with the specified transport.
    /// </summary>
    /// <param name="frameSink">The transport for sending encoded frames</param>
    /// <param name="bufferSize">Size of the encoding buffer per writer (default 1MB)</param>
    /// <param name="ownsSink">If true, disposes the sink when this factory is disposed</param>
    public StageSink(IFrameSink frameSink, int bufferSize = 1024 * 1024, bool ownsSink = true)
    {
        _frameSink = frameSink ?? throw new ArgumentNullException(nameof(frameSink));
        _bufferSize = bufferSize;
        _ownsSink = ownsSink;
    }

    /// <summary>
    /// Creates a writer for the specified frame.
    /// The writer auto-flushes on dispose.
    /// </summary>
    public IStageWriter CreateWriter(ulong frameId)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(StageSink));

        return new StageWriter(frameId, _frameSink, _bufferSize);
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

/// <summary>
/// Per-frame stage writer that auto-flushes on dispose.
/// Follows the same pattern as SegmentationResultWriter and KeyPointsWriter.
/// </summary>
internal sealed class StageWriter : IStageWriter
{
    private readonly ulong _frameId;
    private readonly IFrameSink _frameSink;
    private byte[] _buffer;
    private readonly int _bufferSize;
    private readonly Dictionary<byte, LayerEncoderImpl> _layers = new();
    private readonly List<byte> _activeLayerIds = new();
    private bool _disposed;

    internal StageWriter(ulong frameId, IFrameSink frameSink, int bufferSize = 1024 * 1024)
    {
        _frameId = frameId;
        _frameSink = frameSink ?? throw new ArgumentNullException(nameof(frameSink));
        _bufferSize = bufferSize;
        _buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
    }

    /// <summary>
    /// Gets the frame ID for this writer.
    /// </summary>
    public ulong FrameId => _frameId;

    /// <summary>
    /// Gets the layer canvas for the specified layer ID.
    /// </summary>
    public ILayerCanvas this[byte layerId] => Layer(layerId);

    /// <summary>
    /// Gets the layer canvas for the specified layer ID.
    /// </summary>
    public ILayerCanvas Layer(byte layerId)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(StageWriter));

        if (!_layers.TryGetValue(layerId, out var layer))
        {
            layer = new LayerEncoderImpl(layerId);
            _layers[layerId] = layer;
        }

        // Track that this layer was accessed
        if (!_activeLayerIds.Contains(layerId))
        {
            _activeLayerIds.Add(layerId);
        }

        return layer;
    }

    /// <summary>
    /// Encodes and sends all layer operations via transport.
    /// Called automatically on dispose.
    /// </summary>
    private void Flush()
    {
        if (_activeLayerIds.Count == 0)
            return;

        // Bug 021: the frame buffer is sized from what the layers actually hold, so a frame larger than the
        // initial rent (a big program overlay, a JPEG) is sent whole instead of throwing from a Span copy.
        long requiredBytes = MessageHeaderMaxBytes + EndMarkerMaxBytes;
        foreach (var layerId in _activeLayerIds)
            requiredBytes += _layers[layerId].MaxEncodedBytes;
        if (requiredBytes > Array.MaxLength)
            throw new InvalidOperationException(
                $"Frame {_frameId} would be {requiredBytes} bytes across {_activeLayerIds.Count} layers; that cannot be sent.");
        var required = (int)requiredBytes;
        if (_buffer.Length < required)
        {
            // Rent first, return second: a failed rent must not leave a pooled array referenced by this writer.
            var grown = ArrayPool<byte>.Shared.Rent(required);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = grown;
        }

        var span = _buffer.AsSpan();
        int offset = 0;

        // Write message header
        offset += VectorGraphicsEncoderV2.WriteMessageHeader(span, _frameId, (byte)_activeLayerIds.Count);

        // Encode each active layer
        foreach (var layerId in _activeLayerIds)
        {
            var layer = _layers[layerId];
            offset += layer.CopyEncodedData(span.Slice(offset));
        }

        // Write end marker
        offset += VectorGraphicsEncoderV2.WriteEndMarker(span.Slice(offset));

        // Send via transport
        _frameSink.WriteFrame(new ReadOnlySpan<byte>(_buffer, 0, offset));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            // Auto-flush on dispose (same pattern as other writers)
            Flush();
        }
        finally
        {
            // Return the buffers even when the flush failed — the pooled arrays must not be lost on the
            // repeated-failure path a caller may keep surviving.
            foreach (var layer in _layers.Values)
            {
                layer.ReturnBuffer();
            }
            _layers.Clear();
            _activeLayerIds.Clear();

            // Return main buffer to pool
            ArrayPool<byte>.Shared.Return(_buffer);
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    // WriteMessageHeader = 8 (frame id) + 1 (layer count); WriteEndMarker = 2. Kept as upper bounds.
    private const int MessageHeaderMaxBytes = 16;
    private const int EndMarkerMaxBytes = 4;

    /// <summary>
    /// Internal layer encoder that writes operations directly to buffer.
    /// </summary>
    /// <remarks>
    /// Bug 021: the buffer GROWS. Every op first reserves an upper bound of the bytes the V2 encoder can emit for
    /// it (<see cref="VectorGraphicsEncoderV2"/> writes into a Span with no bounds check of its own, so an op that
    /// did not fit surfaced as <c>ArgumentException: Destination is too short</c> / <c>IndexOutOfRangeException</c>
    /// from inside the caller's draw code). The initial rent is still 256 KB; a frame that needs more re-rents a
    /// larger pooled array and copies, so a big overlay costs one extra copy rather than a failed frame.
    /// </remarks>
    private sealed class LayerEncoderImpl : ILayerCanvas
    {
        private byte[] _layerBuffer;
        private readonly byte _layerId;
        private FrameType _frameType = FrameType.Master;
        private int _operationCount;
        private int _dataOffset;

        // Reserve space for layer header at start of buffer
        private const int HeaderReserve = 16;
        private const int LayerBufferSize = 256 * 1024;

        // Upper bounds of what VectorGraphicsEncoderV2 emits per op. A varint is at most 5 bytes for a 32-bit
        // value; SetContext ops are 3 bytes of header + payload; a matrix is 3 + 6 floats.
        private const int VarintMaxBytes = 5;
        private const int OpHeaderBytes = 1;
        private const int ContextOpHeaderBytes = 3;
        private const int ColorOpMaxBytes = ContextOpHeaderBytes + 4;
        private const int ScalarOpMaxBytes = ContextOpHeaderBytes + VarintMaxBytes;
        private const int PairOpMaxBytes = ContextOpHeaderBytes + 2 * VarintMaxBytes;
        private const int FloatOpMaxBytes = ContextOpHeaderBytes + 2 * VarintMaxBytes;
        private const int MatrixOpMaxBytes = ContextOpHeaderBytes + 6 * 4;
        private const int PointMaxBytes = 2 * VarintMaxBytes;

        public byte LayerId => _layerId;

        public LayerEncoderImpl(byte layerId)
        {
            _layerId = layerId;
            // 256KB per layer to accommodate JPEG frames
            _layerBuffer = ArrayPool<byte>.Shared.Rent(LayerBufferSize);
            _dataOffset = HeaderReserve;
        }

        /// <summary>
        /// Upper bound of the bytes <see cref="CopyEncodedData"/> will write: the layer header (≤ HeaderReserve) plus
        /// the op data. The writer sizes its frame buffer from the sum over all layers.
        /// </summary>
        public int MaxEncodedBytes => _dataOffset;

        /// <summary>
        /// Reserves room for one op of at most <paramref name="maxOpBytes"/> bytes and returns exactly that window,
        /// growing the pooled buffer first when the remaining space is smaller. The window is sliced to the
        /// reservation on purpose: an encoder that ever emits more than the declared bound fails loudly and
        /// deterministically (in the unit tests) instead of only at an array boundary in the field. Growth doubles
        /// (at least to the required size) so a big frame re-rents O(log n) times; the data so far is copied across.
        /// </summary>
        private Span<byte> Reserve(int maxOpBytes)
        {
            long requiredBytes = (long)_dataOffset + maxOpBytes;
            if (requiredBytes > StageSink.MaxLayerBytes)
                throw new InvalidOperationException(
                    $"Layer {_layerId} would exceed {StageSink.MaxLayerBytes / (1024 * 1024)} MB of encoded vector graphics "
                    + $"({_operationCount} ops so far, next op up to {maxOpBytes} bytes); the caller is emitting "
                    + "an unbounded number of draw operations or one oversized image.");
            var required = (int)requiredBytes;
            if (required > _layerBuffer.Length)
            {
                int newSize = Math.Max(required, Math.Min(_layerBuffer.Length * 2, StageSink.MaxLayerBytes));
                var grown = ArrayPool<byte>.Shared.Rent(newSize);
                _layerBuffer.AsSpan(0, _dataOffset).CopyTo(grown);
                ArrayPool<byte>.Shared.Return(_layerBuffer);
                _layerBuffer = grown;
            }
            return _layerBuffer.AsSpan(_dataOffset, maxOpBytes);
        }

        /// <summary>
        /// Returns the buffer to the pool.
        /// </summary>
        public void ReturnBuffer()
        {
            ArrayPool<byte>.Shared.Return(_layerBuffer);
        }

        /// <summary>
        /// Copies the encoded layer data (with header) to the destination buffer.
        /// </summary>
        public int CopyEncodedData(Span<byte> destination)
        {
            int offset = 0;

            switch (_frameType)
            {
                case FrameType.Master:
                    offset += VectorGraphicsEncoderV2.WriteLayerMaster(destination, _layerId, (uint)_operationCount);
                    var dataLength = _dataOffset - HeaderReserve;
                    _layerBuffer.AsSpan(HeaderReserve, dataLength).CopyTo(destination.Slice(offset));
                    offset += dataLength;
                    break;

                case FrameType.Remain:
                    offset += VectorGraphicsEncoderV2.WriteLayerRemain(destination, _layerId);
                    break;

                case FrameType.Clear:
                    offset += VectorGraphicsEncoderV2.WriteLayerClear(destination, _layerId);
                    break;
            }

            return offset;
        }

        #region Frame Type

        public void Master() => _frameType = FrameType.Master;
        public void Remain() => _frameType = FrameType.Remain;
        public void Clear() => _frameType = FrameType.Clear;

        #endregion

        #region Context State - Styling

        public void SetStroke(RgbColor color)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetStroke(Reserve(ColorOpMaxBytes), color);
            _operationCount++;
        }

        public void SetFill(RgbColor color)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetFill(Reserve(ColorOpMaxBytes), color);
            _operationCount++;
        }

        public void SetThickness(int width)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetThickness(Reserve(ScalarOpMaxBytes), width);
            _operationCount++;
        }

        public void SetFontSize(int size)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetFontSize(Reserve(ScalarOpMaxBytes), size);
            _operationCount++;
        }

        public void SetFontColor(RgbColor color)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetFontColor(Reserve(ColorOpMaxBytes), color);
            _operationCount++;
        }

        #endregion

        #region Context State - Transforms

        public void Translate(float dx, float dy)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetOffset(Reserve(PairOpMaxBytes), dx, dy);
            _operationCount++;
        }

        public void Rotate(float degrees)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetRotation(Reserve(FloatOpMaxBytes), degrees);
            _operationCount++;
        }

        public void Scale(float sx, float sy)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetScale(Reserve(PairOpMaxBytes), sx, sy);
            _operationCount++;
        }

        public void Skew(float kx, float ky)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetSkew(Reserve(PairOpMaxBytes), kx, ky);
            _operationCount++;
        }

        public void SetMatrix(SKMatrix matrix)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetMatrix(Reserve(MatrixOpMaxBytes), matrix);
            _operationCount++;
        }

        #endregion

        #region Context Stack

        public void Save()
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSaveContext(Reserve(OpHeaderBytes));
            _operationCount++;
        }

        public void Restore()
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteRestoreContext(Reserve(OpHeaderBytes));
            _operationCount++;
        }

        public void ResetContext()
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteResetContext(Reserve(OpHeaderBytes));
            _operationCount++;
        }

        #endregion

        #region Draw Operations

        public void DrawPolygon(ReadOnlySpan<SKPoint> points)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteDrawPolygon(Reserve(OpHeaderBytes + VarintMaxBytes + points.Length * PointMaxBytes), points);
            _operationCount++;
        }

        public void DrawText(string text, int x, int y)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteDrawText(Reserve(OpHeaderBytes + 3 * VarintMaxBytes + System.Text.Encoding.UTF8.GetByteCount(text)), text, x, y);
            _operationCount++;
        }

        public void DrawCircle(int centerX, int centerY, int radius)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteDrawCircle(Reserve(OpHeaderBytes + 3 * VarintMaxBytes), centerX, centerY, radius);
            _operationCount++;
        }

        public void DrawRectangle(int x, int y, int width, int height)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteDrawRect(Reserve(OpHeaderBytes + 4 * VarintMaxBytes), x, y, width, height);
            _operationCount++;
        }

        public void DrawLine(int x1, int y1, int x2, int y2)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteDrawLine(Reserve(OpHeaderBytes + 4 * VarintMaxBytes), x1, y1, x2, y2);
            _operationCount++;
        }

        public void DrawJpeg(ReadOnlySpan<byte> jpegData, int x, int y, int width, int height)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteDrawJpeg(Reserve(OpHeaderBytes + 5 * VarintMaxBytes + jpegData.Length), jpegData, x, y, width, height);
            _operationCount++;
        }

        #endregion
    }
}
