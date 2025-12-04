using System;
using System.IO;
using System.Threading.Tasks;
using RocketWelder.SDK.Transport;
using Xunit;
using Xunit.Abstractions;

namespace RocketWelder.SDK.Tests.Transport;

/// <summary>
/// Tests for Stream-based transport (MemoryStream, FileStream).
/// </summary>
public class StreamTransportTests
{
    private readonly ITestOutputHelper _output;

    public StreamTransportTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task StreamTransport_RoundTrip_PreservesData()
    {
        // Arrange
        using var stream = new MemoryStream();
        var testData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        // Act - Write
        using (var sink = new StreamFrameSink(stream, leaveOpen: true))
        {
            sink.WriteFrame(testData);
        }

        // Act - Read
        stream.Position = 0;
        using var source = new StreamFrameSource(stream);
        var frame = await source.ReadFrameAsync();

        // Assert
        Assert.Equal(testData, frame.ToArray());
        _output.WriteLine($"Successfully wrote and read {testData.Length} bytes via stream");
    }

    [Fact]
    public async Task StreamTransport_MultipleFrames_PreservesOrder()
    {
        // Arrange
        using var stream = new MemoryStream();
        var frames = new[]
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 4, 5, 6, 7 },
            new byte[] { 8 },
            new byte[] { 9, 10, 11, 12, 13 }
        };

        // Act - Write all frames
        using (var sink = new StreamFrameSink(stream, leaveOpen: true))
        {
            foreach (var frame in frames)
            {
                sink.WriteFrame(frame);
            }
        }

        // Act - Read all frames
        stream.Position = 0;
        using var source = new StreamFrameSource(stream);

        for (int i = 0; i < frames.Length; i++)
        {
            var frame = await source.ReadFrameAsync();
            Assert.Equal(frames[i], frame.ToArray());
        }

        // Verify end of stream
        var emptyFrame = await source.ReadFrameAsync();
        Assert.True(emptyFrame.IsEmpty);

        _output.WriteLine($"Successfully wrote and read {frames.Length} frames in order");
    }

    [Fact]
    public async Task StreamTransport_LargeFrame_HandledCorrectly()
    {
        // Arrange - Large frame (5MB)
        var largeFrame = new byte[5 * 1024 * 1024];
        new Random(42).NextBytes(largeFrame);

        using var stream = new MemoryStream();

        // Act - Write
        using (var sink = new StreamFrameSink(stream, leaveOpen: true))
        {
            await sink.WriteFrameAsync(largeFrame);
        }

        // Act - Read
        stream.Position = 0;
        using var source = new StreamFrameSource(stream);
        var frame = await source.ReadFrameAsync();

        // Assert
        Assert.Equal(largeFrame.Length, frame.Length);
        Assert.Equal(largeFrame, frame.ToArray());

        _output.WriteLine($"Successfully transferred {largeFrame.Length / 1024 / 1024}MB frame via stream");
    }

    [Fact]
    public async Task StreamTransport_EmptyFrame_HandledCorrectly()
    {
        // Arrange
        using var stream = new MemoryStream();
        var emptyFrame = Array.Empty<byte>();

        // Act - Write
        using (var sink = new StreamFrameSink(stream, leaveOpen: true))
        {
            sink.WriteFrame(emptyFrame);
        }

        // Act - Read
        stream.Position = 0;
        using var source = new StreamFrameSource(stream);
        var frame = await source.ReadFrameAsync();

        // Assert
        Assert.True(frame.IsEmpty);
        _output.WriteLine("Empty frame handled correctly");
    }

    [Fact]
    public async Task StreamTransport_FileStream_RoundTrip()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var testData = new byte[] { 42, 43, 44, 45, 46 };

        try
        {
            // Act - Write to file
            using (var fileStream = File.Create(tempFile))
            using (var sink = new StreamFrameSink(fileStream))
            {
                sink.WriteFrame(testData);
            }

            // Act - Read from file
            using var readStream = File.OpenRead(tempFile);
            using var source = new StreamFrameSource(readStream);
            var frame = await source.ReadFrameAsync();

            // Assert
            Assert.Equal(testData, frame.ToArray());
            _output.WriteLine($"Successfully wrote and read from file: {tempFile}");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void StreamTransport_LeaveOpenTrue_StreamNotDisposed()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        using (var sink = new StreamFrameSink(stream, leaveOpen: true))
        {
            sink.WriteFrame(new byte[] { 1, 2, 3 });
        }

        // Assert - Stream should still be usable
        stream.Position = 0;
        Assert.True(stream.CanRead);
        Assert.True(stream.CanWrite);
    }

    [Fact]
    public void StreamTransport_LeaveOpenFalse_StreamDisposed()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        using (var sink = new StreamFrameSink(stream, leaveOpen: false))
        {
            sink.WriteFrame(new byte[] { 1, 2, 3 });
        }

        // Assert - Stream should be disposed
        Assert.Throws<ObjectDisposedException>(() => stream.Position = 0);
    }

    [Fact]
    public async Task StreamTransport_HasMoreFrames_CorrectlyReportsEof()
    {
        // Arrange
        using var stream = new MemoryStream();

        using (var sink = new StreamFrameSink(stream, leaveOpen: true))
        {
            sink.WriteFrame(new byte[] { 1, 2, 3 });
        }

        stream.Position = 0;
        using var source = new StreamFrameSource(stream);

        // Assert - Before reading
        Assert.True(source.HasMoreFrames);

        // Read frame
        var frame1 = await source.ReadFrameAsync();
        Assert.False(frame1.IsEmpty);

        // Try to read past end
        var frame2 = await source.ReadFrameAsync();
        Assert.True(frame2.IsEmpty);
        Assert.False(source.HasMoreFrames);
    }

    [Fact]
    public async Task StreamTransport_VarintFraming_CorrectFormat()
    {
        // Arrange
        using var stream = new MemoryStream();
        var testData = new byte[] { 0xAA, 0xBB, 0xCC };

        // Act - Write
        using (var sink = new StreamFrameSink(stream, leaveOpen: true))
        {
            sink.WriteFrame(testData);
        }

        // Verify format: [varint length][data]
        stream.Position = 0;
        var rawBytes = stream.ToArray();

        // For length 3, varint is just 0x03 (single byte)
        Assert.Equal(0x03, rawBytes[0]);
        Assert.Equal(0xAA, rawBytes[1]);
        Assert.Equal(0xBB, rawBytes[2]);
        Assert.Equal(0xCC, rawBytes[3]);

        _output.WriteLine($"Stream format: {BitConverter.ToString(rawBytes)}");
    }

    [Fact]
    public async Task StreamTransport_LargeLength_VarintMultibyte()
    {
        // Arrange - 300 bytes requires 2-byte varint (300 = 0xAC 0x02)
        using var stream = new MemoryStream();
        var testData = new byte[300];
        Array.Fill<byte>(testData, 0x42);

        // Act - Write
        using (var sink = new StreamFrameSink(stream, leaveOpen: true))
        {
            sink.WriteFrame(testData);
        }

        // Verify varint encoding
        stream.Position = 0;
        var rawBytes = stream.ToArray();

        // 300 in varint = 0xAC 0x02
        Assert.Equal(0xAC, rawBytes[0]);
        Assert.Equal(0x02, rawBytes[1]);
        Assert.Equal(300 + 2, rawBytes.Length); // 300 data + 2 varint bytes

        // Verify can read back
        stream.Position = 0;
        using var source = new StreamFrameSource(stream);
        var frame = await source.ReadFrameAsync();
        Assert.Equal(testData, frame.ToArray());

        _output.WriteLine($"300-byte frame with 2-byte varint length prefix verified");
    }
}
