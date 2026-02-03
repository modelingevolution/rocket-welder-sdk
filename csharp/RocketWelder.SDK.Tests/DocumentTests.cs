using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RocketWelder.SDK;
using RocketWelder.SDK.Protocols;
using Xunit;
using ProtocolSegmentationFrame = RocketWelder.SDK.Protocols.SegmentationFrame;
using ProtocolSegmentationInstance = RocketWelder.SDK.Protocols.SegmentationInstance;

namespace RocketWelder.SDK.Tests;

public class SegmentationDocumentTests
{
    [Fact]
    public void Load_EmptyData_ReturnsEmptyDocument()
    {
        var data = Array.Empty<byte>();
        var doc = SegmentationDocument.Load(data);

        Assert.Equal(0, doc.FrameCount);
        Assert.Empty(doc.FrameIds);
    }

    [Fact]
    public void Load_SingleFrame_CanAccessById()
    {
        // Arrange: Create a frame with segmentation data
        ulong frameId = 12345;
        uint width = 1920;
        uint height = 1080;
        var points = new Point[] { new(100, 100), new(200, 150), new(150, 200) };
        var instances = new[] { new ProtocolSegmentationInstance(1, 0, (Protocols.Confidence)0.95f, points) };
        var frame = new ProtocolSegmentationFrame(frameId, width, height, instances);

        // Encode to binary
        var buffer = new byte[4096];
        int written = SegmentationProtocol.Write(buffer, frame);

        // Wrap with varint length prefix
        var lengthPrefixed = new byte[written + 5];
        using var ms = new MemoryStream(lengthPrefixed);
        ms.WriteVarint((uint)written);
        var headerSize = (int)ms.Position;
        Buffer.BlockCopy(buffer, 0, lengthPrefixed, headerSize, written);
        var finalData = lengthPrefixed.AsSpan(0, headerSize + written).ToArray();

        // Act
        using var doc = SegmentationDocument.Load(finalData);

        // Assert
        Assert.Equal(1, doc.FrameCount);
        Assert.True(doc.ContainsFrame(frameId));
        Assert.Contains(frameId, doc.FrameIds);

        using var handle = doc.GetFrame(frameId);
        Assert.Equal(frameId, handle.FrameId);
        Assert.Equal(width, handle.Width);
        Assert.Equal(height, handle.Height);
        Assert.Equal(1, handle.Instances.Length);
        Assert.Equal(1, handle.Instances[0].ClassId);
        Assert.Equal(0, handle.Instances[0].InstanceId);
        Assert.Equal(3, handle.Instances[0].Points.Length);
    }

    [Fact]
    public void TryGetFrame_NonExistent_ReturnsFalse()
    {
        var doc = SegmentationDocument.Load(Array.Empty<byte>());

        Assert.False(doc.TryGetFrame(999, out _));
    }

    [Fact]
    public void GetFrame_NonExistent_ThrowsKeyNotFoundException()
    {
        var doc = SegmentationDocument.Load(Array.Empty<byte>());

        Assert.Throws<KeyNotFoundException>(() => doc.GetFrame(999));
    }

    [Fact]
    public void Dispose_PreventsFurtherAccess()
    {
        var doc = SegmentationDocument.Load(Array.Empty<byte>());
        doc.Dispose();

        Assert.Throws<ObjectDisposedException>(() => doc.GetFrame(1));
    }

    [Fact]
    public void FrameHandle_DisposedCorrectly_DoesNotThrow()
    {
        // Arrange: Create a simple frame
        ulong frameId = 1;
        var points = new Point[] { new(10, 20) };
        var instances = new[] { new ProtocolSegmentationInstance(0, 0, (Protocols.Confidence)0.95f, points) };
        var frame = new ProtocolSegmentationFrame(frameId, 100, 100, instances);

        var buffer = new byte[1024];
        int written = SegmentationProtocol.Write(buffer, frame);

        var lengthPrefixed = new byte[written + 5];
        using var ms = new MemoryStream(lengthPrefixed);
        ms.WriteVarint((uint)written);
        var headerSize = (int)ms.Position;
        Buffer.BlockCopy(buffer, 0, lengthPrefixed, headerSize, written);
        var finalData = lengthPrefixed.AsSpan(0, headerSize + written).ToArray();

        using var doc = SegmentationDocument.Load(finalData);

        // Act & Assert - multiple dispose is safe
        using (var handle = doc.GetFrame(frameId))
        {
            _ = handle.Instances[0].Points.Length;
        }
        // Handle disposed - no exception
    }

    [Fact]
    public async Task LoadAsync_FromFile_Works()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            ulong frameId = 42;
            var points = new Point[] { new(50, 50) };
            var instances = new[] { new ProtocolSegmentationInstance(2, 1, (Protocols.Confidence)0.95f, points) };
            var frame = new ProtocolSegmentationFrame(frameId, 640, 480, instances);

            var buffer = new byte[1024];
            int written = SegmentationProtocol.Write(buffer, frame);

            var lengthPrefixed = new byte[written + 5];
            using var ms = new MemoryStream(lengthPrefixed);
            ms.WriteVarint((uint)written);
            var headerSize = (int)ms.Position;
            Buffer.BlockCopy(buffer, 0, lengthPrefixed, headerSize, written);

            await File.WriteAllBytesAsync(tempFile, lengthPrefixed.AsSpan(0, headerSize + written).ToArray());

            // Act
            using var doc = await SegmentationDocument.LoadAsync(tempFile);

            // Assert
            Assert.Equal(1, doc.FrameCount);
            Assert.True(doc.ContainsFrame(frameId));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}

public class KeyPointsDocumentTests
{
    private static byte[] CreateKeyPointsData(ulong frameId, IEnumerable<(int id, Point point, float confidence)> keypoints)
    {
        using var stream = new MemoryStream();
        using var sink = new KeyPointsSink(stream, masterFrameInterval: 1, leaveOpen: true);

        using (var writer = sink.CreateWriter(frameId))
        {
            foreach (var (id, point, confidence) in keypoints)
            {
                writer.Append(id, point, confidence);
            }
        }

        return stream.ToArray();
    }

    [Fact]
    public void Load_EmptyData_ReturnsEmptyDocument()
    {
        var data = Array.Empty<byte>();
        var doc = KeyPointsDocument.Load(data);

        Assert.Equal(0, doc.FrameCount);
        Assert.Empty(doc.FrameIds);
    }

    [Fact]
    public void Load_SingleMasterFrame_CanAccessById()
    {
        // Arrange
        ulong frameId = 100;
        var keypoints = new[]
        {
            (id: 0, point: new Point(100, 200), confidence: 0.95f),
            (id: 1, point: new Point(150, 250), confidence: 0.88f)
        };

        var data = CreateKeyPointsData(frameId, keypoints);

        // Act
        using var doc = KeyPointsDocument.Load(data);

        // Assert
        Assert.Equal(1, doc.FrameCount);
        Assert.True(doc.ContainsFrame(frameId));

        var decoded = doc.GetFrame(frameId);
        Assert.Equal(frameId, decoded.FrameId);
        // IsDelta is no longer exposed - it's a wire format detail
        Assert.Equal(2, decoded.Count);
        var kps = decoded.KeyPoints.Span;
        Assert.Equal(0, kps[0].Id);
        Assert.Equal(100, kps[0].Position.X);
        Assert.Equal(200, kps[0].Position.Y);
    }

    [Fact]
    public void TryGetFrame_NonExistent_ReturnsFalse()
    {
        var doc = KeyPointsDocument.Load(Array.Empty<byte>());

        Assert.False(doc.TryGetFrame(999, out _));
    }

    [Fact]
    public void GetFrame_NonExistent_ThrowsKeyNotFoundException()
    {
        var doc = KeyPointsDocument.Load(Array.Empty<byte>());

        Assert.Throws<KeyNotFoundException>(() => doc.GetFrame(999));
    }

    [Fact]
    public void Indexer_SameAsGetFrame()
    {
        // Arrange
        ulong frameId = 50;
        var keypoints = new[] { (id: 0, point: new Point(10, 20), confidence: 0.9f) };
        var data = CreateKeyPointsData(frameId, keypoints);

        using var doc = KeyPointsDocument.Load(data);

        // Act
        var fromIndexer = doc[frameId];
        var fromGet = doc.GetFrame(frameId);

        // Assert
        Assert.Equal(fromGet.FrameId, fromIndexer.FrameId);
        Assert.Equal(fromGet.Count, fromIndexer.Count);
    }

    [Fact]
    public void Dispose_PreventsFurtherAccess()
    {
        var doc = KeyPointsDocument.Load(Array.Empty<byte>());
        doc.Dispose();

        Assert.Throws<ObjectDisposedException>(() => doc.GetFrame(1));
    }

    [Fact]
    public void Frames_EnumeratesAllFrames()
    {
        // Arrange
        ulong frameId = 77;
        var keypoints = new[] { (id: 0, point: new Point(5, 10), confidence: 0.8f) };
        var data = CreateKeyPointsData(frameId, keypoints);

        using var doc = KeyPointsDocument.Load(data);

        // Act & Assert
        var frames = doc.Frames.ToList();
        Assert.Single(frames);
        Assert.Equal(frameId, frames[0].FrameId);
    }

    [Fact]
    public async Task LoadAsync_FromFile_Works()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            ulong frameId = 99;
            var keypoints = new[] { (id: 0, point: new Point(30, 40), confidence: 0.75f) };
            var data = CreateKeyPointsData(frameId, keypoints);

            await File.WriteAllBytesAsync(tempFile, data);

            using var doc = await KeyPointsDocument.LoadAsync(tempFile);

            Assert.Equal(1, doc.FrameCount);
            Assert.True(doc.ContainsFrame(frameId));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
