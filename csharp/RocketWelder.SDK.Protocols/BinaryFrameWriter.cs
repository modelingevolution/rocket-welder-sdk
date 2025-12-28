using System.Buffers.Binary;

namespace RocketWelder.SDK.Protocols;

/// <summary>
/// Zero-allocation binary writer for encoding streaming protocol data.
/// Symmetric counterpart to <see cref="BinaryFrameReader"/> for round-trip testing.
/// Designed for high-performance frame encoding in real-time video processing.
/// </summary>
public ref struct BinaryFrameWriter
{
    private readonly Span<byte> _buffer;
    private int _position;

    public BinaryFrameWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    /// <summary>
    /// Current write position in the buffer.
    /// </summary>
    public int Position => _position;

    /// <summary>
    /// Remaining bytes available to write.
    /// </summary>
    public int Remaining => _buffer.Length - _position;

    /// <summary>
    /// Returns the portion of the buffer that has been written to.
    /// </summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer[.._position];

    /// <summary>
    /// Write a single byte.
    /// </summary>
    public void WriteByte(byte value)
    {
        if (_position >= _buffer.Length)
            throw new InvalidOperationException("Buffer overflow: not enough space for byte");
        _buffer[_position++] = value;
    }

    /// <summary>
    /// Write an unsigned 64-bit integer (little-endian).
    /// </summary>
    public void WriteUInt64LE(ulong value)
    {
        if (_position + 8 > _buffer.Length)
            throw new InvalidOperationException("Buffer overflow: not enough space for UInt64");
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer.Slice(_position, 8), value);
        _position += 8;
    }

    /// <summary>
    /// Write a signed 32-bit integer (little-endian).
    /// </summary>
    public void WriteInt32LE(int value)
    {
        if (_position + 4 > _buffer.Length)
            throw new InvalidOperationException("Buffer overflow: not enough space for Int32");
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.Slice(_position, 4), value);
        _position += 4;
    }

    /// <summary>
    /// Write an unsigned 16-bit integer (little-endian).
    /// </summary>
    public void WriteUInt16LE(ushort value)
    {
        if (_position + 2 > _buffer.Length)
            throw new InvalidOperationException("Buffer overflow: not enough space for UInt16");
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.Slice(_position, 2), value);
        _position += 2;
    }

    /// <summary>
    /// Write a 32-bit floating point (little-endian).
    /// </summary>
    public void WriteSingleLE(float value)
    {
        if (_position + 4 > _buffer.Length)
            throw new InvalidOperationException("Buffer overflow: not enough space for Single");
        BinaryPrimitives.WriteSingleLittleEndian(_buffer.Slice(_position, 4), value);
        _position += 4;
    }

    /// <summary>
    /// Write a varint-encoded unsigned 32-bit integer.
    /// </summary>
    public void WriteVarint(uint value)
    {
        if (_position + Varint.MaxBytesUInt32 > _buffer.Length && _position + Varint.GetByteCount(value) > _buffer.Length)
            throw new InvalidOperationException("Buffer overflow: not enough space for varint");

        var remaining = _buffer[_position..];
        _position += Varint.Write(remaining, value);
    }

    /// <summary>
    /// Write a ZigZag-encoded signed integer (varint format).
    /// </summary>
    public void WriteZigZagVarint(int value)
    {
        if (_position + Varint.MaxBytesUInt32 > _buffer.Length)
            throw new InvalidOperationException("Buffer overflow: not enough space for varint");

        var remaining = _buffer[_position..];
        _position += Varint.WriteZigZag(remaining, value);
    }

    /// <summary>
    /// Write raw bytes from a span.
    /// </summary>
    public void WriteBytes(ReadOnlySpan<byte> source)
    {
        if (_position + source.Length > _buffer.Length)
            throw new InvalidOperationException($"Buffer overflow: not enough space for {source.Length} bytes");
        source.CopyTo(_buffer.Slice(_position, source.Length));
        _position += source.Length;
    }
}
