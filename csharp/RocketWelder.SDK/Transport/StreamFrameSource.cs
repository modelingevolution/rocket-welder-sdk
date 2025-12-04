using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Transport
{
    /// <summary>
    /// Frame source that reads from a Stream (file, memory, etc.).
    /// Reads frames prefixed with varint length for frame boundary detection.
    /// Format: [varint length][frame data]
    /// </summary>
    public class StreamFrameSource : IFrameSource
    {
        private readonly Stream _stream;
        private readonly bool _leaveOpen;
        private bool _disposed;

        /// <summary>
        /// Creates a stream-based frame source.
        /// </summary>
        /// <param name="stream">Underlying stream to read from</param>
        /// <param name="leaveOpen">If true, doesn't dispose stream on disposal</param>
        public StreamFrameSource(Stream stream, bool leaveOpen = false)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _leaveOpen = leaveOpen;
        }

        public bool HasMoreFrames => _stream.Position < _stream.Length;

        public ReadOnlyMemory<byte> ReadFrame(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StreamFrameSource));

            // Check if stream has data
            if (_stream.Position >= _stream.Length)
                return ReadOnlyMemory<byte>.Empty;

            // Read frame length (varint)
            uint frameLength;
            try
            {
                frameLength = _stream.ReadVarint();
            }
            catch (EndOfStreamException)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            if (frameLength == 0)
                return ReadOnlyMemory<byte>.Empty;

            // Read frame data
            var buffer = new byte[frameLength];
            int totalRead = 0;
            while (totalRead < frameLength)
            {
                int bytesRead = _stream.Read(buffer, totalRead, (int)frameLength - totalRead);
                if (bytesRead == 0)
                    throw new EndOfStreamException($"Unexpected end of stream while reading frame. Expected {frameLength} bytes, got {totalRead}");
                totalRead += bytesRead;
            }

            return buffer;
        }

        public async ValueTask<ReadOnlyMemory<byte>> ReadFrameAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StreamFrameSource));

            // Check if stream has data
            if (_stream.Position >= _stream.Length)
                return ReadOnlyMemory<byte>.Empty;

            // Read frame length (varint)
            uint frameLength;
            try
            {
                frameLength = _stream.ReadVarint();
            }
            catch (EndOfStreamException)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            if (frameLength == 0)
                return ReadOnlyMemory<byte>.Empty;

            // Read frame data
            var buffer = new byte[frameLength];
            int totalRead = 0;
            while (totalRead < frameLength)
            {
                int bytesRead = await _stream.ReadAsync(buffer, totalRead, (int)frameLength - totalRead, cancellationToken);
                if (bytesRead == 0)
                    throw new EndOfStreamException($"Unexpected end of stream while reading frame. Expected {frameLength} bytes, got {totalRead}");
                totalRead += bytesRead;
            }

            return buffer;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (!_leaveOpen)
                _stream.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            if (!_leaveOpen)
                await _stream.DisposeAsync();
        }
    }
}
