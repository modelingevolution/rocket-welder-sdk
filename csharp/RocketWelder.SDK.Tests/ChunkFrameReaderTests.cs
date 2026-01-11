using System.IO;
using RocketWelder.SDK.Protocols;
using RocketWelder.SDK.Transport;
using Xunit;

namespace RocketWelder.SDK.Tests;

public class ChunkFrameReaderTests
{
    [Fact]
    public void ReadFrame_SingleFrame_ReturnsData()
    {
        // Arrange
        var testData = new byte[] { 1, 2, 3, 4, 5 };
        var chunk = WriteFramesToChunk(testData);

        // Act
        var reader = new ChunkFrameReader(chunk);
        var frame = reader.ReadFrame();

        // Assert
        Assert.Equal(testData, frame.ToArray());
    }

    [Fact]
    public void ReadFrame_MultipleFrames_ReturnsInOrder()
    {
        // Arrange
        var frames = new[]
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 4, 5, 6, 7 },
            new byte[] { 8 },
            new byte[] { 9, 10, 11, 12, 13 }
        };
        var chunk = WriteFramesToChunk(frames);

        // Act & Assert
        var reader = new ChunkFrameReader(chunk);
        foreach (var expected in frames)
        {
            var frame = reader.ReadFrame();
            Assert.Equal(expected, frame.ToArray());
        }

        // End of data
        var empty = reader.ReadFrame();
        Assert.True(empty.IsEmpty);
    }

    [Fact]
    public void ReadFrame_EmptyChunk_ReturnsEmpty()
    {
        // Arrange
        var chunk = Array.Empty<byte>();

        // Act
        var reader = new ChunkFrameReader(chunk);
        var frame = reader.ReadFrame();

        // Assert
        Assert.True(frame.IsEmpty);
    }

    [Fact]
    public void ReadFrame_LargeFrame_ReturnsData()
    {
        // Arrange - 300 bytes requires 2-byte varint
        var testData = new byte[300];
        Array.Fill<byte>(testData, 0x42);
        var chunk = WriteFramesToChunk(testData);

        // Act
        var reader = new ChunkFrameReader(chunk);
        var frame = reader.ReadFrame();

        // Assert
        Assert.Equal(testData, frame.ToArray());
    }

    private static byte[] WriteFramesToChunk(params byte[][] frames)
    {
        using var stream = new MemoryStream();
        using var sink = new StreamFrameSink(stream, leaveOpen: true);
        foreach (var frame in frames)
            sink.WriteFrame(frame);
        return stream.ToArray();
    }
}
