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
    private readonly byte[] _buffer;
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

        // Auto-flush on dispose (same pattern as other writers)
        Flush();

        // Return layer buffers to pool
        foreach (var layer in _layers.Values)
        {
            layer.ReturnBuffer();
        }
        _layers.Clear();
        _activeLayerIds.Clear();

        // Return main buffer to pool
        ArrayPool<byte>.Shared.Return(_buffer);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Internal layer encoder that writes operations directly to buffer.
    /// </summary>
    private sealed class LayerEncoderImpl : ILayerCanvas
    {
        private readonly byte[] _layerBuffer;
        private readonly byte _layerId;
        private FrameType _frameType = FrameType.Master;
        private int _operationCount;
        private int _dataOffset;

        // Reserve space for layer header at start of buffer
        private const int HeaderReserve = 16;
        private const int LayerBufferSize = 256 * 1024;

        public byte LayerId => _layerId;

        public LayerEncoderImpl(byte layerId)
        {
            _layerId = layerId;
            // 256KB per layer to accommodate JPEG frames
            _layerBuffer = ArrayPool<byte>.Shared.Rent(LayerBufferSize);
            _dataOffset = HeaderReserve;
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

        private Span<byte> GetWriteSpan() => _layerBuffer.AsSpan(_dataOffset);

        #region Frame Type

        public void Master() => _frameType = FrameType.Master;
        public void Remain() => _frameType = FrameType.Remain;
        public void Clear() => _frameType = FrameType.Clear;

        #endregion

        #region Context State - Styling

        public void SetStroke(RgbColor color)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetStroke(GetWriteSpan(), color);
            _operationCount++;
        }

        public void SetFill(RgbColor color)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetFill(GetWriteSpan(), color);
            _operationCount++;
        }

        public void SetThickness(int width)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetThickness(GetWriteSpan(), width);
            _operationCount++;
        }

        public void SetFontSize(int size)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetFontSize(GetWriteSpan(), size);
            _operationCount++;
        }

        public void SetFontColor(RgbColor color)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetFontColor(GetWriteSpan(), color);
            _operationCount++;
        }

        #endregion

        #region Context State - Transforms

        public void Translate(float dx, float dy)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetOffset(GetWriteSpan(), dx, dy);
            _operationCount++;
        }

        public void Rotate(float degrees)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetRotation(GetWriteSpan(), degrees);
            _operationCount++;
        }

        public void Scale(float sx, float sy)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetScale(GetWriteSpan(), sx, sy);
            _operationCount++;
        }

        public void Skew(float kx, float ky)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetSkew(GetWriteSpan(), kx, ky);
            _operationCount++;
        }

        public void SetMatrix(SKMatrix matrix)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSetMatrix(GetWriteSpan(), matrix);
            _operationCount++;
        }

        #endregion

        #region Context Stack

        public void Save()
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteSaveContext(GetWriteSpan());
            _operationCount++;
        }

        public void Restore()
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteRestoreContext(GetWriteSpan());
            _operationCount++;
        }

        public void ResetContext()
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteResetContext(GetWriteSpan());
            _operationCount++;
        }

        #endregion

        #region Draw Operations

        public void DrawPolygon(ReadOnlySpan<SKPoint> points)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteDrawPolygon(GetWriteSpan(), points);
            _operationCount++;
        }

        public void DrawText(string text, int x, int y)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteDrawText(GetWriteSpan(), text, x, y);
            _operationCount++;
        }

        public void DrawCircle(int centerX, int centerY, int radius)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteDrawCircle(GetWriteSpan(), centerX, centerY, radius);
            _operationCount++;
        }

        public void DrawRectangle(int x, int y, int width, int height)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteDrawRect(GetWriteSpan(), x, y, width, height);
            _operationCount++;
        }

        public void DrawLine(int x1, int y1, int x2, int y2)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteDrawLine(GetWriteSpan(), x1, y1, x2, y2);
            _operationCount++;
        }

        public void DrawJpeg(ReadOnlySpan<byte> jpegData, int x, int y, int width, int height)
        {
            _dataOffset += VectorGraphicsEncoderV2.WriteDrawJpeg(GetWriteSpan(), jpegData, x, y, width, height);
            _operationCount++;
        }

        #endregion
    }
}
