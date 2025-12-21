namespace RocketWelder.SDK.Protocols;

/// <summary>
/// Varint and ZigZag encoding extensions for efficient integer compression.
/// Compatible with Protocol Buffers varint encoding.
/// </summary>
public static class VarintExtensions
{
    /// <summary>
    /// Write a varint-encoded unsigned integer to a stream.
    /// </summary>
    public static void WriteVarint(this Stream stream, uint value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }

    /// <summary>
    /// Read a varint-encoded unsigned integer from a stream.
    /// </summary>
    public static uint ReadVarint(this Stream stream)
    {
        uint result = 0;
        int shift = 0;
        while (true)
        {
            int b = stream.ReadByte();
            if (b == -1)
                throw new EndOfStreamException("Unexpected end of stream while reading varint");
            if (shift >= 35)
                throw new InvalidDataException("Varint too long (corrupted stream)");
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
            shift += 7;
        }
    }

    /// <summary>
    /// ZigZag encode a signed integer to unsigned.
    /// Maps negative numbers to odd positives: 0→0, -1→1, 1→2, -2→3, 2→4, etc.
    /// This allows efficient varint encoding of signed values near zero.
    /// </summary>
    public static uint ZigZagEncode(this int value)
    {
        return (uint)((value << 1) ^ (value >> 31));
    }

    /// <summary>
    /// ZigZag decode an unsigned integer to signed.
    /// Reverses the ZigZag encoding: 0→0, 1→-1, 2→1, 3→-2, 4→2, etc.
    /// </summary>
    public static int ZigZagDecode(this uint value)
    {
        return (int)(value >> 1) ^ -((int)(value & 1));
    }

    /// <summary>
    /// Write a varint-encoded unsigned integer to a stream asynchronously.
    /// </summary>
    public static async Task WriteVarintAsync(this Stream stream, uint value, CancellationToken ct = default)
    {
        var buffer = new byte[5]; // Max 5 bytes for uint32 varint
        int index = 0;
        while (value >= 0x80)
        {
            buffer[index++] = (byte)(value | 0x80);
            value >>= 7;
        }
        buffer[index++] = (byte)value;
        await stream.WriteAsync(buffer.AsMemory(0, index), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Read a varint-encoded unsigned integer from a stream asynchronously.
    /// </summary>
    public static async Task<uint> ReadVarintAsync(this Stream stream, CancellationToken ct = default)
    {
        uint result = 0;
        int shift = 0;
        var buffer = new byte[1];
        while (true)
        {
            int bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (bytesRead == 0)
                throw new EndOfStreamException("Unexpected end of stream while reading varint");
            if (shift >= 35)
                throw new InvalidDataException("Varint too long (corrupted stream)");
            result |= (uint)(buffer[0] & 0x7F) << shift;
            if ((buffer[0] & 0x80) == 0)
                return result;
            shift += 7;
        }
    }
}
