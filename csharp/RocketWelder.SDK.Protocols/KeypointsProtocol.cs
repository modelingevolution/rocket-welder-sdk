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
/// [KeypointCount: varint]
/// [Keypoints: Id(varint), X(int32 LE), Y(int32 LE), Confidence(uint16 LE)]
///
/// Delta Frame Format:
/// [FrameType: 1 byte (0x01=Delta)]
/// [FrameId: 8 bytes, little-endian uint64]
/// [KeypointCount: varint]
/// [Keypoints: Id(varint), DeltaX(zigzag), DeltaY(zigzag), DeltaConfidence(zigzag)]
/// </summary>
public static class KeypointsProtocol
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
    public static int WriteMasterFrame(Span<byte> buffer, ulong frameId, ReadOnlySpan<Keypoint> keypoints)
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
            writer.WriteUInt16LE(kp.Confidence);
        }

        return writer.Position;
    }

    /// <summary>
    /// Write a delta frame (keypoint positions relative to previous frame).
    /// </summary>
    /// <returns>Number of bytes written.</returns>
    public static int WriteDeltaFrame(Span<byte> buffer, ulong frameId,
        ReadOnlySpan<Keypoint> current, ReadOnlySpan<Keypoint> previous)
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
            writer.WriteZigZagVarint(curr.Confidence - prev.Confidence);
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
    public static KeypointsFrame Read(ReadOnlySpan<byte> data)
    {
        var reader = new BinaryFrameReader(data);

        var frameType = reader.ReadByte();
        bool isMaster = frameType == MasterFrameType;
        var frameId = reader.ReadUInt64LE();
        var count = (int)reader.ReadVarint();

        if (!isMaster)
        {
            throw new InvalidOperationException(
                "Cannot read delta frame without previous state. Use ReadWithPreviousState instead.");
        }

        var keypoints = new Keypoint[count];

        for (int i = 0; i < count; i++)
        {
            var id = (int)reader.ReadVarint();
            int x = reader.ReadInt32LE();
            int y = reader.ReadInt32LE();
            var confidence = reader.ReadUInt16LE();

            keypoints[i] = new Keypoint(id, x, y, confidence);
        }

        return new KeypointsFrame(frameId, isMaster, keypoints);
    }

    /// <summary>
    /// Read a keypoints frame with previous state for delta decoding.
    /// </summary>
    public static KeypointsFrame ReadWithPreviousState(ReadOnlySpan<byte> data, ReadOnlySpan<Keypoint> previous)
    {
        var reader = new BinaryFrameReader(data);

        var frameType = reader.ReadByte();
        bool isMaster = frameType == MasterFrameType;
        var frameId = reader.ReadUInt64LE();
        var count = (int)reader.ReadVarint();

        var keypoints = new Keypoint[count];

        // Build lookup for previous keypoints
        Dictionary<int, Keypoint>? prevDict = null;
        if (!isMaster)
        {
            prevDict = new Dictionary<int, Keypoint>(previous.Length);
            foreach (var p in previous)
                prevDict[p.Id] = p;
        }

        for (int i = 0; i < count; i++)
        {
            var id = (int)reader.ReadVarint();

            if (isMaster)
            {
                int x = reader.ReadInt32LE();
                int y = reader.ReadInt32LE();
                var confidence = reader.ReadUInt16LE();

                keypoints[i] = new Keypoint(id, x, y, confidence);
            }
            else
            {
                var deltaX = reader.ReadZigZagVarint();
                var deltaY = reader.ReadZigZagVarint();
                var deltaConf = reader.ReadZigZagVarint();

                if (!prevDict!.TryGetValue(id, out var prev))
                {
                    throw new InvalidOperationException($"No previous keypoint found for id {id}");
                }

                keypoints[i] = new Keypoint(
                    id,
                    prev.Position.X + deltaX,
                    prev.Position.Y + deltaY,
                    (ushort)(prev.Confidence + deltaConf)
                );
            }
        }

        return new KeypointsFrame(frameId, isMaster, keypoints);
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
