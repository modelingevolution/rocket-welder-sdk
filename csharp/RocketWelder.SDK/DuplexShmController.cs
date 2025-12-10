using System;
using System.Text.Json;
using System.Threading;
using Emgu.CV;
using ZeroBuffer;
using ZeroBuffer.DuplexChannel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RocketWelder.SDK
{
    internal class DuplexShmController : IController
    {
        private readonly ConnectionString _connection;
        private readonly ILogger<DuplexShmController> _logger;
        private readonly ILoggerFactory? _loggerFactory;
        private IImmutableDuplexServer? _server;
        private GstCaps? _gstCaps;
        private GstMetadata? _metadata;
        private volatile bool _isRunning;
        private Action<FrameMetadata, Mat, Mat>? _onFrame;

        public bool IsRunning => _isRunning;

        public GstMetadata? GetMetadata() => _metadata;

        public event Action<IController, Exception>? OnError;

        public DuplexShmController(in ConnectionString connection, ILoggerFactory? loggerFactory = null)
        {
            _connection = connection;
            _loggerFactory = loggerFactory;
            var factory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = factory.CreateLogger<DuplexShmController>();
        }

        /// <summary>
        /// Start processing frames with FrameMetadata.
        /// The callback receives FrameMetadata (frame number, timestamp, dimensions),
        /// input Mat, and output Mat.
        /// </summary>
        /// <param name="onFrame">Callback receiving (FrameMetadata, inputMat, outputMat)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        public void Start(Action<FrameMetadata, Mat, Mat> onFrame, CancellationToken cancellationToken = default)
        {
            if (_isRunning)
                throw new InvalidOperationException("Already running");

            _isRunning = true;
            _onFrame = onFrame;

            // Create duplex server configuration
            var config = new BufferConfig
            {
                PayloadSize = (int)(long)_connection.BufferSize,
                MetadataSize = (int)(long)_connection.MetadataSize
            };

            // Create server using factory
            var factory = new DuplexChannelFactory(_loggerFactory);
            _server = factory.CreateImmutableServer(_connection.BufferName!, config, TimeSpan.FromMilliseconds(_connection.TimeoutMs));

            // Subscribe to error events
            _server.OnError += OnServerError;

            _logger.LogInformation("Starting duplex server for channel '{ChannelName}' with size {BufferSize} and metadata {MetadataSize}",
                _connection.BufferName, _connection.BufferSize, _connection.MetadataSize);

            // Start server with request handler and metadata handler
            _server.Start(ProcessFrame, OnMetadata, ProcessingMode.SingleThread);
        }

        public void Start(Action<Mat, Mat> onFrame, CancellationToken cancellationToken = default)
        {
            // Wrap the legacy callback - ignore FrameMetadata
            Start((metadata, input, output) => onFrame(input, output), cancellationToken);
        }

        public void Start(Action<Mat> onFrame, CancellationToken cancellationToken = default)
        {
            // For single Mat callback in duplex mode, we treat it as in-place processing.
            Start((metadata, input, output) =>
            {
                onFrame(input);
                input.CopyTo(output);
            }, cancellationToken);
        }

        private void OnMetadata(ReadOnlySpan<byte> metadataBytes)
        {
            // Parse metadata on first frame
            var jsonString = System.Text.Encoding.UTF8.GetString(metadataBytes);
            _metadata = JsonSerializer.Deserialize<GstMetadata>(jsonString);
            _gstCaps = _metadata!.Caps;
            _logger.LogInformation("Received metadata for channel '{ChannelName}': {Caps}", _connection.BufferName, _gstCaps);
        }

        private void ProcessFrame(Frame request, Writer responseWriter)
        {
            if (_onFrame == null)
                return;

            // Frame now has FrameMetadata prepended (16 bytes: frame_number + timestamp_ns)
            if (request.Size < FrameMetadata.Size)
            {
                _logger.LogWarning("Frame too small for FrameMetadata: {Size} bytes", request.Size);
                return;
            }

            // GstCaps must be available (set via OnMetadata)
            if (_gstCaps == null)
            {
                _logger.LogWarning("GstCaps not available, skipping frame");
                return;
            }

            var caps = _gstCaps.Value;

            unsafe
            {
                // Read FrameMetadata from the beginning of the frame (16 bytes)
                var frameMetadata = FrameMetadata.FromPointer((IntPtr)request.Pointer);

                // Calculate pointer to actual pixel data (after metadata)
                byte* pixelDataPtr = request.Pointer + FrameMetadata.Size;
                var pixelDataSize = request.Size - FrameMetadata.Size;

                // Create input Mat from pixel data (zero-copy)
                // Width/height/format come from GstCaps (stream-level, not per-frame)
                using var inputMat = caps.CreateMat(pixelDataPtr);

                // Response doesn't need metadata prefix - just pixel data
                var b = responseWriter.GetFrameBuffer(pixelDataSize, out var s);
                using var outputMat = caps.CreateMat(b);

                // Process frame with metadata
                _onFrame(frameMetadata, inputMat, outputMat);

                responseWriter.CommitFrame();
            }
        }

        private void OnServerError(object? sender, ErrorEventArgs e)
        {
            var ex = e.Exception;

            // Raise the IController.OnError event
            OnError?.Invoke(this, ex);
        }

        public void Stop(CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Stopping duplex controller for channel '{ChannelName}'", _connection.BufferName);
            _isRunning = false;

            if (_server != null)
            {
                _server.OnError -= OnServerError;
                _server.Stop();
            }

            _logger.LogInformation("Stopped duplex controller for channel '{ChannelName}'", _connection.BufferName);
        }

        public void Dispose()
        {
            _logger.LogDebug("Disposing duplex controller for channel '{ChannelName}'", _connection.BufferName);
            _isRunning = false;

            if (_server != null)
            {
                _server.OnError -= OnServerError;
                _server.Dispose();
                _server = null;
            }

            _onFrame = null;
            _logger.LogInformation("Disposed duplex controller for channel '{ChannelName}'", _connection.BufferName);
        }
    }
}
