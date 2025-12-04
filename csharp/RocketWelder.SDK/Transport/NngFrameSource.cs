using System;
using System.Threading;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Transport
{
    /// <summary>
    /// Frame source that subscribes to NNG Pub/Sub pattern.
    /// Each NNG message is treated as a complete frame.
    /// </summary>
    /// <remarks>
    /// Requires ModelingEvolution.Nng package.
    /// Uses NNG Subscriber socket for receiving published messages.
    /// </remarks>
    public class NngFrameSource : IFrameSource
    {
        // TODO: Add ModelingEvolution.Nng package reference
        // private readonly ISubscriberSocket _socket;
        private readonly object _socket;
        private readonly bool _leaveOpen;
        private bool _disposed;

        /// <summary>
        /// Creates an NNG frame source using a Subscriber socket.
        /// </summary>
        /// <param name="socket">NNG Subscriber socket</param>
        /// <param name="leaveOpen">If true, doesn't close socket on disposal</param>
        public NngFrameSource(object socket /* ISubscriberSocket */, bool leaveOpen = false)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _leaveOpen = leaveOpen;
        }

        public bool HasMoreFrames => !_disposed;  // NNG subscriber waits for messages

        public ReadOnlyMemory<byte> ReadFrame(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NngFrameSource));

            // TODO: Implement with ModelingEvolution.Nng
            // var message = _socket.Receive(cancellationToken);
            // return message.AsMemory();

            throw new NotImplementedException(
                "NNG transport requires ModelingEvolution.Nng package. " +
                "Add package reference and uncomment implementation.");
        }

        public async ValueTask<ReadOnlyMemory<byte>> ReadFrameAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NngFrameSource));

            // TODO: Implement with ModelingEvolution.Nng
            // var message = await _socket.ReceiveAsync(cancellationToken);
            // return message.AsMemory();

            await Task.CompletedTask;
            throw new NotImplementedException(
                "NNG transport requires ModelingEvolution.Nng package. " +
                "Add package reference and uncomment implementation.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (!_leaveOpen)
            {
                // TODO: _socket?.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            if (!_leaveOpen)
            {
                // TODO: await _socket?.DisposeAsync();
            }

            await Task.CompletedTask;
        }
    }
}
