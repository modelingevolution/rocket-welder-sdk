using System.Drawing;

namespace RocketWelder.SDK.Protocols;

/// <summary>
/// Static helpers for encoding and decoding segmentation protocol data.
/// Pure protocol logic with no transport or rendering dependencies.
/// WASM-compatible for cross-platform round-trip testing.
///
/// Frame Format:
/// [FrameId: 8 bytes, little-endian uint64]
/// [Width: varint]
/// [Height: varint]
/// [Instances...]
///
/// Instance Format:
/// [ClassId: 1 byte]
/// [InstanceId: 1 byte]
/// [PointCount: varint]
/// [Point0: X zigzag-varint, Y zigzag-varint]  (absolute)
/// [Point1+: deltaX zigzag-varint, deltaY zigzag-varint]
/// </summary>
public static class SegmentationProtocol
{
    /// <summary>
    /// Write a complete segmentation frame to a buffer.
    /// </summary>
    /// <returns>Number of bytes written.</returns>
    public static int Write(Span<byte> buffer, in SegmentationFrame frame)
    {
        var writer = new BinaryFrameWriter(buffer);

        // Write header
        writer.WriteUInt64LE(frame.FrameId);
        writer.WriteVarint(frame.Width);
        writer.WriteVarint(frame.Height);

        // Write instances
        foreach (var instance in frame.Instances)
        {
            WriteInstanceCore(ref writer, instance.ClassId, instance.InstanceId, instance.Points.Span);
        }

        return writer.Position;
    }

    /// <summary>
    /// Write just the frame header (frameId, width, height).
    /// </summary>
    /// <returns>Number of bytes written.</returns>
    public static int WriteHeader(Span<byte> buffer, ulong frameId, uint width, uint height)
    {
        var writer = new BinaryFrameWriter(buffer);
        writer.WriteUInt64LE(frameId);
        writer.WriteVarint(width);
        writer.WriteVarint(height);
        return writer.Position;
    }

    /// <summary>
    /// Write a single segmentation instance.
    /// Points are delta-encoded for compression.
    /// </summary>
    /// <returns>Number of bytes written.</returns>
    public static int WriteInstance(Span<byte> buffer, byte classId, byte instanceId, ReadOnlySpan<Point> points)
    {
        var writer = new BinaryFrameWriter(buffer);
        WriteInstanceCore(ref writer, classId, instanceId, points);
        return writer.Position;
    }

    private static void WriteInstanceCore(ref BinaryFrameWriter writer, byte classId, byte instanceId, ReadOnlySpan<Point> points)
    {
        writer.WriteByte(classId);
        writer.WriteByte(instanceId);
        writer.WriteVarint((uint)points.Length);

        int prevX = 0, prevY = 0;
        for (int i = 0; i < points.Length; i++)
        {
            int x = points[i].X;
            int y = points[i].Y;

            if (i == 0)
            {
                // First point is absolute (but still zigzag encoded)
                writer.WriteZigZagVarint(x);
                writer.WriteZigZagVarint(y);
            }
            else
            {
                // Subsequent points are deltas
                writer.WriteZigZagVarint(x - prevX);
                writer.WriteZigZagVarint(y - prevY);
            }

            prevX = x;
            prevY = y;
        }
    }

    /// <summary>
    /// Calculate the maximum buffer size needed for an instance.
    /// </summary>
    public static int CalculateInstanceSize(int pointCount)
    {
        // classId(1) + instanceId(1) + pointCount(varint, max 5) + points(max 10 bytes each: 2 zigzag varints)
        return 1 + 1 + 5 + (pointCount * 10);
    }

    /// <summary>
    /// Read a complete segmentation frame from a buffer.
    /// </summary>
    public static SegmentationFrame Read(ReadOnlySpan<byte> data)
    {
        var reader = new BinaryFrameReader(data);

        var frameId = reader.ReadUInt64LE();
        var width = reader.ReadVarint();
        var height = reader.ReadVarint();

        // First pass: count instances
        int instancesStart = reader.Position;
        int instanceCount = 0;
        while (reader.HasMore)
        {
            reader.ReadByte(); // classId
            reader.ReadByte(); // instanceId
            var pointCount = (int)reader.ReadVarint();
            for (int i = 0; i < pointCount; i++)
            {
                reader.ReadZigZagVarint(); // x
                reader.ReadZigZagVarint(); // y
            }
            instanceCount++;
        }

        // Second pass: allocate and fill
        reader.Position = instancesStart;
        var instances = new SegmentationInstance[instanceCount];
        int instanceIndex = 0;

        while (reader.HasMore)
        {
            var classId = reader.ReadByte();
            var instanceId = reader.ReadByte();
            var pointCount = (int)reader.ReadVarint();

            var points = new Point[pointCount];
            int prevX = 0, prevY = 0;

            for (int i = 0; i < pointCount; i++)
            {
                int x = reader.ReadZigZagVarint();
                int y = reader.ReadZigZagVarint();

                if (i > 0)
                {
                    // Delta decode
                    x += prevX;
                    y += prevY;
                }

                points[i] = new Point(x, y);
                prevX = x;
                prevY = y;
            }

            instances[instanceIndex++] = new SegmentationInstance(classId, instanceId, points);
        }

        return new SegmentationFrame(frameId, width, height, instances);
    }

    /// <summary>
    /// Try to read a segmentation frame, returning false if the data is invalid.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> data, out SegmentationFrame frame)
    {
        try
        {
            frame = Read(data);
            return true;
        }
        catch
        {
            frame = default;
            return false;
        }
    }
}
