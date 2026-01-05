using System.Drawing;

namespace RocketWelder.SDK.Protocols;

/// <summary>
/// Static helpers for encoding and decoding keypoints protocol data.
/// Pure protocol logic with no transport or rendering dependencies.
/// WASM-compatible for cross-platform round-trip testing.
///
/// Master Frame Format:
/// [FrameType: 1 byte (0x00=Master)]
/// [FrameId: 8 bytes, little-endian uint64]
/// [KeyPointCount: varint]
/// [KeyPoints: Id(varint), X(int32 LE), Y(int32 LE), Confidence(uint16 LE)]
///
/// Delta Frame Format:
/// [FrameType: 1 byte (0x01=Delta)]
/// [FrameId: 8 bytes, little-endian uint64]
/// [KeyPointCount: varint]
/// [KeyPoints: Id(varint), DeltaX(zigzag), DeltaY(zigzag), DeltaConfidence(zigzag)]
/// </summary>
public static class KeyPointsProtocol
{
    /// <summary>
    /// Frame type byte for master frames (absolute positions).
    /// </summary>
    public const byte MasterFrameType = 0x00;

    /// <summary>
    /// Frame type byte for delta frames (relative positions).
    /// </summary>
    public const byte DeltaFrameType = 0x01;

    /// <summary>
    /// Write a master frame (absolute keypoint positions).
    /// </summary>
    /// <returns>Number of bytes written.</returns>
    public static int WriteMasterFrame(Span<byte> buffer, ulong frameId, ReadOnlySpan<KeyPoint> keypoints)
    {
        var writer = new BinaryFrameWriter(buffer);

        writer.WriteByte(MasterFrameType);
        writer.WriteUInt64LE(frameId);
        writer.WriteVarint((uint)keypoints.Length);

        foreach (var kp in keypoints)
        {
            writer.WriteVarint((uint)kp.Id);
            writer.WriteInt32LE(kp.Position.X);
            writer.WriteInt32LE(kp.Position.Y);
            writer.WriteUInt16LE((ushort)kp.Confidence);
        }

        return writer.Position;
    }

    /// <summary>
    /// Write a delta frame (keypoint positions relative to previous frame).
    /// Assumes keypoints are in matching order (current[i].Id == previous[i].Id).
    /// For variable keypoint counts, use the overload with previousLookup dictionary.
    /// </summary>
    /// <returns>Number of bytes written.</returns>
    public static int WriteDeltaFrame(Span<byte> buffer, ulong frameId,
        ReadOnlySpan<KeyPoint> current, ReadOnlySpan<KeyPoint> previous)
    {
        var writer = new BinaryFrameWriter(buffer);

        writer.WriteByte(DeltaFrameType);
        writer.WriteUInt64LE(frameId);
        writer.WriteVarint((uint)current.Length);

        for (int i = 0; i < current.Length; i++)
        {
            var curr = current[i];
            var prev = previous[i];

            writer.WriteVarint((uint)curr.Id);
            writer.WriteZigZagVarint(curr.Position.X - prev.Position.X);
            writer.WriteZigZagVarint(curr.Position.Y - prev.Position.Y);
            writer.WriteZigZagVarint((ushort)curr.Confidence - (ushort)prev.Confidence);
        }

        return writer.Position;
    }

    /// <summary>
    /// Write a delta frame with variable keypoint counts.
    /// KeyPoints are matched by ID using the previousLookup dictionary.
    /// New keypoints (not in previous) are written as absolute values (zigzag encoded).
    /// </summary>
    /// <returns>Number of bytes written.</returns>
    public static int WriteDeltaFrame(Span<byte> buffer, ulong frameId,
        ReadOnlySpan<KeyPoint> current, IReadOnlyDictionary<int, KeyPoint> previousLookup)
    {
        var writer = new BinaryFrameWriter(buffer);

        writer.WriteByte(DeltaFrameType);
        writer.WriteUInt64LE(frameId);
        writer.WriteVarint((uint)current.Length);

        foreach (var curr in current)
        {
            writer.WriteVarint((uint)curr.Id);

            if (previousLookup.TryGetValue(curr.Id, out var prev))
            {
                // Existing keypoint - write delta
                writer.WriteZigZagVarint(curr.Position.X - prev.Position.X);
                writer.WriteZigZagVarint(curr.Position.Y - prev.Position.Y);
                writer.WriteZigZagVarint((ushort)curr.Confidence - (ushort)prev.Confidence);
            }
            else
            {
                // New keypoint - write absolute value as zigzag (as if previous was 0)
                writer.WriteZigZagVarint(curr.Position.X);
                writer.WriteZigZagVarint(curr.Position.Y);
                writer.WriteZigZagVarint((ushort)curr.Confidence);
            }
        }

        return writer.Position;
    }

    /// <summary>
    /// Determine if a master frame should be written based on frame interval.
    /// </summary>
    public static bool ShouldWriteMasterFrame(ulong frameId, int masterInterval)
    {
        return frameId == 0 || (frameId % (ulong)masterInterval) == 0;
    }

    /// <summary>
    /// Read a keypoints frame (master frame only, no previous state needed).
    /// For delta frames, use ReadWithPreviousState.
    /// </summary>
    public static DeltaFrame<KeyPoint> Read(ReadOnlySpan<byte> data)
    {
        var reader = new BinaryFrameReader(data);

        var frameType = reader.ReadByte();
        bool isDelta = frameType == DeltaFrameType;
        var frameId = reader.ReadUInt64LE();
        var count = (int)reader.ReadVarint();

        if (isDelta)
        {
            throw new InvalidOperationException(
                "Cannot read delta frame without previous state. Use ReadWithPreviousState instead.");
        }

        var keypoints = new KeyPoint[count];

        for (int i = 0; i < count; i++)
        {
            var id = (int)reader.ReadVarint();
            int x = reader.ReadInt32LE();
            int y = reader.ReadInt32LE();
            var confidence = reader.ReadUInt16LE();

            keypoints[i] = new KeyPoint(id, x, y, confidence);
        }

        return new DeltaFrame<KeyPoint>(frameId, isDelta, keypoints);
    }

    /// <summary>
    /// Read a keypoints frame with previous state for delta decoding.
    /// </summary>
    /// <param name="data">The binary data to read.</param>
    /// <param name="previous">Previous frame keypoints for delta decoding.</param>
    /// <param name="reuseDict">Optional dictionary to reuse for lookups (reduces allocations in streaming scenarios).</param>
    public static DeltaFrame<KeyPoint> ReadWithPreviousState(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<KeyPoint> previous,
        Dictionary<int, KeyPoint>? reuseDict = null)
    {
        var reader = new BinaryFrameReader(data);

        var frameType = reader.ReadByte();
        bool isDelta = frameType == DeltaFrameType;
        var frameId = reader.ReadUInt64LE();
        var count = (int)reader.ReadVarint();

        var keypoints = new KeyPoint[count];

        // Build lookup for previous keypoints (reuse dictionary if provided)
        Dictionary<int, KeyPoint>? prevDict = null;
        if (isDelta)
        {
            prevDict = reuseDict ?? new Dictionary<int, KeyPoint>(previous.Length);
            prevDict.Clear();
            foreach (var p in previous)
                prevDict[p.Id] = p;
        }

        for (int i = 0; i < count; i++)
        {
            var id = (int)reader.ReadVarint();

            if (!isDelta)
            {
                int x = reader.ReadInt32LE();
                int y = reader.ReadInt32LE();
                var confidence = reader.ReadUInt16LE();

                keypoints[i] = new KeyPoint(id, x, y, confidence);
            }
            else
            {
                var deltaX = reader.ReadZigZagVarint();
                var deltaY = reader.ReadZigZagVarint();
                var deltaConf = reader.ReadZigZagVarint();

                if (prevDict!.TryGetValue(id, out var prev))
                {
                    // Existing keypoint - apply delta
                    keypoints[i] = new KeyPoint(
                        id,
                        prev.Position.X + deltaX,
                        prev.Position.Y + deltaY,
                        (ushort)Math.Clamp((ushort)prev.Confidence + deltaConf, 0, ushort.MaxValue)
                    );
                }
                else
                {
                    // New keypoint - delta values are actually absolute
                    keypoints[i] = new KeyPoint(
                        id,
                        deltaX,
                        deltaY,
                        (ushort)Math.Clamp(deltaConf, 0, ushort.MaxValue)
                    );
                }
            }
        }

        return new DeltaFrame<KeyPoint>(frameId, isDelta, keypoints);
    }

    /// <summary>
    /// Read a keypoints frame with previous state for delta decoding.
    /// More efficient for streaming scenarios where previous frame is already a dictionary.
    /// </summary>
    /// <param name="data">The binary data to read.</param>
    /// <param name="previousLookup">Previous frame keypoints dictionary for delta decoding. Pass null for master frames.</param>
    public static DeltaFrame<KeyPoint> ReadWithPreviousState(
        ReadOnlySpan<byte> data,
        IReadOnlyDictionary<int, KeyPoint>? previousLookup)
    {
        var reader = new BinaryFrameReader(data);

        var frameType = reader.ReadByte();
        bool isDelta = frameType == DeltaFrameType;
        var frameId = reader.ReadUInt64LE();
        var count = (int)reader.ReadVarint();

        var keypoints = new KeyPoint[count];

        for (int i = 0; i < count; i++)
        {
            var id = (int)reader.ReadVarint();

            if (!isDelta)
            {
                int x = reader.ReadInt32LE();
                int y = reader.ReadInt32LE();
                var confidence = reader.ReadUInt16LE();

                keypoints[i] = new KeyPoint(id, x, y, confidence);
            }
            else
            {
                var deltaX = reader.ReadZigZagVarint();
                var deltaY = reader.ReadZigZagVarint();
                var deltaConf = reader.ReadZigZagVarint();

                if (previousLookup != null && previousLookup.TryGetValue(id, out var prev))
                {
                    // Existing keypoint - apply delta
                    keypoints[i] = new KeyPoint(
                        id,
                        prev.Position.X + deltaX,
                        prev.Position.Y + deltaY,
                        (ushort)Math.Clamp((ushort)prev.Confidence + deltaConf, 0, ushort.MaxValue)
                    );
                }
                else
                {
                    // New keypoint - delta values are actually absolute
                    keypoints[i] = new KeyPoint(
                        id,
                        deltaX,
                        deltaY,
                        (ushort)Math.Clamp(deltaConf, 0, ushort.MaxValue)
                    );
                }
            }
        }

        return new DeltaFrame<KeyPoint>(frameId, isDelta, keypoints);
    }

    /// <summary>
    /// Try to read the frame header to determine if it's a master or delta frame.
    /// </summary>
    public static bool IsMasterFrame(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
            return false;
        return data[0] == MasterFrameType;
    }

    /// <summary>
    /// Calculate the maximum buffer size needed for a master frame.
    /// </summary>
    public static int CalculateMasterFrameSize(int keypointCount)
    {
        // type(1) + frameId(8) + count(varint, max 5) + keypoints(max 15 bytes each)
        return 1 + 8 + 5 + (keypointCount * 15);
    }

    /// <summary>
    /// Calculate the maximum buffer size needed for a delta frame.
    /// </summary>
    public static int CalculateDeltaFrameSize(int keypointCount)
    {
        // type(1) + frameId(8) + count(varint, max 5) + keypoints(max 20 bytes each: id + 3 zigzag varints)
        return 1 + 8 + 5 + (keypointCount * 20);
    }
}
