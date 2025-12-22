using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Transport
{
    /// <summary>
    /// Frame sink that writes to a Unix Domain Socket connection with length-prefix framing.
    /// Each frame is prefixed with a 4-byte little-endian length header.
    /// </summary>
    /// <remarks>
    /// Frame format: [Length: 4 bytes LE][Frame Data: N bytes]
    /// Unix Domain Sockets provide high-performance IPC on Linux/macOS.
    /// </remarks>
    public class UnixSocketFrameSink : IFrameSink
    {
        private readonly NetworkStream _stream;
        private readonly Socket? _socket;
        private readonly UnixSocketServer? _server;
        private readonly bool _leaveOpen;
        private bool _disposed;

        /// <summary>
        /// Creates a Unix socket frame sink from a NetworkStream.
        /// </summary>
        /// <param name="stream">NetworkStream from Unix socket</param>
        /// <param name="leaveOpen">If true, doesn't dispose stream on disposal</param>
        public UnixSocketFrameSink(NetworkStream stream, bool leaveOpen = false)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _leaveOpen = leaveOpen;
        }

        /// <summary>
        /// Creates a Unix socket frame sink from a connected Socket.
        /// </summary>
        /// <param name="socket">Connected Unix domain socket</param>
        /// <param name="leaveOpen">If true, doesn't close socket on disposal</param>
        public UnixSocketFrameSink(Socket socket, bool leaveOpen = false)
            : this(socket, server: null, leaveOpen)
        {
        }

        /// <summary>
        /// Creates a Unix socket frame sink from a connected Socket with optional server ownership.
        /// </summary>
        /// <param name="socket">Connected Unix domain socket</param>
        /// <param name="server">Optional server to dispose when sink is disposed</param>
        /// <param name="leaveOpen">If true, doesn't close socket on disposal</param>
        internal UnixSocketFrameSink(Socket socket, UnixSocketServer? server, bool leaveOpen = false)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _server = server;

            if (socket.AddressFamily != AddressFamily.Unix)
                throw new ArgumentException("Socket must be a Unix domain socket", nameof(socket));

            _stream = new NetworkStream(socket, ownsSocket: false);
            _leaveOpen = leaveOpen;
        }

        /// <summary>
        /// Connects to a Unix socket path and creates a frame sink.
        /// </summary>
        /// <param name="socketPath">Path to Unix socket file</param>
        /// <returns>Connected frame sink</returns>
        public static UnixSocketFrameSink Connect(string socketPath)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));
            return new UnixSocketFrameSink(socket, leaveOpen: false);
        }

        /// <summary>
        /// Connects to a Unix socket path asynchronously and creates a frame sink.
        /// </summary>
        /// <param name="socketPath">Path to Unix socket file</param>
        /// <returns>Connected frame sink</returns>
        public static async Task<UnixSocketFrameSink> ConnectAsync(string socketPath)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath));
            return new UnixSocketFrameSink(socket, leaveOpen: false);
        }

        /// <summary>
        /// Connects to a Unix socket path asynchronously with timeout and optional retry.
        /// </summary>
        /// <param name="socketPath">Path to Unix socket file</param>
        /// <param name="timeout">Maximum time to wait for connection</param>
        /// <param name="retry">If true, retries connection until timeout; if false, fails immediately on error</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Connected frame sink</returns>
        /// <exception cref="TimeoutException">Thrown when connection cannot be established within timeout</exception>
        public static async Task<UnixSocketFrameSink> ConnectAsync(
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
                        return new UnixSocketFrameSink(socket, leaveOpen: false);
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

        /// <summary>
        /// Binds to a Unix socket path as a server and waits for a client to connect.
        /// Use this when the SDK is the producer (server) and rocket-welder2 is the consumer (client).
        /// </summary>
        /// <param name="socketPath">Path to Unix socket file</param>
        /// <returns>Frame sink connected to the first client</returns>
        /// <remarks>
        /// This is the server-side counterpart to <see cref="Connect"/>.
        /// The server binds and listens, then blocks until a client connects.
        /// </remarks>
        public static UnixSocketFrameSink Bind(string socketPath)
        {
            var server = new UnixSocketServer(socketPath);
            server.Start();
            var clientSocket = server.Accept();
            return new UnixSocketFrameSink(clientSocket, server, leaveOpen: false);
        }

        /// <summary>
        /// Binds to a Unix socket path as a server and waits asynchronously for a client to connect.
        /// </summary>
        /// <param name="socketPath">Path to Unix socket file</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Frame sink connected to the first client</returns>
        public static async Task<UnixSocketFrameSink> BindAsync(string socketPath, CancellationToken cancellationToken = default)
        {
            var server = new UnixSocketServer(socketPath);
            server.Start();
            var clientSocket = await server.AcceptAsync(cancellationToken);
            return new UnixSocketFrameSink(clientSocket, server, leaveOpen: false);
        }

        public void WriteFrame(ReadOnlySpan<byte> frameData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UnixSocketFrameSink));

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
                throw new ObjectDisposedException(nameof(UnixSocketFrameSink));

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
                throw new ObjectDisposedException(nameof(UnixSocketFrameSink));

            _stream.Flush();
        }

        public async Task FlushAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UnixSocketFrameSink));

            await _stream.FlushAsync();
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

            // Always dispose server (cleans up socket file)
            _server?.Dispose();
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

            // Always dispose server (cleans up socket file)
            _server?.Dispose();
        }
    }
}
