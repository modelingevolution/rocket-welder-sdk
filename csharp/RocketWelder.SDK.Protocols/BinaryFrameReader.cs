using System.Buffers.Binary;
using System.Text;

namespace RocketWelder.SDK.Protocols;

/// <summary>
/// Zero-allocation binary reader for parsing streaming protocol data.
/// Designed for high-performance frame decoding in real-time video processing.
/// </summary>
public ref struct BinaryFrameReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public BinaryFrameReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    /// <summary>
    /// Returns true if there is more data to read.
    /// </summary>
    public bool HasMore => _position < _data.Length;

    /// <summary>
    /// Current read position in the buffer.
    /// </summary>
    public int Position => _position;

    /// <summary>
    /// Remaining bytes available to read.
    /// </summary>
    public int Remaining => _data.Length - _position;

    /// <summary>
    /// Read a single byte.
    /// </summary>
    public byte ReadByte()
    {
        if (_position >= _data.Length)
            throw new EndOfStreamException("Unexpected end of data");
        return _data[_position++];
    }

    /// <summary>
    /// Read an unsigned 64-bit integer (little-endian).
    /// </summary>
    public ulong ReadUInt64LE()
    {
        if (_position + 8 > _data.Length)
            throw new EndOfStreamException("Not enough data for UInt64");
        var value = BinaryPrimitives.ReadUInt64LittleEndian(_data.Slice(_position, 8));
        _position += 8;
        return value;
    }

    /// <summary>
    /// Read a signed 32-bit integer (little-endian).
    /// </summary>
    public int ReadInt32LE()
    {
        if (_position + 4 > _data.Length)
            throw new EndOfStreamException("Not enough data for Int32");
        var value = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(_position, 4));
        _position += 4;
        return value;
    }

    /// <summary>
    /// Read an unsigned 16-bit integer (little-endian).
    /// </summary>
    public ushort ReadUInt16LE()
    {
        if (_position + 2 > _data.Length)
            throw new EndOfStreamException("Not enough data for UInt16");
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(_position, 2));
        _position += 2;
        return value;
    }

    /// <summary>
    /// Read a 32-bit floating point (little-endian).
    /// </summary>
    public float ReadSingleLE()
    {
        if (_position + 4 > _data.Length)
            throw new EndOfStreamException("Not enough data for Single");
        var value = BinaryPrimitives.ReadSingleLittleEndian(_data.Slice(_position, 4));
        _position += 4;
        return value;
    }

    /// <summary>
    /// Read a varint-encoded unsigned 32-bit integer.
    /// </summary>
    public uint ReadVarint()
    {
        uint result = 0;
        int shift = 0;

        while (true)
        {
            if (_position >= _data.Length)
                throw new EndOfStreamException("Unexpected end of varint");

            byte b = _data[_position++];
            result |= (uint)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
                break;

            shift += 7;
            if (shift >= 35)
                throw new InvalidDataException("Varint too long");
        }

        return result;
    }

    /// <summary>
    /// Read a ZigZag-encoded signed integer (varint format).
    /// </summary>
    public int ReadZigZagVarint()
    {
        uint encoded = ReadVarint();
        return encoded.ZigZagDecode();
    }

    /// <summary>
    /// Read a UTF-8 encoded string of specified length.
    /// </summary>
    public string ReadString(int length)
    {
        if (_position + length > _data.Length)
            throw new EndOfStreamException($"Not enough data for string of length {length}");

        var bytes = _data.Slice(_position, length);
        _position += length;
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Skip a specified number of bytes.
    /// </summary>
    public void Skip(int count)
    {
        if (_position + count > _data.Length)
            throw new EndOfStreamException($"Cannot skip {count} bytes, only {Remaining} remaining");
        _position += count;
    }

    /// <summary>
    /// Read raw bytes into a span.
    /// </summary>
    public void ReadBytes(Span<byte> destination)
    {
        if (_position + destination.Length > _data.Length)
            throw new EndOfStreamException($"Not enough data for {destination.Length} bytes");
        _data.Slice(_position, destination.Length).CopyTo(destination);
        _position += destination.Length;
    }
}
