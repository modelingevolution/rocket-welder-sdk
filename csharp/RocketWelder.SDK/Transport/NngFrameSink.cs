using System;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Transport
{
    /// <summary>
    /// Frame sink that publishes to NNG Pub/Sub pattern.
    /// Each frame is sent as a single NNG message.
    /// </summary>
    /// <remarks>
    /// Requires ModelingEvolution.Nng package.
    /// Uses NNG Publisher socket for one-to-many distribution.
    /// </remarks>
    public class NngFrameSink : IFrameSink
    {
        // TODO: Add ModelingEvolution.Nng package reference
        // private readonly IPublisherSocket _socket;
        private readonly object _socket;
        private readonly bool _leaveOpen;
        private bool _disposed;

        /// <summary>
        /// Creates an NNG frame sink using a Publisher socket.
        /// </summary>
        /// <param name="socket">NNG Publisher socket</param>
        /// <param name="leaveOpen">If true, doesn't close socket on disposal</param>
        public NngFrameSink(object socket /* IPublisherSocket */, bool leaveOpen = false)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _leaveOpen = leaveOpen;
        }

        public void WriteFrame(ReadOnlySpan<byte> frameData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NngFrameSink));

            // TODO: Implement with ModelingEvolution.Nng
            // Each frame = one NNG message (atomic send)
            // _socket.Send(frameData.ToArray());

            throw new NotImplementedException(
                "NNG transport requires ModelingEvolution.Nng package. " +
                "Add package reference and uncomment implementation.");
        }

        public async ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frameData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NngFrameSink));

            // TODO: Implement with ModelingEvolution.Nng
            // await _socket.SendAsync(frameData);

            await Task.CompletedTask;
            throw new NotImplementedException(
                "NNG transport requires ModelingEvolution.Nng package. " +
                "Add package reference and uncomment implementation.");
        }

        public void Flush()
        {
            // NNG sends immediately, no buffering
        }

        public Task FlushAsync()
        {
            // NNG sends immediately, no buffering
            return Task.CompletedTask;
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
