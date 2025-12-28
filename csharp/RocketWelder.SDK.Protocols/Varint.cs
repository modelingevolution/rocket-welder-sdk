namespace RocketWelder.SDK.Protocols;

/// <summary>
/// Core varint and ZigZag encoding/decoding algorithms.
/// Single source of truth for all varint operations in the SDK.
/// Compatible with Protocol Buffers varint encoding.
///
/// Varint encoding uses 7 bits per byte with MSB as continuation flag.
/// ZigZag encoding maps signed integers to unsigned for efficient varint encoding
/// of values near zero (both positive and negative).
/// </summary>
public static class Varint
{
    #region ZigZag Encoding

    /// <summary>
    /// ZigZag encode a signed integer to unsigned.
    /// Maps negative numbers to odd positives: 0→0, -1→1, 1→2, -2→3, 2→4, etc.
    /// This allows efficient varint encoding of signed values near zero.
    /// </summary>
    public static uint ZigZagEncode(int value) => (uint)((value << 1) ^ (value >> 31));

    /// <summary>
    /// ZigZag decode an unsigned integer to signed.
    /// Reverses the ZigZag encoding: 0→0, 1→-1, 2→1, 3→-2, 4→2, etc.
    /// </summary>
    public static int ZigZagDecode(uint value) => (int)(value >> 1) ^ -((int)(value & 1));

    #endregion

    #region Size Calculation

    /// <summary>
    /// Calculate the number of bytes needed to encode a value as varint.
    /// </summary>
    public static int GetByteCount(uint value)
    {
        if (value < 0x80) return 1;
        if (value < 0x4000) return 2;
        if (value < 0x200000) return 3;
        if (value < 0x10000000) return 4;
        return 5;
    }

    /// <summary>
    /// Maximum bytes needed for a uint32 varint.
    /// </summary>
    public const int MaxBytesUInt32 = 5;

    #endregion

    #region Span-Based I/O (Zero-Allocation)

    /// <summary>
    /// Write a varint to a span. Returns the number of bytes written.
    /// </summary>
    /// <param name="buffer">Destination buffer (must have at least 5 bytes).</param>
    /// <param name="value">Value to encode.</param>
    /// <returns>Number of bytes written (1-5).</returns>
    public static int Write(Span<byte> buffer, uint value)
    {
        int i = 0;
        while (value >= 0x80)
        {
            buffer[i++] = (byte)(value | 0x80);
            value >>= 7;
        }
        buffer[i++] = (byte)value;
        return i;
    }

    /// <summary>
    /// Write a ZigZag-encoded signed integer as varint.
    /// </summary>
    /// <param name="buffer">Destination buffer (must have at least 5 bytes).</param>
    /// <param name="value">Signed value to encode.</param>
    /// <returns>Number of bytes written (1-5).</returns>
    public static int WriteZigZag(Span<byte> buffer, int value) => Write(buffer, ZigZagEncode(value));

    /// <summary>
    /// Read a varint from a span.
    /// </summary>
    /// <param name="buffer">Source buffer.</param>
    /// <param name="bytesRead">Number of bytes consumed.</param>
    /// <returns>Decoded unsigned value.</returns>
    /// <exception cref="InvalidDataException">Thrown if varint is malformed.</exception>
    public static uint Read(ReadOnlySpan<byte> buffer, out int bytesRead)
    {
        uint result = 0;
        int shift = 0;
        int i = 0;

        while (true)
        {
            if (i >= buffer.Length)
                throw new EndOfStreamException("Unexpected end of varint");

            byte b = buffer[i++];
            result |= (uint)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
                break;

            shift += 7;
            if (shift >= 35)
                throw new InvalidDataException("Varint too long (corrupted data)");
        }

        bytesRead = i;
        return result;
    }

    /// <summary>
    /// Read a ZigZag-encoded signed integer.
    /// </summary>
    /// <param name="buffer">Source buffer.</param>
    /// <param name="bytesRead">Number of bytes consumed.</param>
    /// <returns>Decoded signed value.</returns>
    public static int ReadZigZag(ReadOnlySpan<byte> buffer, out int bytesRead)
    {
        var encoded = Read(buffer, out bytesRead);
        return ZigZagDecode(encoded);
    }

    /// <summary>
    /// Try to read a varint, returning false if not enough data.
    /// </summary>
    /// <param name="buffer">Source buffer.</param>
    /// <param name="value">Decoded value if successful.</param>
    /// <param name="bytesRead">Number of bytes consumed if successful.</param>
    /// <returns>True if successful, false if not enough data.</returns>
    public static bool TryRead(ReadOnlySpan<byte> buffer, out uint value, out int bytesRead)
    {
        value = 0;
        bytesRead = 0;
        uint result = 0;
        int shift = 0;

        for (int i = 0; i < buffer.Length && i < 5; i++)
        {
            byte b = buffer[i];
            result |= (uint)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
            {
                value = result;
                bytesRead = i + 1;
                return true;
            }

            shift += 7;
        }

        return false;
    }

    #endregion
}

/// <summary>
/// Extension methods for ZigZag encoding on primitive types.
/// Provides convenient syntax: value.ZigZagEncode() and value.ZigZagDecode().
/// </summary>
public static class ZigZagExtensions
{
    /// <summary>
    /// ZigZag encode a signed integer to unsigned.
    /// </summary>
    public static uint ZigZagEncode(this int value) => Varint.ZigZagEncode(value);

    /// <summary>
    /// ZigZag decode an unsigned integer to signed.
    /// </summary>
    public static int ZigZagDecode(this uint value) => Varint.ZigZagDecode(value);
}
