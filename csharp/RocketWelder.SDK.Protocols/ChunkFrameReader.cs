namespace RocketWelder.SDK.Protocols;

/// <summary>
/// Reads length-prefixed frames from an overlay block.
/// Format: [varint length][frame data]...
/// </summary>
public ref struct ChunkFrameReader
{
    private readonly byte[] _buffer;
    private readonly int _end;
    private int _offset;

    public ChunkFrameReader(byte[] data, int offset, int length)
    {
        _buffer = data;
        _offset = offset;
        _end = offset + length;
    }

    public ReadOnlyMemory<byte> ReadFrame(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_offset >= _end)
            return ReadOnlyMemory<byte>.Empty;

        if (!Varint.TryRead(_buffer.AsSpan(_offset, _end - _offset), out var length, out int varintBytes))
            return ReadOnlyMemory<byte>.Empty;

        var dataOffset = _offset + varintBytes;
        if (dataOffset + (int)length > _end)
            return ReadOnlyMemory<byte>.Empty;

        var frame = _buffer.AsMemory(dataOffset, (int)length);
        _offset = dataOffset + (int)length;
        return frame;
    }
}
