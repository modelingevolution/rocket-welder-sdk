using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using nng;
using nng.Native;
using nng.Factories.Latest;
using static nng.Native.Defines;

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
            var sender = NngPublisherSender.Create(url);
            return new NngFrameSink(sender, leaveOpen: false);
        }

        /// <summary>
        /// Creates an NNG Pusher frame sink connected to the specified URL.
        /// </summary>
        /// <param name="url">NNG URL (e.g., "tcp://127.0.0.1:5555", "ipc:///tmp/mysocket")</param>
        /// <param name="bindMode">If true, listens (bind); if false, dials (connect)</param>
        /// <returns>Frame sink ready to push messages</returns>
        public static NngFrameSink CreatePusher(string url, bool bindMode = true)
        {
            var sender = NngPusherSender.Create(url, bindMode);
            return new NngFrameSink(sender, leaveOpen: false);
        }

        /// <summary>
        /// Gets the number of connected subscribers (for pub/sub pattern).
        /// </summary>
        public int SubscriberCount => (_sender as NngPublisherSender)?.SubscriberCount ?? 0;

        /// <summary>
        /// Waits for at least one subscriber to connect (for pub/sub pattern).
        /// </summary>
        /// <param name="timeout">Maximum time to wait</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if a subscriber connected, false if timed out</returns>
        public async Task<bool> WaitForSubscriberAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (_sender is NngPublisherSender publisher)
            {
                return await publisher.WaitForSubscriberAsync(timeout, cancellationToken);
            }
            return true; // Non-pub/sub senders don't need to wait
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
    /// NNG Publisher sender implementation using the real NNG library.
    /// Uses pipe notifications to track subscriber connections.
    /// </summary>
    internal sealed class NngPublisherSender : INngSender
    {
        private readonly IPubSocket _socket;
        private readonly ISendAsyncContext<INngMsg> _asyncContext;
        private readonly Factory _factory;
        private readonly SemaphoreSlim _subscriberConnected;
        private int _subscriberCount;
        private bool _disposed;
        private GCHandle _callbackHandle;

        public int SubscriberCount => _subscriberCount;

        private NngPublisherSender(IPubSocket socket, ISendAsyncContext<INngMsg> asyncContext, Factory factory)
        {
            _socket = socket;
            _asyncContext = asyncContext;
            _factory = factory;
            _subscriberConnected = new SemaphoreSlim(0);
        }

        public static NngPublisherSender Create(string url)
        {
            var factory = new Factory();
            var socket = factory.PublisherOpen().Unwrap();
            socket.Listen(url).Unwrap();
            var asyncContext = socket.CreateAsyncContext(factory).Unwrap();

            var sender = new NngPublisherSender(socket, asyncContext, factory);
            sender.SetupPipeNotifications();
            return sender;
        }

        private void SetupPipeNotifications()
        {
            // Create a callback that tracks pipe events
            PipeEventCallback callback = PipeCallback;
            // Keep the delegate alive for the lifetime of the socket
            _callbackHandle = GCHandle.Alloc(callback);

            // Register for AddPost (connection established) and RemPost (connection closed)
            _socket.Notify(NngPipeEv.AddPost, callback, IntPtr.Zero);
            _socket.Notify(NngPipeEv.RemPost, callback, IntPtr.Zero);
        }

        private void PipeCallback(nng_pipe pipe, NngPipeEv ev, IntPtr arg)
        {
            switch (ev)
            {
                case NngPipeEv.AddPost:
                    // A subscriber has connected
                    Interlocked.Increment(ref _subscriberCount);
                    try { _subscriberConnected.Release(); } catch { /* ignore if disposed */ }
                    break;
                case NngPipeEv.RemPost:
                    // A subscriber has disconnected
                    Interlocked.Decrement(ref _subscriberCount);
                    break;
            }
        }

        /// <summary>
        /// Waits for at least one subscriber to connect.
        /// </summary>
        public async Task<bool> WaitForSubscriberAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (_subscriberCount > 0)
                return true;

            return await _subscriberConnected.WaitAsync(timeout, cancellationToken);
        }

        public void Send(ReadOnlySpan<byte> data)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NngPublisherSender));

            // Synchronous send using socket directly
            _socket.Send(data).Unwrap();
        }

        public async ValueTask SendAsync(ReadOnlyMemory<byte> data)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NngPublisherSender));

            var msg = _factory.CreateMessage();
            msg.Append(data.Span);
            (await _asyncContext.Send(msg)).Unwrap();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _asyncContext.Dispose();
            _socket.Dispose();
            _subscriberConnected.Dispose();
            if (_callbackHandle.IsAllocated)
                _callbackHandle.Free();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// NNG Pusher sender implementation using the real NNG library.
    /// </summary>
    internal sealed class NngPusherSender : INngSender
    {
        private readonly IPushSocket _socket;
        private readonly ISendAsyncContext<INngMsg> _asyncContext;
        private readonly Factory _factory;
        private bool _disposed;

        private NngPusherSender(IPushSocket socket, ISendAsyncContext<INngMsg> asyncContext, Factory factory)
        {
            _socket = socket;
            _asyncContext = asyncContext;
            _factory = factory;
        }

        public static NngPusherSender Create(string url, bool bindMode = true)
        {
            var factory = new Factory();
            var socket = factory.PusherOpen().Unwrap();
            if (bindMode)
                socket.Listen(url).Unwrap();
            else
                socket.Dial(url).Unwrap();
            var asyncContext = socket.CreateAsyncContext(factory).Unwrap();
            return new NngPusherSender(socket, asyncContext, factory);
        }

        public void Send(ReadOnlySpan<byte> data)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NngPusherSender));

            // Synchronous send using socket directly
            _socket.Send(data).Unwrap();
        }

        public async ValueTask SendAsync(ReadOnlyMemory<byte> data)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NngPusherSender));

            var msg = _factory.CreateMessage();
            msg.Append(data.Span);
            (await _asyncContext.Send(msg)).Unwrap();
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
