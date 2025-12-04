using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Transport
{
    /// <summary>
    /// Frame sink that writes to a TCP connection with length-prefix framing.
    /// Each frame is prefixed with a 4-byte little-endian length header.
    /// </summary>
    /// <remarks>
    /// Frame format: [Length: 4 bytes LE][Frame Data: N bytes]
    /// </remarks>
    public class TcpFrameSink : IFrameSink
    {
        private readonly NetworkStream _stream;
        private readonly bool _leaveOpen;
        private bool _disposed;

        /// <summary>
        /// Creates a TCP frame sink from a NetworkStream.
        /// </summary>
        /// <param name="stream">NetworkStream to write to</param>
        /// <param name="leaveOpen">If true, doesn't dispose stream on disposal</param>
        public TcpFrameSink(NetworkStream stream, bool leaveOpen = false)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _leaveOpen = leaveOpen;
        }

        /// <summary>
        /// Creates a TCP frame sink from a TcpClient.
        /// </summary>
        public TcpFrameSink(TcpClient client, bool leaveOpen = false)
            : this(client?.GetStream() ?? throw new ArgumentNullException(nameof(client)), leaveOpen)
        {
        }

        public void WriteFrame(ReadOnlySpan<byte> frameData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpFrameSink));

            // Write 4-byte length prefix (little-endian)
            Span<byte> lengthPrefix = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(lengthPrefix, (uint)frameData.Length);
            _stream.Write(lengthPrefix);

            // Write frame data
            _stream.Write(frameData);
        }

        public async ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frameData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpFrameSink));

            // Write 4-byte length prefix (little-endian)
            byte[] lengthPrefix = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(lengthPrefix, (uint)frameData.Length);
            await _stream.WriteAsync(lengthPrefix, 0, 4);

            // Write frame data
            await _stream.WriteAsync(frameData);
        }

        public void Flush()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpFrameSink));

            _stream.Flush();
        }

        public async Task FlushAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpFrameSink));

            await _stream.FlushAsync();
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
