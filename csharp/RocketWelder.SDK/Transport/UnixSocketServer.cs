using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Transport;

/// <summary>
/// Unix Domain Socket server that binds, listens, and accepts connections.
/// Internal implementation used by <see cref="UnixSocketFrameSink.Bind"/>.
/// </summary>
/// <remarks>
/// DEAD CODE: Only used by UnixSocketFrameSink.Bind() which is not used by the high-level API.
/// RocketWelderClientImpl uses Connect() (client mode), not Bind() (server mode).
/// Only test code and StageSink (graphics) use Bind().
/// </remarks>
[Obsolete("Dead code - server-side socket not used by high-level API. Will be removed in future version.")]
internal sealed class UnixSocketServer : IDisposable
{
    private readonly string _socketPath;
    private Socket? _socket;
    private bool _disposed;

    public UnixSocketServer(string socketPath)
    {
        _socketPath = socketPath ?? throw new ArgumentNullException(nameof(socketPath));
    }

    /// <summary>
    /// Start listening on the Unix socket.
    /// Removes existing socket file if present.
    /// </summary>
    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UnixSocketServer));

        // Remove existing socket file if present
        if (File.Exists(_socketPath))
            File.Delete(_socketPath);

        _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _socket.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _socket.Listen(1);
    }

    /// <summary>
    /// Accept a client connection (blocking).
    /// </summary>
    /// <returns>Connected client socket</returns>
    public Socket Accept()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UnixSocketServer));
        if (_socket == null)
            throw new InvalidOperationException("Server not started. Call Start() first.");

        return _socket.Accept();
    }

    /// <summary>
    /// Accept a client connection asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Connected client socket</returns>
    public async Task<Socket> AcceptAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UnixSocketServer));
        if (_socket == null)
            throw new InvalidOperationException("Server not started. Call Start() first.");

        return await _socket.AcceptAsync(cancellationToken);
    }

    /// <summary>
    /// Stop the server and clean up the socket file.
    /// </summary>
    public void Stop()
    {
        if (_socket != null)
        {
            try { _socket.Close(); }
            catch { /* Ignore close errors */ }
            _socket = null;
        }

        // Clean up socket file
        if (File.Exists(_socketPath))
        {
            try { File.Delete(_socketPath); }
            catch { /* Ignore cleanup errors */ }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
