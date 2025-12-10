using System;
using System.Runtime.InteropServices;
using Xunit;

namespace RocketWelder.SDK.Tests;

/// <summary>
/// Tests for FrameMetadata structure including cross-platform binary compatibility.
/// </summary>
public class FrameMetadataTests
{
    /// <summary>
    /// Test that the FrameMetadata size is exactly 16 bytes.
    /// This must match the C++ and Python implementations.
    /// </summary>
    [Fact]
    public void Size_IsExactly16Bytes()
    {
        // C++ struct is 16 bytes:
        //   [0-7]   frame_number    - uint64_t
        //   [8-15]  timestamp_ns    - uint64_t
        Assert.Equal(16, FrameMetadata.Size);
        Assert.Equal(16, Marshal.SizeOf<FrameMetadata>());
    }

    /// <summary>
    /// Test that TIMESTAMP_UNAVAILABLE matches C++ UINT64_MAX.
    /// </summary>
    [Fact]
    public void TimestampUnavailable_IsUInt64Max()
    {
        Assert.Equal(ulong.MaxValue, FrameMetadata.TimestampUnavailable);
        Assert.Equal(0xFFFFFFFFFFFFFFFF, FrameMetadata.TimestampUnavailable);
    }

    /// <summary>
    /// Test that FrameMetadata can be read from a span of bytes.
    /// </summary>
    [Fact]
    public void FromSpan_ReadsCorrectly()
    {
        // Create binary data matching C++ struct layout (little-endian)
        byte[] data = new byte[16];
        BitConverter.TryWriteBytes(data.AsSpan(0, 8), 42UL);       // frame_number
        BitConverter.TryWriteBytes(data.AsSpan(8, 8), 1234567890UL); // timestamp_ns

        var metadata = FrameMetadata.FromSpan(data);

        Assert.Equal(42UL, metadata.FrameNumber);
        Assert.Equal(1234567890UL, metadata.TimestampNs);
    }

    /// <summary>
    /// Test that FromSpan throws for insufficient data.
    /// </summary>
    [Fact]
    public void FromSpan_ThrowsForShortData()
    {
        byte[] shortData = new byte[8]; // Only 8 bytes, need 16
        Assert.Throws<ArgumentException>(() => FrameMetadata.FromSpan(shortData));
    }

    /// <summary>
    /// Test HasTimestamp property when timestamp is available.
    /// </summary>
    [Fact]
    public void HasTimestamp_TrueWhenAvailable()
    {
        var metadata = new FrameMetadata(0, 1000000);
        Assert.True(metadata.HasTimestamp);
    }

    /// <summary>
    /// Test HasTimestamp property when timestamp is unavailable.
    /// </summary>
    [Fact]
    public void HasTimestamp_FalseWhenUnavailable()
    {
        var metadata = new FrameMetadata(0, FrameMetadata.TimestampUnavailable);
        Assert.False(metadata.HasTimestamp);
    }

    /// <summary>
    /// Test Timestamp property returns correct TimeSpan.
    /// </summary>
    [Fact]
    public void Timestamp_ReturnsCorrectTimeSpan()
    {
        // 1,000,000 ns = 1 ms
        var metadata = new FrameMetadata(0, 1_000_000);
        Assert.NotNull(metadata.Timestamp);
        Assert.Equal(TimeSpan.FromMilliseconds(1), metadata.Timestamp.Value);
    }

    /// <summary>
    /// Test Timestamp property returns null when unavailable.
    /// </summary>
    [Fact]
    public void Timestamp_ReturnsNullWhenUnavailable()
    {
        var metadata = new FrameMetadata(0, FrameMetadata.TimestampUnavailable);
        Assert.Null(metadata.Timestamp);
    }

    /// <summary>
    /// Cross-platform test: Verify byte layout matches C++ struct.
    /// C++ uses little-endian byte order on x86/x64/ARM.
    /// </summary>
    [Fact]
    public void CrossPlatform_ByteLayoutMatchesCpp()
    {
        // C++ struct layout (16 bytes, 8-byte aligned):
        //   [0-7]   frame_number    - uint64_t
        //   [8-15]  timestamp_ns    - uint64_t

        // Create data with known values at specific byte positions
        ulong frameNumber = 0x0102030405060708;
        ulong timestampNs = 0x1112131415161718;

        byte[] expectedBytes = new byte[16];
        // Little-endian: LSB first
        expectedBytes[0] = 0x08; expectedBytes[1] = 0x07; expectedBytes[2] = 0x06; expectedBytes[3] = 0x05;
        expectedBytes[4] = 0x04; expectedBytes[5] = 0x03; expectedBytes[6] = 0x02; expectedBytes[7] = 0x01;
        expectedBytes[8] = 0x18; expectedBytes[9] = 0x17; expectedBytes[10] = 0x16; expectedBytes[11] = 0x15;
        expectedBytes[12] = 0x14; expectedBytes[13] = 0x13; expectedBytes[14] = 0x12; expectedBytes[15] = 0x11;

        var metadata = FrameMetadata.FromSpan(expectedBytes);

        Assert.Equal(frameNumber, metadata.FrameNumber);
        Assert.Equal(timestampNs, metadata.TimestampNs);
    }

    /// <summary>
    /// Cross-platform test: Verify that C# writes the same bytes as expected by C++/Python.
    /// </summary>
    [Fact]
    public void CrossPlatform_WritesMatchExpectedBytes()
    {
        var metadata = new FrameMetadata(frameNumber: 1, timestampNs: 2);

        // Get the raw bytes from the struct
        byte[] actualBytes = new byte[16];
        MemoryMarshal.Write(actualBytes, in metadata);

        // Expected little-endian bytes
        byte[] expectedBytes = new byte[]
        {
            // frame_number = 1 (little-endian uint64)
            0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            // timestamp_ns = 2 (little-endian uint64)
            0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        Assert.Equal(expectedBytes, actualBytes);
    }

    /// <summary>
    /// Cross-platform test: Verify struct field offsets match C++ layout.
    /// </summary>
    [Fact]
    public void CrossPlatform_FieldOffsetsMatchCpp()
    {
        // frame_number at offset 0
        Assert.Equal(0, (int)Marshal.OffsetOf<FrameMetadata>(nameof(FrameMetadata.FrameNumber)));
        // timestamp_ns at offset 8
        Assert.Equal(8, (int)Marshal.OffsetOf<FrameMetadata>(nameof(FrameMetadata.TimestampNs)));
    }

    /// <summary>
    /// Cross-platform test: Verify struct size and field offsets match C++ (8-byte aligned).
    /// Note: StructLayoutAttribute.Pack may not be preserved via reflection in all .NET runtimes,
    /// so we verify alignment indirectly through size and offset checks.
    /// </summary>
    [Fact]
    public void CrossPlatform_AlignmentIs8Bytes()
    {
        // Verify the struct is exactly 16 bytes (no padding, two 8-byte fields)
        Assert.Equal(16, Marshal.SizeOf<FrameMetadata>());

        // Verify field offsets are 8-byte aligned (0 and 8)
        Assert.Equal(0, (int)Marshal.OffsetOf<FrameMetadata>(nameof(FrameMetadata.FrameNumber)));
        Assert.Equal(8, (int)Marshal.OffsetOf<FrameMetadata>(nameof(FrameMetadata.TimestampNs)));

        // Verify no wasted space - each ulong is 8 bytes, total should be 16
        Assert.Equal(2 * sizeof(ulong), Marshal.SizeOf<FrameMetadata>());
    }

    /// <summary>
    /// Test ToString format with available timestamp.
    /// </summary>
    [Fact]
    public void ToString_WithTimestamp_FormatsCorrectly()
    {
        var metadata = new FrameMetadata(42, 1_500_000_000); // 1500 ms
        var result = metadata.ToString();

        Assert.Contains("Frame 42", result);
        Assert.Contains("1500.000ms", result);
    }

    /// <summary>
    /// Test ToString format with unavailable timestamp.
    /// </summary>
    [Fact]
    public void ToString_WithoutTimestamp_ShowsNA()
    {
        var metadata = new FrameMetadata(0, FrameMetadata.TimestampUnavailable);
        var result = metadata.ToString();

        Assert.Contains("N/A", result);
    }
}
