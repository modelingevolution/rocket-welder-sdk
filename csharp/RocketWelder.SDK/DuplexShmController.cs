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
        private Action<Mat, Mat>? _onFrame;
        
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

        public void Start(Action<Mat, Mat> onFrame, CancellationToken cancellationToken = default)
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

        public void Start(Action<Mat> onFrame, CancellationToken cancellationToken = default)
        {
            // For single Mat callback in duplex mode, we treat it as in-place processing.
            Start((input, output) =>
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
            if (!_gstCaps.HasValue || _onFrame == null)
                return;

            unsafe
            {
                // Create input Mat from request frame (zero-copy)
                using var inputMat = _gstCaps.Value.CreateMat(request.Pointer);

                var b = responseWriter.GetFrameBuffer(request.Size, out var s);
                using var outputMat = _gstCaps.Value.CreateMat(b);
                
                // Process frame
                _onFrame(inputMat, outputMat);

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