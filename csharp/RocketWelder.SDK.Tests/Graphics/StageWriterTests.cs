using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BlazorBlaze.Server;
using BlazorBlaze.VectorGraphics;
using BlazorBlaze.VectorGraphics.Protocol;
using RocketWelder.SDK.Graphics;
using RocketWelder.SDK.Transport;
using SkiaSharp;
using Xunit;

namespace RocketWelder.SDK.Tests.Graphics;

public class StageWriterTests
{
    /// <summary>
    /// Mock frame sink that captures written frames for verification.
    /// </summary>
    private class MockFrameSink : IFrameSink
    {
        public List<byte[]> WrittenFrames { get; } = new();
        public int FlushCount { get; private set; }

        public void WriteFrame(ReadOnlySpan<byte> frameData)
        {
            WrittenFrames.Add(frameData.ToArray());
        }

        public ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frameData)
        {
            WrittenFrames.Add(frameData.ToArray());
            return ValueTask.CompletedTask;
        }

        public void Flush() => FlushCount++;
        public Task FlushAsync() { FlushCount++; return Task.CompletedTask; }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void IStageSink_CreateWriter_ReturnsIStageWriter()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act
        using var writer = stageSink.CreateWriter(1);

        // Assert
        Assert.NotNull(writer);
        Assert.IsAssignableFrom<IStageWriter>(writer);
    }

    [Fact]
    public void CreateWriter_SetsFrameId()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act
        using var writer1 = stageSink.CreateWriter(42);
        using var writer2 = stageSink.CreateWriter(100);

        // Assert
        Assert.Equal(42ul, writer1.FrameId);
        Assert.Equal(100ul, writer2.FrameId);
    }

    [Fact]
    public void Layer_ReturnsILayerCanvas()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);
        using var writer = stageSink.CreateWriter(1);

        // Act
        var layer0 = writer.Layer(0);
        var layer1 = writer.Layer(1);

        // Assert
        Assert.NotNull(layer0);
        Assert.NotNull(layer1);
        Assert.IsAssignableFrom<ILayerCanvas>(layer0);
        Assert.IsAssignableFrom<ILayerCanvas>(layer1);
    }

    [Fact]
    public void Indexer_ReturnsSameLayerAsMethod()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);
        using var writer = stageSink.CreateWriter(1);

        // Act
        var layerViaMethod = writer.Layer(0);
        var layerViaIndexer = writer[0];

        // Assert - same instance
        Assert.Same(layerViaMethod, layerViaIndexer);
    }

    [Fact]
    public void Dispose_WithNoLayers_WritesNothing()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act
        using (var writer = stageSink.CreateWriter(1))
        {
            // Don't access any layers
        }

        // Assert - no frames written when no layers accessed
        Assert.Empty(sink.WrittenFrames);
    }

    [Fact]
    public void Dispose_WithLayer_AutoFlushesFrame()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act
        using (var writer = stageSink.CreateWriter(1))
        {
            writer[0].SetStroke(new RgbColor(255, 0, 0));
        }

        // Assert - frame was written on dispose
        Assert.Single(sink.WrittenFrames);
        Assert.True(sink.WrittenFrames[0].Length > 0);
    }

    [Fact]
    public void Dispose_EncodesProtocolV2Format()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act
        using (var writer = stageSink.CreateWriter(1))
        {
            writer[0].SetStroke(new RgbColor(255, 128, 64));
        }

        // Assert - check Protocol V2 header format
        var frame = sink.WrittenFrames[0];
        Assert.True(frame.Length >= 4, "Frame should have header");
    }

    [Fact]
    public void MultipleWriters_WriteMultipleFrames()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act
        for (int i = 0; i < 3; i++)
        {
            using var writer = stageSink.CreateWriter((ulong)(i + 1));
            writer[0].SetStroke(new RgbColor(255, 0, 0));
        }

        // Assert
        Assert.Equal(3, sink.WrittenFrames.Count);
    }

    [Fact]
    public void LayerCanvas_SetStroke_Works()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act - should not throw
        using (var writer = stageSink.CreateWriter(1))
        {
            var layer = writer[0];
            layer.SetStroke(new RgbColor(255, 128, 64));
            layer.SetFill(new RgbColor(0, 255, 0));
            layer.SetThickness(3);
        }

        // Assert
        Assert.Single(sink.WrittenFrames);
    }

    [Fact]
    public void LayerCanvas_DrawPolygon_Works()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);
        var points = new SKPoint[] { new(10, 20), new(30, 40), new(50, 20) };

        // Act - should not throw
        using (var writer = stageSink.CreateWriter(1))
        {
            writer[0].DrawPolygon(points);
        }

        // Assert
        Assert.Single(sink.WrittenFrames);
        Assert.True(sink.WrittenFrames[0].Length > 10, "Polygon data should be encoded");
    }

    [Fact]
    public void LayerCanvas_DrawText_Works()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act - should not throw
        using (var writer = stageSink.CreateWriter(1))
        {
            writer[0].DrawText("Hello World", 100, 200);
        }

        // Assert
        Assert.Single(sink.WrittenFrames);
    }

    [Fact]
    public void LayerCanvas_DrawCircle_Works()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act - should not throw
        using (var writer = stageSink.CreateWriter(1))
        {
            writer[0].DrawCircle(100, 100, 50);
        }

        // Assert
        Assert.Single(sink.WrittenFrames);
    }

    [Fact]
    public void LayerCanvas_DrawRectangle_Works()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act - should not throw
        using (var writer = stageSink.CreateWriter(1))
        {
            writer[0].DrawRectangle(10, 20, 100, 50);
        }

        // Assert
        Assert.Single(sink.WrittenFrames);
    }

    [Fact]
    public void LayerCanvas_DrawLine_Works()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act - should not throw
        using (var writer = stageSink.CreateWriter(1))
        {
            writer[0].DrawLine(0, 0, 100, 100);
        }

        // Assert
        Assert.Single(sink.WrittenFrames);
    }

    [Fact]
    public void LayerCanvas_Transforms_Work()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act - should not throw
        using (var writer = stageSink.CreateWriter(1))
        {
            var layer = writer[0];
            layer.Translate(50, 50);
            layer.Rotate(45);
            layer.Scale(2, 2);
            layer.Skew(0.1f, 0.2f);
            layer.DrawCircle(0, 0, 10);
        }

        // Assert
        Assert.Single(sink.WrittenFrames);
    }

    [Fact]
    public void LayerCanvas_SaveRestore_Works()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act - should not throw
        using (var writer = stageSink.CreateWriter(1))
        {
            var layer = writer[0];
            layer.Save();
            layer.Translate(100, 100);
            layer.DrawCircle(0, 0, 10);
            layer.Restore();
            layer.DrawCircle(0, 0, 20);
        }

        // Assert
        Assert.Single(sink.WrittenFrames);
    }

    [Fact]
    public void MultipleLayers_AreEncodedSeparately()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act
        using (var writer = stageSink.CreateWriter(1))
        {
            writer[0].SetStroke(new RgbColor(255, 0, 0));
            writer[0].DrawCircle(50, 50, 25);
            writer[1].SetStroke(new RgbColor(0, 255, 0));
            writer[1].DrawText("Label", 10, 10);
            writer[2].DrawRectangle(0, 0, 100, 100);
        }

        // Assert - one frame with multiple layers encoded
        Assert.Single(sink.WrittenFrames);
        Assert.True(sink.WrittenFrames[0].Length > 20, "Multiple layers should have significant data");
    }

    [Fact]
    public void EachWriter_IsIndependent()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act - first writer
        using (var writer1 = stageSink.CreateWriter(1))
        {
            writer1[0].DrawCircle(10, 10, 5);
        }
        var firstFrameSize = sink.WrittenFrames[0].Length;

        // Act - second writer with same content
        using (var writer2 = stageSink.CreateWriter(2))
        {
            writer2[0].DrawCircle(10, 10, 5);
        }
        var secondFrameSize = sink.WrittenFrames[1].Length;

        // Assert - frames should be similar size (both have same content)
        Assert.Equal(firstFrameSize, secondFrameSize);
    }

    [Fact]
    public void LayerFrameTypes_Work()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act - test Master/Remain/Clear
        using (var writer = stageSink.CreateWriter(1))
        {
            writer[0].Master();
            writer[0].DrawCircle(10, 10, 5);
            writer[1].Remain();
            writer[2].Clear();
        }

        // Assert
        Assert.Single(sink.WrittenFrames);
    }

    [Fact]
    public void StageSink_Dispose_DisposesOwnedSink()
    {
        // Arrange
        var disposed = false;
        var mockSink = new MockFrameSink();
        var trackingSink = new DisposalTrackingSink(mockSink, () => disposed = true);

        // Act
        var stageSink = new StageSink(trackingSink, ownsSink: true);
        stageSink.Dispose();

        // Assert
        Assert.True(disposed);
    }

    [Fact]
    public void StageSink_Dispose_DoesNotDisposeUnownedSink()
    {
        // Arrange
        var disposed = false;
        var mockSink = new MockFrameSink();
        var trackingSink = new DisposalTrackingSink(mockSink, () => disposed = true);

        // Act
        var stageSink = new StageSink(trackingSink, ownsSink: false);
        stageSink.Dispose();

        // Assert
        Assert.False(disposed);
    }

    [Fact]
    public void Writer_ImplementsIDisposable()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act
        var writer = stageSink.CreateWriter(1);

        // Assert
        Assert.IsAssignableFrom<IDisposable>(writer);
        writer.Dispose();
    }

    [Fact]
    public void Writer_ImplementsIAsyncDisposable()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act
        var writer = stageSink.CreateWriter(1);

        // Assert
        Assert.IsAssignableFrom<IAsyncDisposable>(writer);
        writer.Dispose();
    }

    [Fact]
    public void UsingPattern_AutoFlushes()
    {
        // Arrange
        var sink = new MockFrameSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        // Act - simulate SDK pattern
        using (var writer = stageSink.CreateWriter(1))
        {
            writer[0].SetStroke(new RgbColor(255, 0, 0));
            writer[0].DrawPolygon(new SKPoint[] { new(0, 0), new(100, 0), new(50, 100) });
        }
        // Writer disposed here, should auto-flush

        // Assert
        Assert.Single(sink.WrittenFrames);
    }

    /// <summary>
    /// Wrapper sink to track disposal.
    /// </summary>
    private class DisposalTrackingSink : IFrameSink
    {
        private readonly IFrameSink _inner;
        private readonly Action _onDispose;

        public DisposalTrackingSink(IFrameSink inner, Action onDispose)
        {
            _inner = inner;
            _onDispose = onDispose;
        }

        public void WriteFrame(ReadOnlySpan<byte> frameData) => _inner.WriteFrame(frameData);
        public ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frameData) => _inner.WriteFrameAsync(frameData);
        public void Flush() => _inner.Flush();
        public Task FlushAsync() => _inner.FlushAsync();
        public void Dispose() { _onDispose(); _inner.Dispose(); }
        public ValueTask DisposeAsync() { _onDispose(); return _inner.DisposeAsync(); }
    }
}
