using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Transport
{
    /// <summary>
    /// Frame source that reads from a Unix Domain Socket connection with length-prefix framing.
    /// Each frame is prefixed with a 4-byte little-endian length header.
    /// </summary>
    /// <remarks>
    /// Frame format: [Length: 4 bytes LE][Frame Data: N bytes]
    /// Unix Domain Sockets provide high-performance IPC on Linux/macOS.
    /// </remarks>
    public class UnixSocketFrameSource : IFrameSource
    {
        private readonly NetworkStream _stream;
        private readonly Socket? _socket;
        private readonly bool _leaveOpen;
        private bool _disposed;
        private bool _endOfStream;

        /// <summary>
        /// Creates a Unix socket frame source from a NetworkStream.
        /// </summary>
        /// <param name="stream">NetworkStream from Unix socket</param>
        /// <param name="leaveOpen">If true, doesn't dispose stream on disposal</param>
        public UnixSocketFrameSource(NetworkStream stream, bool leaveOpen = false)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _leaveOpen = leaveOpen;
        }

        /// <summary>
        /// Creates a Unix socket frame source from a connected Socket.
        /// </summary>
        /// <param name="socket">Connected Unix domain socket</param>
        /// <param name="leaveOpen">If true, doesn't close socket on disposal</param>
        public UnixSocketFrameSource(Socket socket, bool leaveOpen = false)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));

            if (socket.AddressFamily != AddressFamily.Unix)
                throw new ArgumentException("Socket must be a Unix domain socket", nameof(socket));

            _stream = new NetworkStream(socket, ownsSocket: false);
            _leaveOpen = leaveOpen;
        }

        /// <summary>
        /// Connects to a Unix socket path and creates a frame source.
        /// </summary>
        /// <param name="socketPath">Path to Unix socket file</param>
        /// <returns>Connected frame source</returns>
        public static UnixSocketFrameSource Connect(string socketPath)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));
            return new UnixSocketFrameSource(socket, leaveOpen: false);
        }

        /// <summary>
        /// Connects to a Unix socket path asynchronously and creates a frame source.
        /// </summary>
        /// <param name="socketPath">Path to Unix socket file</param>
        /// <returns>Connected frame source</returns>
        public static async Task<UnixSocketFrameSource> ConnectAsync(string socketPath)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath));
            return new UnixSocketFrameSource(socket, leaveOpen: false);
        }

        /// <summary>
        /// Connects to a Unix socket path asynchronously with timeout and optional retry.
        /// </summary>
        /// <param name="socketPath">Path to Unix socket file</param>
        /// <param name="timeout">Maximum time to wait for connection</param>
        /// <param name="retry">If true, retries connection until timeout; if false, fails immediately on error</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Connected frame source</returns>
        /// <exception cref="TimeoutException">Thrown when connection cannot be established within timeout</exception>
        public static async Task<UnixSocketFrameSource> ConnectAsync(
            string socketPath,
            TimeSpan timeout,
            bool retry = true,
            CancellationToken cancellationToken = default)
        {
            var deadline = DateTime.UtcNow + timeout;
            var retryDelay = TimeSpan.FromMilliseconds(100);
            SocketException? lastException = null;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
                        return new UnixSocketFrameSource(socket, leaveOpen: false);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
                catch (SocketException ex) when (retry)
                {
                    lastException = ex;
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                        break;

                    var delay = remaining < retryDelay ? remaining : retryDelay;
                    await Task.Delay(delay, cancellationToken);
                }
            }

            throw new TimeoutException(
                $"Could not connect to Unix socket '{socketPath}' within {timeout.TotalSeconds:F1}s",
                lastException);
        }

        public bool HasMoreFrames => !_endOfStream && _stream.CanRead;

        public ReadOnlyMemory<byte> ReadFrame(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UnixSocketFrameSource));

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
                throw new ObjectDisposedException(nameof(UnixSocketFrameSource));

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
            {
                _stream.Dispose();
                _socket?.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            if (!_leaveOpen)
            {
                await _stream.DisposeAsync();
                _socket?.Dispose();
            }
        }
    }
}
