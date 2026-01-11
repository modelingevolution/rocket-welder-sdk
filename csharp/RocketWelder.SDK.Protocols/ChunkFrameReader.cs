namespace RocketWelder.SDK.Protocols;

/// <summary>
/// Reads length-prefixed frames from an overlay block.
/// Format: [varint length][frame data]...
/// </summary>
public ref struct ChunkFrameReader
{
    private readonly ReadOnlyMemory<byte> _buffer;
    private int _offset;

    public ChunkFrameReader(byte[] data)
    {
        _buffer = data;
        _offset = 0;
    }

    public ReadOnlyMemory<byte> ReadFrame(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_offset >= _buffer.Length)
            return ReadOnlyMemory<byte>.Empty;

        var span = _buffer.Span;
        if (!Varint.TryRead(span[_offset..], out var length, out int varintBytes))
            return ReadOnlyMemory<byte>.Empty;

        var dataOffset = _offset + varintBytes;
        if (dataOffset + (int)length > _buffer.Length)
            return ReadOnlyMemory<byte>.Empty;

        var frame = _buffer.Slice(dataOffset, (int)length);
        _offset = dataOffset + (int)length;
        return frame;
    }
}
