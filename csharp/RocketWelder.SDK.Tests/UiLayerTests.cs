using BlazorBlaze.Server;
using BlazorBlaze.VectorGraphics;
using ModelingEvolution.Drawing;
using NSubstitute;
using RocketWelder.SDK.Graphics;
using SkiaSharp;
using Xunit;

namespace RocketWelder.SDK.Tests;

/// <summary>
/// Manual test double for ILayerCanvas that records calls.
/// Needed because ReadOnlySpan parameters can't be captured by NSubstitute.
/// </summary>
internal class RecordingLayerCanvas : ILayerCanvas
{
    public byte LayerId { get; set; }

    // Recorded polygon calls (copied to arrays since spans are transient)
    public List<SKPoint[]> DrawnPolygons { get; } = new();
    public List<(int cx, int cy, int r)> DrawnCircles { get; } = new();
    public List<(int x1, int y1, int x2, int y2)> DrawnLines { get; } = new();
    public List<(int x, int y, int w, int h)> DrawnRectangles { get; } = new();
    public List<(string text, int x, int y)> DrawnTexts { get; } = new();

    public List<RgbColor> StrokeColors { get; } = new();
    public List<RgbColor> FillColors { get; } = new();
    public List<int> Thicknesses { get; } = new();
    public List<int> FontSizes { get; } = new();
    public List<RgbColor> FontColors { get; } = new();

    public int SaveCount { get; private set; }
    public int RestoreCount { get; private set; }
    public int ResetContextCount { get; private set; }
    public int MasterCount { get; private set; }
    public int RemainCount { get; private set; }
    public int ClearCount { get; private set; }

    public void DrawPolygon(ReadOnlySpan<SKPoint> points) => DrawnPolygons.Add(points.ToArray());
    public void DrawCircle(int centerX, int centerY, int radius) => DrawnCircles.Add((centerX, centerY, radius));
    public void DrawLine(int x1, int y1, int x2, int y2) => DrawnLines.Add((x1, y1, x2, y2));
    public void DrawRectangle(int x, int y, int width, int height) => DrawnRectangles.Add((x, y, width, height));
    public void DrawText(string text, int x, int y) => DrawnTexts.Add((text, x, y));
    public void DrawJpeg(ReadOnlySpan<byte> jpegData, int x, int y, int width, int height) { }

    public void SetStroke(RgbColor color) => StrokeColors.Add(color);
    public void SetFill(RgbColor color) => FillColors.Add(color);
    public void SetThickness(int width) => Thicknesses.Add(width);
    public void SetFontSize(int size) => FontSizes.Add(size);
    public void SetFontColor(RgbColor color) => FontColors.Add(color);

    public void Translate(float dx, float dy) { }
    public void Rotate(float degrees) { }
    public void Scale(float sx, float sy) { }
    public void Skew(float kx, float ky) { }
    public void SetMatrix(SKMatrix matrix) { }

    public void Save() => SaveCount++;
    public void Restore() => RestoreCount++;
    public void ResetContext() => ResetContextCount++;
    public void Master() => MasterCount++;
    public void Remain() => RemainCount++;
    public void Clear() => ClearCount++;
}

public class UiLayerTests
{
    private readonly RecordingLayerCanvas _canvas = new();
    private readonly UiLayer _layer;

    private const uint ViewportWidth = 1920;
    private const uint ViewportHeight = 1080;

    public UiLayerTests()
    {
        _layer = new UiLayer(_canvas, ViewportWidth, ViewportHeight);
    }

    #region Draw(Polygon)

    [Fact]
    public void DrawPolygon_ConvertsPointsToSKPoints()
    {
        var pts = new Point<float>[]
        {
            new(10.5f, 20.3f),
            new(30.7f, 40.1f),
            new(50.9f, 60.6f)
        };
        var polygon = new Polygon<float>(pts);

        _layer.Draw(polygon);

        Assert.Single(_canvas.DrawnPolygons);
        var drawn = _canvas.DrawnPolygons[0];
        Assert.Equal(3, drawn.Length);
        Assert.Equal(10.5f, drawn[0].X);
        Assert.Equal(20.3f, drawn[0].Y);
        Assert.Equal(30.7f, drawn[1].X);
        Assert.Equal(40.1f, drawn[1].Y);
        Assert.Equal(50.9f, drawn[2].X);
        Assert.Equal(60.6f, drawn[2].Y);
    }

    [Fact]
    public void DrawPolygon_EmptyPolygon_DoesNotCallCanvas()
    {
        var polygon = new Polygon<float>(Array.Empty<Point<float>>());

        _layer.Draw(polygon);

        Assert.Empty(_canvas.DrawnPolygons);
    }

    [Fact]
    public void DrawPolygon_LargePolygon_GrowsBuffer()
    {
        var pts = new Point<float>[100];
        for (int i = 0; i < 100; i++)
            pts[i] = new Point<float>(i, i * 2);
        var polygon = new Polygon<float>(pts);

        _layer.Draw(polygon);

        Assert.Single(_canvas.DrawnPolygons);
        var drawn = _canvas.DrawnPolygons[0];
        Assert.Equal(100, drawn.Length);
        Assert.Equal(99f, drawn[99].X);
        Assert.Equal(198f, drawn[99].Y);
    }

    #endregion

    #region Draw(Circle)

    [Fact]
    public void DrawCircle_ConvertsToCenterAndRadius()
    {
        var circle = new Circle<float>(new Point<float>(150.7f, 200.3f), 50.9f);

        _layer.Draw(circle);

        Assert.Single(_canvas.DrawnCircles);
        var (cx, cy, r) = _canvas.DrawnCircles[0];
        Assert.Equal(150, cx);
        Assert.Equal(200, cy);
        Assert.Equal(50, r);
    }

    #endregion

    #region Draw(Segment)

    [Fact]
    public void DrawSegment_ConvertsEndpointsToInts()
    {
        var segment = new Segment<float>(
            new Point<float>(10.8f, 20.2f),
            new Point<float>(100.6f, 200.9f));

        _layer.Draw(segment);

        Assert.Single(_canvas.DrawnLines);
        var (x1, y1, x2, y2) = _canvas.DrawnLines[0];
        Assert.Equal(10, x1);
        Assert.Equal(20, y1);
        Assert.Equal(100, x2);
        Assert.Equal(200, y2);
    }

    #endregion

    #region Draw(Line)

    [Fact]
    public void DrawLine_InsideViewport_DrawsClippedSegment()
    {
        // Horizontal line y=540 (middle of 1080 viewport) — should intersect
        var line = Line<float>.From(
            new Point<float>(0, 540),
            new Point<float>(1920, 540));

        _layer.Draw(line);

        Assert.Single(_canvas.DrawnLines);
        var (x1, y1, x2, y2) = _canvas.DrawnLines[0];
        Assert.Equal(540, y1);
        Assert.Equal(540, y2);
    }

    [Fact]
    public void DrawLine_OutsideViewport_DoesNotDraw()
    {
        // Horizontal line y=-100, completely outside viewport
        var line = Line<float>.From(
            new Point<float>(0, -100),
            new Point<float>(100, -100));

        _layer.Draw(line);

        Assert.Empty(_canvas.DrawnLines);
    }

    #endregion

    #region Draw(Rectangle)

    [Fact]
    public void DrawRectangle_ConvertsToInts()
    {
        var rect = new Rectangle<float>(10.5f, 20.3f, 100.8f, 200.1f);

        _layer.Draw(rect);

        Assert.Single(_canvas.DrawnRectangles);
        var (x, y, w, h) = _canvas.DrawnRectangles[0];
        Assert.Equal(10, x);
        Assert.Equal(20, y);
        Assert.Equal(100, w);
        Assert.Equal(200, h);
    }

    #endregion

    #region Draw(Triangle)

    [Fact]
    public void DrawTriangle_Converts3VerticesToPolygon()
    {
        var triangle = new Triangle<float>(
            new Point<float>(10f, 20f),
            new Point<float>(30f, 40f),
            new Point<float>(50f, 60f));

        _layer.Draw(triangle);

        Assert.Single(_canvas.DrawnPolygons);
        var drawn = _canvas.DrawnPolygons[0];
        Assert.Equal(3, drawn.Length);
        Assert.Equal(10f, drawn[0].X);
        Assert.Equal(20f, drawn[0].Y);
        Assert.Equal(30f, drawn[1].X);
        Assert.Equal(40f, drawn[1].Y);
        Assert.Equal(50f, drawn[2].X);
        Assert.Equal(60f, drawn[2].Y);
    }

    #endregion

    #region Draw(Point)

    [Fact]
    public void DrawPoint_DrawsCircleWithMarkerRadius()
    {
        var point = new Point<float>(100.7f, 200.3f);

        _layer.Draw(point);

        Assert.Single(_canvas.DrawnCircles);
        var (cx, cy, r) = _canvas.DrawnCircles[0];
        Assert.Equal(100, cx);
        Assert.Equal(200, cy);
        Assert.Equal(UiLayer.DefaultPointMarkerRadius, r);
    }

    #endregion

    #region Passthrough Methods

    [Fact]
    public void SetStroke_DelegatesToInner()
    {
        var color = new RgbColor(255, 0, 0);
        _layer.SetStroke(color);
        Assert.Single(_canvas.StrokeColors);
        Assert.Equal(color, _canvas.StrokeColors[0]);
    }

    [Fact]
    public void SetFill_DelegatesToInner()
    {
        var color = new RgbColor(0, 255, 0);
        _layer.SetFill(color);
        Assert.Single(_canvas.FillColors);
        Assert.Equal(color, _canvas.FillColors[0]);
    }

    [Fact]
    public void SetThickness_DelegatesToInner()
    {
        _layer.SetThickness(5);
        Assert.Single(_canvas.Thicknesses);
        Assert.Equal(5, _canvas.Thicknesses[0]);
    }

    [Fact]
    public void DrawText_DelegatesToInner()
    {
        _layer.DrawText("hello", 10, 20);
        Assert.Single(_canvas.DrawnTexts);
        Assert.Equal(("hello", 10, 20), _canvas.DrawnTexts[0]);
    }

    [Fact]
    public void SaveRestore_DelegatesToInner()
    {
        _layer.Save();
        _layer.Restore();
        Assert.Equal(1, _canvas.SaveCount);
        Assert.Equal(1, _canvas.RestoreCount);
    }

    [Fact]
    public void Master_DelegatesToInner()
    {
        _layer.Master();
        Assert.Equal(1, _canvas.MasterCount);
    }

    [Fact]
    public void Remain_DelegatesToInner()
    {
        _layer.Remain();
        Assert.Equal(1, _canvas.RemainCount);
    }

    [Fact]
    public void Clear_DelegatesToInner()
    {
        _layer.Clear();
        Assert.Equal(1, _canvas.ClearCount);
    }

    [Fact]
    public void LayerId_DelegatesToInner()
    {
        _canvas.LayerId = 5;
        Assert.Equal(5, _layer.LayerId);
    }

    #endregion
}

public class UiWriterTests
{
    [Fact]
    public void Indexer_ReturnsSameLayerForSameId()
    {
        var writer = Substitute.For<IStageWriter>();
        var canvas0 = new RecordingLayerCanvas();
        writer[0].Returns(canvas0);

        var ui = new UiWriter(writer, 1920, 1080);

        var layer1 = ui[0];
        var layer2 = ui[0];

        Assert.Same(layer1, layer2);
    }

    [Fact]
    public void Indexer_ReturnsDifferentLayersForDifferentIds()
    {
        var writer = Substitute.For<IStageWriter>();
        writer[0].Returns(new RecordingLayerCanvas());
        writer[1].Returns(new RecordingLayerCanvas());

        var ui = new UiWriter(writer, 1920, 1080);

        var layer0 = ui[0];
        var layer1 = ui[1];

        Assert.NotSame(layer0, layer1);
    }

    [Fact]
    public void FrameId_DelegatesToWriter()
    {
        var writer = Substitute.For<IStageWriter>();
        writer.FrameId.Returns(42UL);

        var ui = new UiWriter(writer, 1920, 1080);

        Assert.Equal(42UL, ui.FrameId);
    }

    [Fact]
    public void Dispose_DisposesWriter()
    {
        var writer = Substitute.For<IStageWriter>();
        var ui = new UiWriter(writer, 1920, 1080);

        ui.Dispose();

        writer.Received(1).Dispose();
    }
}

public class UiSinkTests
{
    [Fact]
    public void CreateWriter_UsesFrameIdFromFunc()
    {
        var stageSink = Substitute.For<IStageSink>();
        var writer = Substitute.For<IStageWriter>();
        writer.FrameId.Returns(123UL);
        stageSink.CreateWriter(123UL).Returns(writer);

        var sink = new UiSink(stageSink, 1920, 1080, () => 123UL, ownsSink: false);

        var ui = sink.CreateWriter();

        stageSink.Received(1).CreateWriter(123UL);
        Assert.Equal(123UL, ui.FrameId);
    }

    [Fact]
    public void CreateWriter_FrameIdChangesPerCall()
    {
        var stageSink = Substitute.For<IStageSink>();
        ulong currentFrame = 100;

        var writer100 = Substitute.For<IStageWriter>();
        writer100.FrameId.Returns(100UL);
        var writer101 = Substitute.For<IStageWriter>();
        writer101.FrameId.Returns(101UL);

        stageSink.CreateWriter(100UL).Returns(writer100);
        stageSink.CreateWriter(101UL).Returns(writer101);

        var sink = new UiSink(stageSink, 1920, 1080, () => currentFrame, ownsSink: false);

        var ui1 = sink.CreateWriter();
        Assert.Equal(100UL, ui1.FrameId);

        currentFrame = 101;
        var ui2 = sink.CreateWriter();
        Assert.Equal(101UL, ui2.FrameId);
    }

    [Fact]
    public void Width_ReturnsConfiguredValue()
    {
        var sink = new UiSink(Substitute.For<IStageSink>(), 1920, 1080, () => 0, ownsSink: false);
        Assert.Equal(1920u, sink.Width);
    }

    [Fact]
    public void Height_ReturnsConfiguredValue()
    {
        var sink = new UiSink(Substitute.For<IStageSink>(), 1920, 1080, () => 0, ownsSink: false);
        Assert.Equal(1080u, sink.Height);
    }

    [Fact]
    public void Dispose_DisposesInnerWhenOwned()
    {
        var stageSink = Substitute.For<IStageSink>();
        var sink = new UiSink(stageSink, 1920, 1080, () => 0, ownsSink: true);

        sink.Dispose();

        stageSink.Received(1).Dispose();
    }

    [Fact]
    public void Dispose_DoesNotDisposeInnerWhenNotOwned()
    {
        var stageSink = Substitute.For<IStageSink>();
        var sink = new UiSink(stageSink, 1920, 1080, () => 0, ownsSink: false);

        sink.Dispose();

        stageSink.DidNotReceive().Dispose();
    }

    [Fact]
    public void CreateWriter_AfterDispose_Throws()
    {
        var stageSink = Substitute.For<IStageSink>();
        var sink = new UiSink(stageSink, 1920, 1080, () => 0, ownsSink: false);
        sink.Dispose();

        Assert.Throws<ObjectDisposedException>(() => sink.CreateWriter());
    }
}
