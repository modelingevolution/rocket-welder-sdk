using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorBlaze.Server;
using BlazorBlaze.VectorGraphics;
using ModelingEvolution.Drawing;
using RocketWelder.SDK.Hmi;
using SkiaSharp;

namespace RocketWelder.SDK.Graphics;

/// <summary>
/// Factory for creating per-frame <see cref="IUi"/> writers.
/// Wraps an <see cref="IStageSink"/> and adds Drawing primitive translation.
/// </summary>
public sealed class UiSink : IUiSink
{
    private readonly IStageSink _inner;
    private readonly Func<ulong> _getFrameId;
    private readonly bool _ownsSink;
    private bool _disposed;

    /// <summary>
    /// Creates a UiSink wrapping the specified stage sink.
    /// </summary>
    /// <param name="inner">The underlying stage sink for transport.</param>
    /// <param name="width">Viewport width in pixels (used for Line clipping).</param>
    /// <param name="height">Viewport height in pixels (used for Line clipping).</param>
    /// <param name="getFrameId">Function returning the current frame ID (connected to segmentation stream).</param>
    /// <param name="ownsSink">If true, disposes the inner sink when this factory is disposed.</param>
    public UiSink(IStageSink inner, uint width, uint height, Func<ulong> getFrameId, bool ownsSink = true)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _getFrameId = getFrameId ?? throw new ArgumentNullException(nameof(getFrameId));
        Width = width;
        Height = height;
        _ownsSink = ownsSink;
    }

    public uint Width { get; }
    public uint Height { get; }

    public IUi CreateWriter()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UiSink));

        var frameId = _getFrameId();
        var writer = _inner.CreateWriter(frameId);
        return new UiWriter(writer, Width, Height);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsSink) _inner.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsSink) await _inner.DisposeAsync();
    }
}

/// <summary>
/// Per-frame UI writer. Wraps <see cref="IStageWriter"/> and provides
/// <see cref="IUiLayer"/> access per layer. Auto-flushes on dispose.
/// </summary>
internal sealed class UiWriter : IUi
{
    private readonly IStageWriter _writer;
    private readonly uint _width;
    private readonly uint _height;
    private readonly Dictionary<byte, UiLayer> _layers = new();

    internal UiWriter(IStageWriter writer, uint width, uint height)
    {
        _writer = writer;
        _width = width;
        _height = height;
    }

    public ulong FrameId => _writer.FrameId;

    public IUiLayer this[byte layerId] => Layer(layerId);

    public IUiLayer Layer(byte layerId)
    {
        if (!_layers.TryGetValue(layerId, out var layer))
        {
            layer = new UiLayer(_writer[layerId], _width, _height);
            _layers[layerId] = layer;
        }
        return layer;
    }

    public void Dispose() => _writer.Dispose();
    public ValueTask DisposeAsync() => _writer.DisposeAsync();
}

/// <summary>
/// Layer adapter translating Drawing primitives to <see cref="ILayerCanvas"/> calls.
/// Pre-allocates an SKPoint buffer to avoid per-frame allocations.
/// </summary>
internal sealed class UiLayer : IUiLayer
{
    private readonly ILayerCanvas _inner;
    private readonly uint _width;
    private readonly uint _height;
    private SKPoint[] _pointBuffer = new SKPoint[64];

    internal const int DefaultPointMarkerRadius = 3;

    internal UiLayer(ILayerCanvas inner, uint width, uint height)
    {
        _inner = inner;
        _width = width;
        _height = height;
    }

    private void EnsureBuffer(int needed)
    {
        if (_pointBuffer.Length < needed)
        {
            // Grow to next power of 2
            int size = _pointBuffer.Length;
            while (size < needed) size <<= 1;
            _pointBuffer = new SKPoint[size];
        }
    }

    #region Drawing Primitives

    public void Draw(Polygon<float> polygon)
    {
        var span = polygon.AsSpan();
        if (span.Length == 0) return;

        EnsureBuffer(span.Length);
        for (int i = 0; i < span.Length; i++)
            _pointBuffer[i] = new SKPoint(span[i].X, span[i].Y);

        _inner.DrawPolygon(_pointBuffer.AsSpan(0, span.Length));
    }

    public void Draw(in Circle<float> circle)
    {
        _inner.DrawCircle(
            (int)circle.Center.X,
            (int)circle.Center.Y,
            (int)circle.Radius);
    }

    public void Draw(in Segment<float> segment)
    {
        _inner.DrawLine(
            (int)segment.Start.X, (int)segment.Start.Y,
            (int)segment.End.X, (int)segment.End.Y);
    }

    public void Draw(in Line<float> line)
    {
        var viewport = new Rectangle<float>(0, 0, _width, _height);
        var clipped = Intersections.FirstOf(line, viewport);
        if (clipped.HasValue)
            Draw(clipped.Value);
    }

    public void Draw(in Rectangle<float> rectangle)
    {
        _inner.DrawRectangle(
            (int)rectangle.X, (int)rectangle.Y,
            (int)rectangle.Width, (int)rectangle.Height);
    }

    public void Draw(in Triangle<float> triangle)
    {
        EnsureBuffer(3);
        _pointBuffer[0] = new SKPoint(triangle.A.X, triangle.A.Y);
        _pointBuffer[1] = new SKPoint(triangle.B.X, triangle.B.Y);
        _pointBuffer[2] = new SKPoint(triangle.C.X, triangle.C.Y);
        _inner.DrawPolygon(_pointBuffer.AsSpan(0, 3));
    }

    public void Draw(in Point<float> point)
    {
        _inner.DrawCircle((int)point.X, (int)point.Y, DefaultPointMarkerRadius);
    }

    public void DrawKeyPoint(string label, in Point<float> point,
        PointSymbol symbol = PointSymbol.Cross,
        RgbColor color = default,
        int size = 10,
        int fontSize = 12)
    {
        if (color == default) color = RgbColor.Green;

        var x = (int)point.X;
        var y = (int)point.Y;
        var half = size / 2;

        _inner.Save();

        _inner.SetStroke(color);
        _inner.SetThickness(2);

        switch (symbol)
        {
            case PointSymbol.Cross:
                _inner.DrawLine(x - half, y - half, x + half, y + half);
                _inner.DrawLine(x - half, y + half, x + half, y - half);
                break;
            case PointSymbol.Circle:
                _inner.DrawCircle(x, y, half);
                break;
            case PointSymbol.Square:
                _inner.DrawRectangle(x - half, y - half, size, size);
                break;
        }

        _inner.SetFontSize(fontSize);
        _inner.SetFontColor(color);
        _inner.DrawText(label, x + half + 4, y - half);

        _inner.Restore();
    }

    #endregion

    #region ILayerCanvas Passthrough

    public byte LayerId => _inner.LayerId;

    public void Master() => _inner.Master();
    public void Remain() => _inner.Remain();
    public void Clear() => _inner.Clear();

    public void SetStroke(RgbColor color) => _inner.SetStroke(color);
    public void SetFill(RgbColor color) => _inner.SetFill(color);
    public void SetThickness(int width) => _inner.SetThickness(width);
    public void SetFontSize(int size) => _inner.SetFontSize(size);
    public void SetFontColor(RgbColor color) => _inner.SetFontColor(color);

    public void Translate(float dx, float dy) => _inner.Translate(dx, dy);
    public void Rotate(float degrees) => _inner.Rotate(degrees);
    public void Scale(float sx, float sy) => _inner.Scale(sx, sy);
    public void Skew(float kx, float ky) => _inner.Skew(kx, ky);
    public void SetMatrix(SKMatrix matrix) => _inner.SetMatrix(matrix);

    public void Save() => _inner.Save();
    public void Restore() => _inner.Restore();
    public void ResetContext() => _inner.ResetContext();

    public void DrawPolygon(ReadOnlySpan<SKPoint> points) => _inner.DrawPolygon(points);
    public void DrawText(string text, int x, int y) => _inner.DrawText(text, x, y);
    public void DrawCircle(int centerX, int centerY, int radius) => _inner.DrawCircle(centerX, centerY, radius);
    public void DrawRectangle(int x, int y, int width, int height) => _inner.DrawRectangle(x, y, width, height);
    public void DrawLine(int x1, int y1, int x2, int y2) => _inner.DrawLine(x1, y1, x2, y2);
    public void DrawJpeg(ReadOnlySpan<byte> jpegData, int x, int y, int width, int height) => _inner.DrawJpeg(jpegData, x, y, width, height);

    #endregion
}
