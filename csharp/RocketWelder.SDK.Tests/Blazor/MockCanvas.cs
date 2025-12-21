using BlazorBlaze.ValueTypes;
using BlazorBlaze.VectorGraphics;
using BlazorBlaze.VectorGraphics.Protocol;
using SkiaSharp;

namespace RocketWelder.SDK.Tests.Blazor;

/// <summary>
/// Mock canvas implementation for testing decoders.
/// Records all draw calls for verification.
/// </summary>
public class MockCanvas : ICanvas
{
    public record PolygonCall(SKPoint[] Points, RgbColor Stroke, int Thickness);
    public record CircleCall(int CenterX, int CenterY, int Radius, RgbColor Stroke, int Thickness);
    public record TextCall(string Text, int X, int Y, RgbColor Color, int FontSize);
    public record RectCall(int X, int Y, int Width, int Height, RgbColor Stroke, int Thickness);
    public record LineCall(int X1, int Y1, int X2, int Y2, RgbColor Stroke, int Thickness);

    public List<PolygonCall> PolygonCalls { get; } = new();
    public List<CircleCall> CircleCalls { get; } = new();
    public List<TextCall> TextCalls { get; } = new();
    public List<RectCall> RectCalls { get; } = new();
    public List<LineCall> LineCalls { get; } = new();
    public List<SKMatrix> MatrixCalls { get; } = new();
    public int SaveCount { get; private set; }
    public int RestoreCount { get; private set; }

    public void Save() => SaveCount++;
    public void Restore() => RestoreCount++;
    public void SetMatrix(SKMatrix matrix) => MatrixCalls.Add(matrix);

    public void DrawPolygon(ReadOnlySpan<SKPoint> points, RgbColor stroke, int thickness)
    {
        PolygonCalls.Add(new PolygonCall(points.ToArray(), stroke, thickness));
    }

    public void DrawText(string text, int x, int y, RgbColor color, int fontSize)
    {
        TextCalls.Add(new TextCall(text, x, y, color, fontSize));
    }

    public void DrawCircle(int centerX, int centerY, int radius, RgbColor stroke, int thickness)
    {
        CircleCalls.Add(new CircleCall(centerX, centerY, radius, stroke, thickness));
    }

    public void DrawRect(int x, int y, int width, int height, RgbColor stroke, int thickness)
    {
        RectCalls.Add(new RectCall(x, y, width, height, stroke, thickness));
    }

    public void DrawLine(int x1, int y1, int x2, int y2, RgbColor stroke, int thickness)
    {
        LineCalls.Add(new LineCall(x1, y1, x2, y2, stroke, thickness));
    }

    public void DrawJpeg(in ReadOnlySpan<byte> jpegData, int x, int y, int width, int height)
    {
        // Not used in these tests
    }

    public void Clear()
    {
        PolygonCalls.Clear();
        CircleCalls.Clear();
        TextCalls.Clear();
        RectCalls.Clear();
        LineCalls.Clear();
        MatrixCalls.Clear();
        SaveCount = 0;
        RestoreCount = 0;
    }
}

/// <summary>
/// Mock stage implementation for testing decoders.
/// </summary>
public class MockStage : IStage
{
    private readonly MockCanvas _canvas;
    public ulong? LastFrameId { get; private set; }
    public int FrameStartCount { get; private set; }
    public int FrameEndCount { get; private set; }
    public List<byte> ClearedLayers { get; } = new();
    public List<byte> RemainedLayers { get; } = new();

    public MockStage(MockCanvas canvas)
    {
        _canvas = canvas;
    }

    public ICanvas this[byte layerId] => _canvas;

    public void OnFrameStart(ulong frameId)
    {
        LastFrameId = frameId;
        FrameStartCount++;
    }

    public void OnFrameEnd()
    {
        FrameEndCount++;
    }

    public void Clear(byte layerId)
    {
        ClearedLayers.Add(layerId);
    }

    public void Remain(byte layerId)
    {
        RemainedLayers.Add(layerId);
    }

    public bool TryCopyFrame(out RefArray<Lease<ILayer>>? copy)
    {
        copy = null;
        return false;
    }
}
