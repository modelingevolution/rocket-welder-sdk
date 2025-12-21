using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using nng;
using nng.Factories.Latest;

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
        /// <param name="topic">Optional topic filter (empty byte array for all messages)</param>
        /// <returns>Frame source ready to receive messages</returns>
        public static NngFrameSource CreateSubscriber(string url, byte[]? topic = null)
        {
            var receiver = NngSubscriberReceiver.Create(url, topic ?? Array.Empty<byte>());
            return new NngFrameSource(receiver, leaveOpen: false);
        }

        /// <summary>
        /// Creates an NNG Puller frame source bound to the specified URL.
        /// </summary>
        /// <param name="url">NNG URL (e.g., "tcp://127.0.0.1:5555", "ipc:///tmp/mysocket")</param>
        /// <param name="bindMode">If true, listens (bind); if false, dials (connect)</param>
        /// <returns>Frame source ready to pull messages</returns>
        public static NngFrameSource CreatePuller(string url, bool bindMode = true)
        {
            var receiver = NngPullerReceiver.Create(url, bindMode);
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
    /// NNG Subscriber receiver implementation using the real NNG library.
    /// </summary>
    internal sealed class NngSubscriberReceiver : INngReceiver
    {
        private readonly ISubSocket _socket;
        private readonly ISubAsyncContext<INngMsg> _asyncContext;
        private readonly Factory _factory;
        private bool _disposed;

        private NngSubscriberReceiver(ISubSocket socket, ISubAsyncContext<INngMsg> asyncContext, Factory factory)
        {
            _socket = socket;
            _asyncContext = asyncContext;
            _factory = factory;
        }

        public static NngSubscriberReceiver Create(string url, byte[] topic)
        {
            var factory = new Factory();
            var socket = factory.SubscriberOpen().Unwrap();
            socket.Dial(url).Unwrap();

            // Subscribe to topic (empty topic = all messages)
            socket.Subscribe(topic);

            var asyncContext = socket.CreateAsyncContext(factory).Unwrap();
            return new NngSubscriberReceiver(socket, asyncContext, factory);
        }

        public ReadOnlyMemory<byte> Receive(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NngSubscriberReceiver));

            // Synchronous receive using socket directly
            var result = _socket.RecvMsg();
            var msg = result.Unwrap();
            var data = msg.AsSpan().ToArray();
            msg.Dispose();
            return data;
        }

        public ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            // NNG.NET's ISubAsyncContext has a known issue where async receive hangs
            // when used with Pub/Sub pattern. The async context callback is never invoked
            // if there are no messages queued at the time of the call.
            // Use the synchronous Receive() method instead.
            // See: https://github.com/jeikabu/nng.NETCore/issues/110
            throw new NotSupportedException(
                "Async receive is not supported for NNG Pub/Sub pattern due to a known issue in NNG.NET. " +
                "Use the synchronous ReadFrame() method instead.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _asyncContext.Dispose();
            _socket.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// NNG Puller receiver implementation using the real NNG library.
    /// </summary>
    internal sealed class NngPullerReceiver : INngReceiver
    {
        private readonly IPullSocket _socket;
        private readonly IReceiveAsyncContext<INngMsg> _asyncContext;
        private readonly Factory _factory;
        private bool _disposed;

        private NngPullerReceiver(IPullSocket socket, IReceiveAsyncContext<INngMsg> asyncContext, Factory factory)
        {
            _socket = socket;
            _asyncContext = asyncContext;
            _factory = factory;
        }

        public static NngPullerReceiver Create(string url, bool bindMode = true)
        {
            var factory = new Factory();
            var socket = factory.PullerOpen().Unwrap();
            if (bindMode)
                socket.Listen(url).Unwrap();
            else
                socket.Dial(url).Unwrap();
            var asyncContext = socket.CreateAsyncContext(factory).Unwrap();
            return new NngPullerReceiver(socket, asyncContext, factory);
        }

        public ReadOnlyMemory<byte> Receive(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NngPullerReceiver));

            // Synchronous receive using socket directly
            var result = _socket.RecvMsg();
            var msg = result.Unwrap();
            var data = msg.AsSpan().ToArray();
            msg.Dispose();
            return data;
        }

        public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NngPullerReceiver));

            var result = await _asyncContext.Receive(cancellationToken);
            var msg = result.Unwrap();
            var data = msg.AsSpan().ToArray();
            msg.Dispose();
            return data;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _asyncContext.Dispose();
            _socket.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
