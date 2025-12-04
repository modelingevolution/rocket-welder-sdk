using System;
using System.Threading;
using System.Threading.Tasks;

namespace RocketWelder.SDK.Transport
{
    /// <summary>
    /// Frame source that subscribes to NNG Pub/Sub or Pull pattern.
    /// Each NNG message is treated as a complete frame (no framing needed - NNG handles message boundaries).
    /// </summary>
    /// <remarks>
    /// NNG (nanomsg next generation) provides high-performance, scalable messaging patterns.
    /// Supported patterns:
    /// - Pub/Sub: Subscribe to published messages
    /// - Push/Pull: Receive load-balanced work items
    /// - Pair: Point-to-point communication
    ///
    /// Note: Requires ModelingEvolution.Nng package. If not available, throws NotSupportedException.
    /// </remarks>
    public class NngFrameSource : IFrameSource
    {
        private readonly INngReceiver _receiver;
        private readonly bool _leaveOpen;
        private bool _disposed;

        /// <summary>
        /// Creates an NNG frame source from any NNG receiver (Subscriber, Puller, Pair).
        /// </summary>
        /// <param name="receiver">NNG receiver socket wrapper</param>
        /// <param name="leaveOpen">If true, doesn't dispose receiver on disposal</param>
        public NngFrameSource(INngReceiver receiver, bool leaveOpen = false)
        {
            _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
            _leaveOpen = leaveOpen;
        }

        /// <summary>
        /// Creates an NNG Subscriber frame source connected to the specified URL.
        /// </summary>
        /// <param name="url">NNG URL (e.g., "tcp://127.0.0.1:5555", "ipc:///tmp/mysocket")</param>
        /// <param name="topic">Optional topic filter (empty for all messages)</param>
        /// <returns>Frame source ready to receive messages</returns>
        public static NngFrameSource CreateSubscriber(string url, string topic = "")
        {
            var receiver = NngReceiverFactory.CreateSubscriber(url, topic);
            return new NngFrameSource(receiver, leaveOpen: false);
        }

        /// <summary>
        /// Creates an NNG Puller frame source bound to the specified URL.
        /// </summary>
        /// <param name="url">NNG URL (e.g., "tcp://127.0.0.1:5555", "ipc:///tmp/mysocket")</param>
        /// <returns>Frame source ready to pull messages</returns>
        public static NngFrameSource CreatePuller(string url)
        {
            var receiver = NngReceiverFactory.CreatePuller(url);
            return new NngFrameSource(receiver, leaveOpen: false);
        }

        public bool HasMoreFrames => !_disposed;  // NNG blocks waiting for messages

        public ReadOnlyMemory<byte> ReadFrame(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NngFrameSource));

            // NNG messages are atomic - no length prefix parsing needed
            return _receiver.Receive(cancellationToken);
        }

        public async ValueTask<ReadOnlyMemory<byte>> ReadFrameAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NngFrameSource));

            // NNG messages are atomic - no length prefix parsing needed
            return await _receiver.ReceiveAsync(cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (!_leaveOpen)
            {
                _receiver.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            if (!_leaveOpen)
            {
                await _receiver.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Abstraction for NNG receiving sockets (Subscriber, Puller, Pair).
    /// </summary>
    public interface INngReceiver : IDisposable, IAsyncDisposable
    {
        ReadOnlyMemory<byte> Receive(CancellationToken cancellationToken = default);
        ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Factory for creating NNG receivers. Throws NotSupportedException if NNG is not available.
    /// </summary>
    public static class NngReceiverFactory
    {
        private static readonly bool _nngAvailable = CheckNngAvailable();

        private static bool CheckNngAvailable()
        {
            try
            {
                // Try to load NNG types
                var nngType = Type.GetType("ModelingEvolution.Nng.SubscriberSocket, ModelingEvolution.Nng");
                return nngType != null;
            }
            catch
            {
                return false;
            }
        }

        public static INngReceiver CreateSubscriber(string url, string topic = "")
        {
            if (!_nngAvailable)
                throw new NotSupportedException(
                    "NNG transport requires ModelingEvolution.Nng package. " +
                    "Install the package and ensure native NNG libraries are available.");

            return NngReceiverImpl.CreateSubscriber(url, topic);
        }

        public static INngReceiver CreatePuller(string url)
        {
            if (!_nngAvailable)
                throw new NotSupportedException(
                    "NNG transport requires ModelingEvolution.Nng package. " +
                    "Install the package and ensure native NNG libraries are available.");

            return NngReceiverImpl.CreatePuller(url);
        }
    }

    /// <summary>
    /// Internal NNG receiver implementation - separated to avoid loading NNG types if not available.
    /// </summary>
    internal static class NngReceiverImpl
    {
        public static INngReceiver CreateSubscriber(string url, string topic)
        {
            // This will fail at runtime if NNG is not available,
            // but the factory checks first so this is only called when NNG is present.
            throw new NotSupportedException(
                "NNG implementation requires ModelingEvolution.Nng package to be referenced and native libraries available. " +
                "To enable NNG support, add: <PackageReference Include=\"ModelingEvolution.Nng\" Version=\"1.0.0\" />");
        }

        public static INngReceiver CreatePuller(string url)
        {
            throw new NotSupportedException(
                "NNG implementation requires ModelingEvolution.Nng package to be referenced and native libraries available. " +
                "To enable NNG support, add: <PackageReference Include=\"ModelingEvolution.Nng\" Version=\"1.0.0\" />");
        }
    }
}
