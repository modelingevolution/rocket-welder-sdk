using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Transport
{
    /// <summary>
    /// Frame sink that writes to a WebSocket connection.
    /// Each frame is sent as a single binary WebSocket message.
    /// </summary>
    public class WebSocketFrameSink : IFrameSink
    {
        private readonly WebSocket _webSocket;
        private readonly bool _leaveOpen;
        private bool _disposed;

        /// <summary>
        /// Creates a WebSocket frame sink.
        /// </summary>
        /// <param name="webSocket">WebSocket to write to</param>
        /// <param name="leaveOpen">If true, doesn't close WebSocket on disposal</param>
        public WebSocketFrameSink(WebSocket webSocket, bool leaveOpen = false)
        {
            _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
            _leaveOpen = leaveOpen;
        }

        public void WriteFrame(ReadOnlySpan<byte> frameData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WebSocketFrameSink));

            // WebSocket doesn't have a synchronous Send, so we use the async version with Wait()
            WriteFrameAsync(frameData.ToArray()).AsTask().Wait();
        }

        public async ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frameData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WebSocketFrameSink));

            if (_webSocket.State != WebSocketState.Open)
                throw new InvalidOperationException($"WebSocket is not open: {_webSocket.State}");

            // Send as single binary message
            await _webSocket.SendAsync(
                frameData,
                WebSocketMessageType.Binary,
                endOfMessage: true,
                CancellationToken.None);
        }

        public void Flush()
        {
            // WebSocket sends immediately, no buffering
        }

        public Task FlushAsync()
        {
            // WebSocket sends immediately, no buffering
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (!_leaveOpen && _webSocket.State == WebSocketState.Open)
            {
                try
                {
                    _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Sink disposed", CancellationToken.None).Wait();
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
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Sink disposed", CancellationToken.None);
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
