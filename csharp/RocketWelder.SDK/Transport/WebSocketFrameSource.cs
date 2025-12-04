using System;
using System.Buffers;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Transport
{
    /// <summary>
    /// Frame source that reads from a WebSocket connection.
    /// Each WebSocket binary message is treated as a complete frame.
    /// </summary>
    public class WebSocketFrameSource : IFrameSource
    {
        private readonly WebSocket _webSocket;
        private readonly bool _leaveOpen;
        private bool _disposed;

        /// <summary>
        /// Creates a WebSocket frame source.
        /// </summary>
        /// <param name="webSocket">WebSocket to read from</param>
        /// <param name="leaveOpen">If true, doesn't close WebSocket on disposal</param>
        public WebSocketFrameSource(WebSocket webSocket, bool leaveOpen = false)
        {
            _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
            _leaveOpen = leaveOpen;
        }

        public bool HasMoreFrames =>
            _webSocket.State == WebSocketState.Open ||
            _webSocket.State == WebSocketState.CloseSent;

        public ReadOnlyMemory<byte> ReadFrame(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WebSocketFrameSource));

            // WebSocket doesn't have a synchronous Receive, so we use the async version with Wait()
            return ReadFrameAsync(cancellationToken).AsTask().Result;
        }

        public async ValueTask<ReadOnlyMemory<byte>> ReadFrameAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WebSocketFrameSource));

            if (!HasMoreFrames)
                return ReadOnlyMemory<byte>.Empty;

            // Receive complete message (may span multiple frames)
            using var memoryStream = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(8192);

            try
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return ReadOnlyMemory<byte>.Empty;
                    }

                    if (result.MessageType != WebSocketMessageType.Binary)
                    {
                        throw new InvalidDataException($"Expected binary message, got {result.MessageType}");
                    }

                    memoryStream.Write(buffer, 0, result.Count);

                } while (!result.EndOfMessage);

                return memoryStream.ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (!_leaveOpen && _webSocket.State == WebSocketState.Open)
            {
                try
                {
                    _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Source disposed", CancellationToken.None).Wait();
                }
                catch
                {
                    // Best effort close
                }
                _webSocket.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            if (!_leaveOpen && _webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Source disposed", CancellationToken.None);
                }
                catch
                {
                    // Best effort close
                }
                _webSocket.Dispose();
            }
        }
    }
}
