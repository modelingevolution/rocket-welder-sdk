using System;
using System.IO;
using System.Threading.Tasks;
using RocketWelder.SDK.Protocols;

namespace RocketWelder.SDK.Transport
{
    /// <summary>
    /// Frame sink that writes to a Stream (file, memory, etc.).
    /// Each frame is prefixed with its length (varint encoding) for frame boundary detection.
    /// Format: [varint length][frame data]
    /// </summary>
    public class StreamFrameSink : IFrameSink
    {
        private readonly Stream _stream;
        private readonly bool _leaveOpen;
        private bool _disposed;

        /// <summary>
        /// Creates a stream-based frame sink.
        /// </summary>
        /// <param name="stream">Underlying stream to write to</param>
        /// <param name="leaveOpen">If true, doesn't dispose stream on disposal</param>
        public StreamFrameSink(Stream stream, bool leaveOpen = false)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _leaveOpen = leaveOpen;
        }

        public void WriteFrame(ReadOnlySpan<byte> frameData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StreamFrameSink));

            // Write frame length as varint
            _stream.WriteVarint((uint)frameData.Length);

            // Write frame data
            _stream.Write(frameData);
        }

        public async ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frameData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StreamFrameSink));

            // Write frame length as varint
            _stream.WriteVarint((uint)frameData.Length);

            // Write frame data
            await _stream.WriteAsync(frameData).ConfigureAwait(false);
        }

        public void Flush()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StreamFrameSink));

            _stream.Flush();
        }

        public async Task FlushAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StreamFrameSink));

            await _stream.FlushAsync().ConfigureAwait(false);
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
                await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
