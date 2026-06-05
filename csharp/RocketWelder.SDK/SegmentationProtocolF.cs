using System;
using System.Buffers;
using ModelingEvolution.Drawing;
using RocketWelder.SDK.Protocols;
using RocketWelder.SDK.Vision;

namespace RocketWelder.SDK;

/// <summary>
/// Float-precision segmentation protocol decoder.
/// Decodes binary frames directly into <see cref="Point{T}"/> (float) for
/// zero-copy <see cref="Polygon{T}"/> construction.
/// </summary>
public static class SegmentationProtocolF
{
    /// <summary>
    /// Decode a segmentation frame into a non-pooled <see cref="SegmentationFrameF"/>.
    /// Each instance's Points shares a single backing array.
    /// </summary>
    public static SegmentationFrameF Read(ReadOnlySpan<byte> data)
    {
        var reader = new BinaryFrameReader(data);

        var frameId = reader.ReadUInt64LE();
        var width = reader.ReadVarint();
        var height = reader.ReadVarint();

        // First pass: count instances and total points
        int instancesStart = reader.Position;
        int instanceCount = 0;
        int totalPoints = 0;

        while (reader.HasMore)
        {
            reader.ReadByte(); // classId
            reader.ReadByte(); // instanceId
            reader.ReadUInt16LE(); // confidence
            var pointCount = (int)reader.ReadVarint();
            totalPoints += pointCount;
            for (int i = 0; i < pointCount; i++)
            {
                reader.ReadZigZagVarint(); // x
                reader.ReadZigZagVarint(); // y
            }
            instanceCount++;
        }

        // Second pass: allocate single buffer and fill
        var pointsBuffer = new Point<float>[totalPoints];
        var instances = new SegmentationInstanceF[instanceCount];
        reader.Position = instancesStart;
        int pointOffset = 0;
        int instanceIndex = 0;

        while (reader.HasMore)
        {
            var classId = reader.ReadByte();
            var instanceId = reader.ReadByte();
            var confidenceRaw = reader.ReadUInt16LE();
            var confidence = (Confidence)confidenceRaw;
            var pointCount = (int)reader.ReadVarint();

            int instancePointStart = pointOffset;
            DecodePoints(ref reader, pointsBuffer, ref pointOffset, pointCount);

            instances[instanceIndex++] = new SegmentationInstanceF(
                classId, instanceId, confidence,
                pointsBuffer.AsMemory(instancePointStart, pointCount));
        }

        return new SegmentationFrameF(frameId, width, height, instances);
    }

    /// <summary>
    /// Decode a segmentation frame into a pooled <see cref="SegmentationFrameHandleF"/>.
    /// Zero allocation after ArrayPool warmup — the hot path for real-time decoding.
    /// The caller MUST dispose the returned handle to return buffers to the pool.
    /// </summary>
    public static SegmentationFrameHandleF ReadPooled(ReadOnlySpan<byte> data)
    {
        var reader = new BinaryFrameReader(data);

        var frameId = reader.ReadUInt64LE();
        var width = reader.ReadVarint();
        var height = reader.ReadVarint();

        // First pass: count instances and total points
        int instancesStart = reader.Position;
        int instanceCount = 0;
        int totalPoints = 0;

        while (reader.HasMore)
        {
            reader.ReadByte(); // classId
            reader.ReadByte(); // instanceId
            reader.ReadUInt16LE(); // confidence
            var pointCount = (int)reader.ReadVarint();
            totalPoints += pointCount;
            for (int i = 0; i < pointCount; i++)
            {
                reader.ReadZigZagVarint(); // x
                reader.ReadZigZagVarint(); // y
            }
            instanceCount++;
        }

        // Rent pooled buffers
        var pointsBuffer = ArrayPool<Point<float>>.Shared.Rent(Math.Max(totalPoints, 1));
        var instancesBuffer = ArrayPool<SegmentationInstanceF>.Shared.Rent(Math.Max(instanceCount, 1));

        try
        {
            // Second pass: decode into pooled buffers
            reader.Position = instancesStart;
            int pointOffset = 0;
            int instanceIndex = 0;

            while (reader.HasMore)
            {
                var classId = reader.ReadByte();
                var instanceId = reader.ReadByte();
                var confidenceRaw = reader.ReadUInt16LE();
                var confidence = (Confidence)confidenceRaw;
                var pointCount = (int)reader.ReadVarint();

                int instancePointStart = pointOffset;
                DecodePoints(ref reader, pointsBuffer, ref pointOffset, pointCount);

                instancesBuffer[instanceIndex++] = new SegmentationInstanceF(
                    classId, instanceId, confidence,
                    pointsBuffer.AsMemory(instancePointStart, pointCount));
            }

            return new SegmentationFrameHandleF(
                frameId, width, height,
                pointsBuffer, totalPoints,
                instancesBuffer, instanceCount);
        }
        catch
        {
            ArrayPool<Point<float>>.Shared.Return(pointsBuffer);
            ArrayPool<SegmentationInstanceF>.Shared.Return(instancesBuffer);
            throw;
        }
    }

    private static void DecodePoints(
        ref BinaryFrameReader reader,
        Point<float>[] buffer,
        ref int offset,
        int pointCount)
    {
        if (pointCount == 0) return;

        // First point (absolute, zigzag encoded)
        int x = reader.ReadZigZagVarint();
        int y = reader.ReadZigZagVarint();
        buffer[offset++] = new Point<float>(x, y);

        // Remaining points (delta encoded)
        for (int i = 1; i < pointCount; i++)
        {
            x += reader.ReadZigZagVarint();
            y += reader.ReadZigZagVarint();
            buffer[offset++] = new Point<float>(x, y);
        }
    }
}
