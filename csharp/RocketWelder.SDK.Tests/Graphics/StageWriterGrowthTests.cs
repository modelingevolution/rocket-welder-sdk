using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorBlaze.VectorGraphics;
using BlazorBlaze.VectorGraphics.Protocol;
using RocketWelder.SDK.Graphics;
using RocketWelder.SDK.Transport;
using Xunit;

namespace RocketWelder.SDK.Tests.Graphics;

/// <summary>
/// Bug 021: a layer used to be a fixed 256 KB rent and the frame buffer a fixed 1 MB rent; the V2 encoder writes
/// into a Span with no bounds check, so an overlay bigger than that threw <c>ArgumentException: Destination is
/// too short</c> / <c>IndexOutOfRangeException</c> out of the caller's draw code (rw2 lost its Blazor circuit on a
/// 146-point program). The buffers now grow; these tests pin that a frame above both limits is sent whole.
/// </summary>
public class StageWriterGrowthTests
{
    private const int InitialLayerBytes = 256 * 1024;
    private const int InitialWriterBytes = 1024 * 1024;

    private sealed class CapturingSink : IFrameSink
    {
        public List<byte[]> Frames { get; } = new();
        public void WriteFrame(ReadOnlySpan<byte> frameData) => Frames.Add(frameData.ToArray());
        public ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frameData) { Frames.Add(frameData.ToArray()); return ValueTask.CompletedTask; }
        public void Flush() { }
        public Task FlushAsync() => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // Frame = [frameId u64][layerCount u8] { [layerId][FrameType.Master][opCount varint] ops... }* [FF FF]
    private static (ulong frameId, byte layerCount, byte layerId, uint opCount, int dataStart) ReadHead(byte[] frame)
    {
        var frameId = BitConverter.ToUInt64(frame, 0);
        var layerCount = frame[8];
        var layerId = frame[9];
        Assert.Equal((byte)FrameType.Master, frame[10]);
        var n = BinaryEncoding.ReadVarint(frame.AsSpan(11), out uint opCount);
        return (frameId, layerCount, layerId, opCount, 11 + n);
    }

    private static void AssertEndMarker(byte[] frame)
    {
        Assert.Equal(ProtocolV2.EndMarkerByte1, frame[^2]);
        Assert.Equal(ProtocolV2.EndMarkerByte2, frame[^1]);
    }

    [Fact]
    public void DrawText_Beyond_The_Initial_Layer_Buffer_Is_Sent_As_One_Whole_Frame()
    {
        // 40 000 short labels ≈ 440 KB of DrawText ops — the bug-012 halo shape (one label = up to 69 DrawText),
        // well past the 256 KB a layer used to be.
        var sink = new CapturingSink();
        using var stageSink = new StageSink(sink, ownsSink: false);
        const int labels = 40_000;

        using (var writer = stageSink.CreateWriter(7))
        {
            var layer = writer.Layer(0);
            layer.SetFontColor(new RgbColor(0, 0, 0));
            for (var i = 0; i < labels; i++)
                layer.DrawText("t12", 1500 + i % 100, 900 + i % 50);
        }

        var frame = Assert.Single(sink.Frames);
        Assert.True(frame.Length > InitialLayerBytes, $"frame is {frame.Length} B, must exceed the old 256 KB layer rent");
        var (frameId, layerCount, layerId, opCount, _) = ReadHead(frame);
        Assert.Equal(7UL, frameId);
        Assert.Equal(1, layerCount);
        Assert.Equal(0, layerId);
        Assert.Equal((uint)labels + 1, opCount);
        AssertEndMarker(frame);
    }

    [Fact]
    public void Ops_Written_Before_The_Growth_Survive_The_Copy()
    {
        // The first op sits at the start of the buffer; after growth it must still be the first op, byte for byte.
        var sink = new CapturingSink();
        using var stageSink = new StageSink(sink, ownsSink: false);
        var expectedFirst = new byte[7];
        VectorGraphicsEncoderV2.WriteSetStroke(expectedFirst, new RgbColor(12, 34, 56, 78));

        using (var writer = stageSink.CreateWriter(1))
        {
            var layer = writer.Layer(3);
            layer.SetStroke(new RgbColor(12, 34, 56, 78));
            for (var i = 0; i < 30_000; i++)
                layer.DrawLine(i, i, i + 10, i + 10);
        }

        var frame = Assert.Single(sink.Frames);
        var (_, _, layerId, opCount, dataStart) = ReadHead(frame);
        Assert.Equal(3, layerId);
        Assert.Equal(30_001u, opCount);
        Assert.Equal(expectedFirst, frame.AsSpan(dataStart, expectedFirst.Length).ToArray());
        Assert.True(frame.Length > InitialLayerBytes);
    }

    [Fact]
    public void DrawJpeg_Larger_Than_The_Initial_Layer_Buffer_Is_Sent_Intact()
    {
        var sink = new CapturingSink();
        using var stageSink = new StageSink(sink, ownsSink: false);
        var jpeg = new byte[600 * 1024];
        new Random(21).NextBytes(jpeg);

        using (var writer = stageSink.CreateWriter(1))
            writer.Layer(0).DrawJpeg(jpeg, 0, 0, 1920, 1080);

        var frame = Assert.Single(sink.Frames);
        AssertEndMarker(frame);
        // The payload is the last thing before the end marker.
        Assert.Equal(jpeg, frame.AsSpan(frame.Length - 2 - jpeg.Length, jpeg.Length).ToArray());
    }

    [Fact]
    public void Frame_Larger_Than_The_Initial_Writer_Buffer_Is_Sent_Whole()
    {
        // Two layers of ~700 KB each: every layer fits its own (grown) buffer, but the assembled frame is ~1.4 MB —
        // more than the writer's 1 MB rent — so the frame buffer must grow too.
        var sink = new CapturingSink();
        using var stageSink = new StageSink(sink, ownsSink: false);
        var jpeg = new byte[700 * 1024];

        using (var writer = stageSink.CreateWriter(1))
        {
            writer.Layer(0).DrawJpeg(jpeg, 0, 0, 10, 10);
            writer.Layer(1).DrawJpeg(jpeg, 0, 0, 10, 10);
        }

        var frame = Assert.Single(sink.Frames);
        Assert.True(frame.Length > InitialWriterBytes, $"frame is {frame.Length} B");
        Assert.Equal(2, frame[8]);
        AssertEndMarker(frame);
    }

    [Fact]
    public void A_Frame_Within_The_Initial_Buffers_Is_Byte_Identical_To_Before()
    {
        // The growth path must not change the encoding of an ordinary frame.
        var sink = new CapturingSink();
        using var stageSink = new StageSink(sink, ownsSink: false);

        using (var writer = stageSink.CreateWriter(5))
        {
            var l = writer.Layer(0);
            l.SetStroke(new RgbColor(1, 2, 3));
            l.SetThickness(2);
            l.DrawLine(10, 20, 30, 40);
            l.DrawText("a", 5, 6);
        }

        var expected = new byte[64];
        var o = VectorGraphicsEncoderV2.WriteMessageHeader(expected, 5, 1);
        o += VectorGraphicsEncoderV2.WriteLayerMaster(expected.AsSpan(o), 0, 4);
        o += VectorGraphicsEncoderV2.WriteSetStroke(expected.AsSpan(o), new RgbColor(1, 2, 3));
        o += VectorGraphicsEncoderV2.WriteSetThickness(expected.AsSpan(o), 2);
        o += VectorGraphicsEncoderV2.WriteDrawLine(expected.AsSpan(o), 10, 20, 30, 40);
        o += VectorGraphicsEncoderV2.WriteDrawText(expected.AsSpan(o), "a", 5, 6);
        o += VectorGraphicsEncoderV2.WriteEndMarker(expected.AsSpan(o));

        Assert.Equal(expected.AsSpan(0, o).ToArray(), Assert.Single(sink.Frames));
    }

    [Fact]
    public void A_Layer_Past_The_Hard_Ceiling_Fails_With_A_Clear_Message_Not_A_Span_Error()
    {
        var sink = new CapturingSink();
        using var stageSink = new StageSink(sink, ownsSink: false);
        var tooBig = new byte[64 * 1024 * 1024 + 1];

        using var writer = stageSink.CreateWriter(1);
        var ex = Assert.Throws<InvalidOperationException>(() => writer.Layer(0).DrawJpeg(tooBig, 0, 0, 1, 1));
        Assert.Contains("64 MB", ex.Message);
    }
}
