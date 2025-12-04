using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Transport
{
    /// <summary>
    /// Frame source that reads from a TCP connection with length-prefix framing.
    /// Each frame is prefixed with a 4-byte little-endian length header.
    /// </summary>
    /// <remarks>
    /// Frame format: [Length: 4 bytes LE][Frame Data: N bytes]
    /// </remarks>
    public class TcpFrameSource : IFrameSource
    {
        private readonly NetworkStream _stream;
        private readonly bool _leaveOpen;
        private bool _disposed;
        private bool _endOfStream;

        /// <summary>
        /// Creates a TCP frame source from a NetworkStream.
        /// </summary>
        /// <param name="stream">NetworkStream to read from</param>
        /// <param name="leaveOpen">If true, doesn't dispose stream on disposal</param>
        public TcpFrameSource(NetworkStream stream, bool leaveOpen = false)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _leaveOpen = leaveOpen;
        }

        /// <summary>
        /// Creates a TCP frame source from a TcpClient.
        /// </summary>
        public TcpFrameSource(TcpClient client, bool leaveOpen = false)
            : this(client?.GetStream() ?? throw new ArgumentNullException(nameof(client)), leaveOpen)
        {
        }

        public bool HasMoreFrames => !_endOfStream && _stream.CanRead;

        public ReadOnlyMemory<byte> ReadFrame(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpFrameSource));

            if (_endOfStream)
                return ReadOnlyMemory<byte>.Empty;

            // Read 4-byte length prefix
            Span<byte> lengthPrefix = stackalloc byte[4];
            int bytesRead = ReadExactly(_stream, lengthPrefix);

            if (bytesRead == 0)
            {
                _endOfStream = true;
                return ReadOnlyMemory<byte>.Empty;
            }

            if (bytesRead < 4)
                throw new EndOfStreamException("Incomplete frame length prefix");

            uint frameLength = BinaryPrimitives.ReadUInt32LittleEndian(lengthPrefix);

            if (frameLength == 0)
                return ReadOnlyMemory<byte>.Empty;

            if (frameLength > 100 * 1024 * 1024) // 100 MB sanity check
                throw new InvalidDataException($"Frame length {frameLength} exceeds maximum");

            // Read frame data
            byte[] frameData = new byte[frameLength];
            bytesRead = ReadExactly(_stream, frameData);

            if (bytesRead < frameLength)
                throw new EndOfStreamException($"Incomplete frame data: expected {frameLength}, got {bytesRead}");

            return frameData;
        }

        public async ValueTask<ReadOnlyMemory<byte>> ReadFrameAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpFrameSource));

            if (_endOfStream)
                return ReadOnlyMemory<byte>.Empty;

            // Read 4-byte length prefix
            byte[] lengthPrefix = new byte[4];
            int bytesRead = await ReadExactlyAsync(_stream, lengthPrefix, cancellationToken);

            if (bytesRead == 0)
            {
                _endOfStream = true;
                return ReadOnlyMemory<byte>.Empty;
            }

            if (bytesRead < 4)
                throw new EndOfStreamException("Incomplete frame length prefix");

            uint frameLength = BinaryPrimitives.ReadUInt32LittleEndian(lengthPrefix);

            if (frameLength == 0)
                return ReadOnlyMemory<byte>.Empty;

            if (frameLength > 100 * 1024 * 1024) // 100 MB sanity check
                throw new InvalidDataException($"Frame length {frameLength} exceeds maximum");

            // Read frame data
            byte[] frameData = new byte[frameLength];
            bytesRead = await ReadExactlyAsync(_stream, frameData, cancellationToken);

            if (bytesRead < frameLength)
                throw new EndOfStreamException($"Incomplete frame data: expected {frameLength}, got {bytesRead}");

            return frameData;
        }

        private static int ReadExactly(Stream stream, Span<byte> buffer)
        {
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int bytesRead = stream.Read(buffer.Slice(totalRead));
                if (bytesRead == 0)
                    break;
                totalRead += bytesRead;
            }
            return totalRead;
        }

        private static async ValueTask<int> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int bytesRead = await stream.ReadAsync(buffer, totalRead, buffer.Length - totalRead, cancellationToken);
                if (bytesRead == 0)
                    break;
                totalRead += bytesRead;
            }
            return totalRead;
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
