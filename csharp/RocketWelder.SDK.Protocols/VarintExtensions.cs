namespace RocketWelder.SDK.Protocols;

/// <summary>
/// Stream-based varint extension methods.
/// Uses the core <see cref="Varint"/> algorithms for encoding/decoding.
/// </summary>
public static class VarintStreamExtensions
{
    /// <summary>
    /// Write a varint-encoded unsigned integer to a stream.
    /// </summary>
    public static void WriteVarint(this Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[Varint.MaxBytesUInt32];
        int written = Varint.Write(buffer, value);
        stream.Write(buffer[..written]);
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
    /// Write a varint-encoded unsigned integer to a stream asynchronously.
    /// </summary>
    public static async Task WriteVarintAsync(this Stream stream, uint value, CancellationToken ct = default)
    {
        var buffer = new byte[Varint.MaxBytesUInt32];
        int written = Varint.Write(buffer, value);
        await stream.WriteAsync(buffer.AsMemory(0, written), ct).ConfigureAwait(false);
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
