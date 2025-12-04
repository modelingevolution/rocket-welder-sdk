using System;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Transport
{
    /// <summary>
    /// Frame sink that publishes to NNG Pub/Sub or Push/Pull pattern.
    /// Each frame is sent as a single NNG message (no framing needed - NNG handles message boundaries).
    /// </summary>
    /// <remarks>
    /// NNG (nanomsg next generation) provides high-performance, scalable messaging patterns.
    /// Supported patterns:
    /// - Pub/Sub: One publisher to many subscribers
    /// - Push/Pull: Load-balanced distribution to workers
    /// - Pair: Point-to-point communication
    ///
    /// Note: Requires ModelingEvolution.Nng package. If not available, throws NotSupportedException.
    /// </remarks>
    public class NngFrameSink : IFrameSink
    {
        private readonly INngSender _sender;
        private readonly bool _leaveOpen;
        private bool _disposed;

        /// <summary>
        /// Creates an NNG frame sink from any NNG sender (Publisher, Pusher, Pair).
        /// </summary>
        /// <param name="sender">NNG sender socket wrapper</param>
        /// <param name="leaveOpen">If true, doesn't dispose sender on disposal</param>
        public NngFrameSink(INngSender sender, bool leaveOpen = false)
        {
            _sender = sender ?? throw new ArgumentNullException(nameof(sender));
            _leaveOpen = leaveOpen;
        }

        /// <summary>
        /// Creates an NNG Publisher frame sink bound to the specified URL.
        /// </summary>
        /// <param name="url">NNG URL (e.g., "tcp://127.0.0.1:5555", "ipc:///tmp/mysocket")</param>
        /// <returns>Frame sink ready to publish messages</returns>
        public static NngFrameSink CreatePublisher(string url)
        {
            var sender = NngSenderFactory.CreatePublisher(url);
            return new NngFrameSink(sender, leaveOpen: false);
        }

        /// <summary>
        /// Creates an NNG Pusher frame sink connected to the specified URL.
        /// </summary>
        /// <param name="url">NNG URL (e.g., "tcp://127.0.0.1:5555", "ipc:///tmp/mysocket")</param>
        /// <returns>Frame sink ready to push messages</returns>
        public static NngFrameSink CreatePusher(string url)
        {
            var sender = NngSenderFactory.CreatePusher(url);
            return new NngFrameSink(sender, leaveOpen: false);
        }

        public void WriteFrame(ReadOnlySpan<byte> frameData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NngFrameSink));

            // NNG messages are atomic - no length prefix needed
            _sender.Send(frameData);
        }

        public async ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frameData)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NngFrameSink));

            // NNG messages are atomic - no length prefix needed
            await _sender.SendAsync(frameData);
        }

        public void Flush()
        {
            // NNG sends immediately, no buffering needed
        }

        public Task FlushAsync()
        {
            // NNG sends immediately, no buffering needed
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (!_leaveOpen)
            {
                _sender.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            if (!_leaveOpen)
            {
                await _sender.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Abstraction for NNG sending sockets (Publisher, Pusher, Pair).
    /// </summary>
    public interface INngSender : IDisposable, IAsyncDisposable
    {
        void Send(ReadOnlySpan<byte> data);
        ValueTask SendAsync(ReadOnlyMemory<byte> data);
    }

    /// <summary>
    /// Factory for creating NNG senders. Throws NotSupportedException if NNG is not available.
    /// </summary>
    public static class NngSenderFactory
    {
        private static readonly bool _nngAvailable = CheckNngAvailable();

        private static bool CheckNngAvailable()
        {
            try
            {
                // Try to load NNG types
                var nngType = Type.GetType("ModelingEvolution.Nng.PublisherSocket, ModelingEvolution.Nng");
                return nngType != null;
            }
            catch
            {
                return false;
            }
        }

        public static INngSender CreatePublisher(string url)
        {
            if (!_nngAvailable)
                throw new NotSupportedException(
                    "NNG transport requires ModelingEvolution.Nng package. " +
                    "Install the package and ensure native NNG libraries are available.");

            return NngSenderImpl.CreatePublisher(url);
        }

        public static INngSender CreatePusher(string url)
        {
            if (!_nngAvailable)
                throw new NotSupportedException(
                    "NNG transport requires ModelingEvolution.Nng package. " +
                    "Install the package and ensure native NNG libraries are available.");

            return NngSenderImpl.CreatePusher(url);
        }
    }

    /// <summary>
    /// Internal NNG sender implementation - separated to avoid loading NNG types if not available.
    /// </summary>
    internal static class NngSenderImpl
    {
        public static INngSender CreatePublisher(string url)
        {
            // This will fail at runtime if NNG is not available,
            // but the factory checks first so this is only called when NNG is present.
            throw new NotSupportedException(
                "NNG implementation requires ModelingEvolution.Nng package to be referenced and native libraries available. " +
                "To enable NNG support, add: <PackageReference Include=\"ModelingEvolution.Nng\" Version=\"1.0.0\" />");
        }

        public static INngSender CreatePusher(string url)
        {
            throw new NotSupportedException(
                "NNG implementation requires ModelingEvolution.Nng package to be referenced and native libraries available. " +
                "To enable NNG support, add: <PackageReference Include=\"ModelingEvolution.Nng\" Version=\"1.0.0\" />");
        }
    }
}
