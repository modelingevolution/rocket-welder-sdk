using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RocketWelder.SDK.Protocols;
using RocketWelder.SDK.Transport;
using ZeroBuffer;
using ZeroBuffer.DuplexChannel;

namespace RocketWelder.SDK.Server
{
    /// <summary>
    /// Server that communicates with RocketWelderClient via DuplexChannel.
    /// Acts as the "GStreamer side" - sends frames and receives processed results.
    /// Uses ZeroBuffer.DuplexChannel under the hood.
    /// </summary>
    public class Server : IDisposable
    {
        private readonly string _channelName;
        private readonly int _width;
        private readonly int _height;
        private readonly string _format;
        private readonly ILoggerFactory? _loggerFactory;
        private readonly ILogger<Server> _logger;

        private IDuplexClient? _client;
        private GstCaps _caps;
        private ulong _frameNumber;
        private bool _started;
        private bool _disposed;

        // Socket readers for AI results
        private UnixSocketFrameSource? _keypointsSource;
        private UnixSocketFrameSource? _segmentationSource;

        /// <summary>
        /// Creates a server with the specified channel configuration.
        /// </summary>
        /// <param name="channelName">Shared memory channel name (e.g., "video-input")</param>
        /// <param name="width">Frame width in pixels</param>
        /// <param name="height">Frame height in pixels</param>
        /// <param name="format">Pixel format (default: RGB)</param>
        /// <param name="loggerFactory">Optional logger factory</param>
        public Server(string channelName, int width, int height, string format = "RGB", ILoggerFactory? loggerFactory = null)
        {
            _channelName = channelName ?? throw new ArgumentNullException(nameof(channelName));
            _width = width;
            _height = height;
            _format = format;
            _loggerFactory = loggerFactory;
            _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<Server>();
            _caps = GstCaps.FromSimple(width, height, format);
        }

        /// <summary>
        /// Current frame number (incremented on each SendFrame).
        /// </summary>
        public ulong FrameNumber => _frameNumber;

        /// <summary>
        /// Whether the SDK client is currently connected.
        /// </summary>
        public bool IsClientConnected => _client?.IsServerConnected ?? false;

        /// <summary>
        /// Starts the server and waits for client to connect.
        /// </summary>
        /// <param name="timeout">Timeout for waiting for client connection</param>
        public void Start(TimeSpan? timeout = null)
        {
            if (_started)
                throw new InvalidOperationException("Server already started");

            _logger.LogInformation("Starting server for channel '{ChannelName}' with {Width}x{Height} {Format}",
                _channelName, _width, _height, _format);

            // Create duplex client
            var factory = new DuplexChannelFactory(_loggerFactory);
            _client = factory.CreateClient(_channelName);

            // Set metadata (GstMetadata JSON)
            var metadata = new GstMetadata(
                Type: "zerosrc",
                Version: "1.0",
                Caps: _caps,
                ElementName: "sdk-server"
            );
            var metadataJson = JsonSerializer.SerializeToUtf8Bytes(metadata);
            _client.SetMetadata(metadataJson);

            _started = true;

            // Wait for server (RocketWelderClient) to connect
            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
            var deadline = DateTime.UtcNow + effectiveTimeout;

            while (!_client.IsServerConnected && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }

            if (!_client.IsServerConnected)
            {
                _logger.LogWarning("Client did not connect within timeout");
            }
            else
            {
                _logger.LogInformation("Client connected to channel '{ChannelName}'", _channelName);
            }
        }

        /// <summary>
        /// Starts the server asynchronously and waits for client to connect.
        /// </summary>
        public async Task StartAsync(TimeSpan? timeout = null, CancellationToken ct = default)
        {
            if (_started)
                throw new InvalidOperationException("Server already started");

            _logger.LogInformation("Starting server for channel '{ChannelName}' with {Width}x{Height} {Format}",
                _channelName, _width, _height, _format);

            // Create duplex client
            var factory = new DuplexChannelFactory(_loggerFactory);
            _client = factory.CreateClient(_channelName);

            // Set metadata (GstMetadata JSON)
            var metadata = new GstMetadata(
                Type: "zerosrc",
                Version: "1.0",
                Caps: _caps,
                ElementName: "sdk-server"
            );
            var metadataJson = JsonSerializer.SerializeToUtf8Bytes(metadata);
            _client.SetMetadata(metadataJson);

            _started = true;

            // Wait for server (RocketWelderClient) to connect
            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
            var deadline = DateTime.UtcNow + effectiveTimeout;

            while (!_client.IsServerConnected && DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(100, ct);
            }

            if (!_client.IsServerConnected)
            {
                _logger.LogWarning("Client did not connect within timeout");
            }
            else
            {
                _logger.LogInformation("Client connected to channel '{ChannelName}'", _channelName);
            }
        }

        /// <summary>
        /// Sends a frame to the connected RocketWelderClient and receives processed output.
        /// </summary>
        /// <param name="inputFrame">Input frame (BGR/RGB pixel data)</param>
        /// <param name="responseTimeout">Timeout for waiting for response</param>
        /// <returns>Processed output frame from the client</returns>
        public unsafe Mat SendFrame(Mat inputFrame, TimeSpan? responseTimeout = null)
        {
            // Console.WriteLine($"[Server] SendFrame called, frame #{_frameNumber}");
            // Console.Out.Flush();

            if (!_started || _client == null)
                throw new InvalidOperationException("Server not started");

            if (inputFrame == null)
                throw new ArgumentNullException(nameof(inputFrame));

            // Validate frame dimensions
            if (inputFrame.Width != _width || inputFrame.Height != _height)
                throw new ArgumentException($"Frame dimensions {inputFrame.Width}x{inputFrame.Height} don't match server {_width}x{_height}");

            // Calculate frame data size (with FrameMetadata prefix)
            var pixelDataSize = _caps.FrameSize;
            var totalSize = FrameMetadata.Size + pixelDataSize;
            // Console.WriteLine($"[Server] Frame size: {pixelDataSize}, total with metadata: {totalSize}");
            // Console.Out.Flush();

            // Create frame metadata
            var frameMetadata = new FrameMetadata(_frameNumber, (ulong)(Stopwatch.GetTimestamp() * 1000));

            // Acquire buffer and write frame
            // Console.WriteLine($"[Server] Calling AcquireRequestBuffer...");
            // Console.Out.Flush();
            _client.AcquireRequestBuffer(totalSize, out var buffer);
            // Console.WriteLine($"[Server] AcquireRequestBuffer returned, buffer size: {buffer.Length}");
            // Console.Out.Flush();

            // Write FrameMetadata (24 bytes)
            MemoryMarshal.Write(buffer.Slice(0, FrameMetadata.Size), frameMetadata);

            // Write pixel data (zero-copy from Mat)
            var srcSpan = new ReadOnlySpan<byte>(inputFrame.DataPointer.ToPointer(), pixelDataSize);
            srcSpan.CopyTo(buffer.Slice(FrameMetadata.Size));

            // Commit and wait for response
            // Console.WriteLine($"[Server] Calling CommitRequest...");
            // Console.Out.Flush();
            _client.CommitRequest();
            // Console.WriteLine($"[Server] CommitRequest done, waiting for response...");
            // Console.Out.Flush();

            var effectiveTimeout = responseTimeout ?? TimeSpan.FromSeconds(5);
            var response = _client.ReceiveResponse(effectiveTimeout);
            // Console.WriteLine($"[Server] ReceiveResponse returned, IsValid: {response.IsValid}");
            // Console.Out.Flush();

            if (!response.IsValid)
            {
                _logger.LogWarning("Timeout waiting for frame response");
                throw new TimeoutException("Timeout waiting for frame response from SDK client");
            }

            _frameNumber++;

            // Create output Mat from response
            var outputMat = new Mat(_height, _width, _caps.Depth, _caps.Channels);
            response.Span.CopyTo(new Span<byte>(outputMat.DataPointer.ToPointer(), pixelDataSize));

            return outputMat;
        }

        /// <summary>
        /// Connects to SDK container's keypoints output socket to receive AI results.
        /// </summary>
        /// <param name="socketPath">Path to Unix socket file</param>
        /// <param name="timeout">Connection timeout with retry</param>
        public async Task ConnectToKeyPointsSocketAsync(string socketPath, TimeSpan timeout, CancellationToken ct = default)
        {
            _logger.LogInformation("Connecting to keypoints socket: {SocketPath}", socketPath);
            _keypointsSource = await UnixSocketFrameSource.ConnectAsync(socketPath, timeout, retry: true, ct);
            _logger.LogInformation("Connected to keypoints socket");
        }

        /// <summary>
        /// Connects to SDK container's segmentation output socket to receive AI results.
        /// </summary>
        /// <param name="socketPath">Path to Unix socket file</param>
        /// <param name="timeout">Connection timeout with retry</param>
        public async Task ConnectToSegmentationSocketAsync(string socketPath, TimeSpan timeout, CancellationToken ct = default)
        {
            _logger.LogInformation("Connecting to segmentation socket: {SocketPath}", socketPath);
            _segmentationSource = await UnixSocketFrameSource.ConnectAsync(socketPath, timeout, retry: true, ct);
            _logger.LogInformation("Connected to segmentation socket");
        }

        /// <summary>
        /// Reads keypoints frames from connected socket.
        /// </summary>
        /// <param name="timeout">Read timeout</param>
        /// <returns>DeltaFrame with keypoints if available, null if timeout or end of stream</returns>
        public DeltaFrame<KeyPoint>? TryReadKeyPointsFrame(TimeSpan timeout)
        {
            if (_keypointsSource == null)
                throw new InvalidOperationException("Not connected to keypoints socket");

            if (!_keypointsSource.HasMoreFrames)
                return null;

            try
            {
                using var cts = new CancellationTokenSource(timeout);
                var frameData = _keypointsSource.ReadFrame(cts.Token);

                if (frameData.IsEmpty)
                    return null;

                // Deserialize using protocol reader (uses ReadOnlySpan<byte>)
                return KeyPointsProtocol.Read(frameData.Span);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// Reads segmentation frames from connected socket.
        /// </summary>
        /// <param name="timeout">Read timeout</param>
        /// <returns>SegmentationFrame if available, null if timeout or end of stream</returns>
        public RocketWelder.SDK.Protocols.SegmentationFrame? TryReadSegmentationFrame(TimeSpan timeout)
        {
            if (_segmentationSource == null)
                throw new InvalidOperationException("Not connected to segmentation socket");

            if (!_segmentationSource.HasMoreFrames)
                return null;

            try
            {
                using var cts = new CancellationTokenSource(timeout);
                var frameData = _segmentationSource.ReadFrame(cts.Token);

                if (frameData.IsEmpty)
                    return null;

                // Deserialize using protocol reader (uses ReadOnlySpan<byte>)
                return SegmentationProtocol.Read(frameData.Span);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// Stops the server.
        /// </summary>
        public void Stop()
        {
            _logger.LogDebug("Stopping server for channel '{ChannelName}'", _channelName);

            _keypointsSource?.Dispose();
            _keypointsSource = null;

            _segmentationSource?.Dispose();
            _segmentationSource = null;

            if (_client is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _client = null;

            _started = false;
            _logger.LogInformation("Stopped server for channel '{ChannelName}'", _channelName);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
