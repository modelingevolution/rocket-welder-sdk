using System;
using System.Text.Json;
using System.Threading;
using Emgu.CV;
using ZeroBuffer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RocketWelder.SDK
{
    internal class OneWayShmController : IController
    {
        private readonly ConnectionString _connection;
        private readonly ILogger<OneWayShmController> _logger;
        private readonly ILogger<Reader> _readerLogger;
        private Reader? _reader;
        private GstCaps? _gstCaps;
        private volatile bool _isRunning;
        private Thread? _worker;
        private GstMetadata? _metadata;
        
        public bool IsRunning => _isRunning;
        
        public GstMetadata? GetMetadata() => _metadata;

        public OneWayShmController(in ConnectionString connection, ILoggerFactory? loggerFactory = null)
        {
            _connection = connection;
            var factory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = factory.CreateLogger<OneWayShmController>();
            _readerLogger = factory.CreateLogger<Reader>();
        }

        public void Start(Action<Mat, Mat> onFrame, CancellationToken cancellationToken = default)
        {
            if (_isRunning)
                throw new InvalidOperationException("Already running");

            _isRunning = true;

            // Create buffer - we are the server, GStreamer connects to us
            var config = new BufferConfig
            {
                PayloadSize = (int)(long)_connection.BufferSize,
                MetadataSize = (int)(long)_connection.MetadataSize
            };
            _reader = new Reader(_connection.BufferName!, config, _readerLogger);
            _logger.LogInformation("Created shared memory buffer '{BufferName}' with size {BufferSize} and metadata {MetadataSize}", 
                _connection.BufferName, _connection.BufferSize, _connection.MetadataSize);

            // Start processing on worker thread with duplex callback
            _worker = new Thread(() => ProcessFramesDuplex(onFrame, cancellationToken))
            {
                Name = $"RocketWelder-{_connection.BufferName}",
                IsBackground = false
            };
            _worker.Start();
        }

        public void Start(Action<Mat> onFrame, CancellationToken cancellationToken = default)
        {
            if (_isRunning)
                throw new InvalidOperationException("Already running");

            _isRunning = true;

            // Create buffer - we are the server, GStreamer connects to us
            var config = new BufferConfig
            {
                PayloadSize = (int)(long)_connection.BufferSize,
                MetadataSize = (int)(long)_connection.MetadataSize
            };
            _reader = new Reader(_connection.BufferName!, config, _readerLogger);
            _logger.LogInformation("Created shared memory buffer '{BufferName}' with size {BufferSize} and metadata {MetadataSize}", 
                _connection.BufferName, _connection.BufferSize, _connection.MetadataSize);

            // Start processing on worker thread
            _worker = new Thread(() => ProcessFrames(onFrame, cancellationToken))
            {
                Name = $"RocketWelder-{_connection.BufferName}",
                IsBackground = false
            };
            _worker.Start();
        }

        private void ProcessFrames(Action<Mat> onFrame, CancellationToken cancellationToken)
        {
            OnFirstFrame(onFrame, cancellationToken);

            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // ReadFrame blocks until frame available
                    using var frame = _reader!.ReadFrame(TimeSpan.FromMilliseconds(_connection.TimeoutMs));

                    if (!frame.IsValid)
                        continue; // Skip invalid frames


                    // Create Mat wrapping frame data (zero-copy)
                    unsafe
                    {
                        using var mat = _gstCaps!.Value.CreateMat(frame.Pointer);
                        onFrame(mat);
                    }
                }
                catch (ReaderDeadException)
                {
                    _logger.LogInformation("Writer disconnected from buffer '{BufferName}'", _connection.BufferName);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing frame from buffer '{BufferName}'", _connection.BufferName);
                    if (!_isRunning) break;
                }
            }
        }

        private void ProcessFramesDuplex(Action<Mat, Mat> onFrame, CancellationToken cancellationToken)
        {
            // Get first frame to initialize caps
            OnFirstFrameDuplex(onFrame, cancellationToken);

            // Allocate output Mat once - will be reused (though we ignore it in OneWay mode)


            using var outputMat = new Mat(_gstCaps!.Value.Height, _gstCaps.Value.Width, _gstCaps.Value.Depth, _gstCaps.Value.Channels);

            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // ReadFrame blocks until frame available
                    using var frame = _reader!.ReadFrame(TimeSpan.FromMilliseconds(_connection.TimeoutMs));

                    if (!frame.IsValid)
                        continue; // Skip invalid frames

                    // Create Mat wrapping frame data (zero-copy)
                    unsafe
                    {
                        using var mat = _gstCaps!.Value.CreateMat(frame.Pointer);
                        onFrame(mat, outputMat);
                        // We ignore the output Mat in OneWay mode
                    }
                }
                catch (ReaderDeadException)
                {
                    _logger.LogInformation("Writer disconnected from buffer '{BufferName}'", _connection.BufferName);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing frame from buffer '{BufferName}'", _connection.BufferName);
                    if (!_isRunning) break;
                }
            }

        }

        private void OnFirstFrameDuplex(Action<Mat, Mat> onFrame, CancellationToken cancellationToken)
        {
            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // ReadFrame blocks until frame available
                    using var frame = _reader!.ReadFrame(TimeSpan.FromMilliseconds(_connection.TimeoutMs));

                    if (!frame.IsValid)
                        continue; // Skip invalid frames

                    // Read metadata - we ALWAYS expect metadata
                    var metadataBytes = _reader.GetMetadata();
                    _metadata = JsonSerializer.Deserialize<GstMetadata>(metadataBytes);
                    _gstCaps = _metadata!.Caps;
                    _logger.LogInformation("Received metadata from buffer '{BufferName}': {Caps}", _connection.BufferName, _gstCaps);

                    // Allocate output Mat for first frame
                    using var outputMat = new Mat(_gstCaps!.Value.Height, _gstCaps.Value.Width, _gstCaps.Value.Depth, _gstCaps.Value.Channels);

                    unsafe
                    {
                        using var mat = _gstCaps!.Value.CreateMat(frame.Pointer);
                        onFrame(mat, outputMat);
                    }

                    return; // Successfully processed first frame
                }
                catch (ReaderDeadException)
                {
                    _isRunning = false;
                    _logger.LogInformation("Writer disconnected while waiting for first frame on buffer '{BufferName}'", _connection.BufferName);
                    throw;
                }
                catch (Exception)
                {
                    // Log and continue
                    if (!_isRunning) break;
                }
            }

        }

        /// <summary>
        /// We read the metadata and the first frame to initialize the caps.
        /// </summary>
        /// <param name="onFrame"></param>
        /// <param name="cancellationToken"></param>
        private void OnFirstFrame(Action<Mat> onFrame, CancellationToken cancellationToken)
        {
            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // ReadFrame blocks until frame available
                    using var frame = _reader!.ReadFrame(TimeSpan.FromMilliseconds(_connection.TimeoutMs));

                    if (!frame.IsValid)
                        continue; // Skip invalid frames

                    // Read metadata - we ALWAYS expect metadata
                    var metadataBytes = _reader.GetMetadata();
                    _metadata = JsonSerializer.Deserialize<GstMetadata>(metadataBytes);
                    _gstCaps = _metadata!.Caps;
                    _logger.LogInformation("Received metadata from buffer '{BufferName}': {Caps}", _connection.BufferName, _gstCaps);

                    unsafe
                    {
                        using var mat = _gstCaps!.Value.CreateMat(frame.Pointer);
                        onFrame(mat);
                    }

                    return; // Successfully processed first frame
                }
                catch (ReaderDeadException)
                {
                    _isRunning = false;
                    _logger.LogInformation("Writer disconnected while waiting for first frame on buffer '{BufferName}'", _connection.BufferName);
                    throw;
                }
                catch (Exception)
                {
                    // Log and continue
                    if (!_isRunning) break;
                }
            }
        }

        public void Stop(CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Stopping controller for buffer '{BufferName}'", _connection.BufferName);
            _isRunning = false;
            _worker?.Join(TimeSpan.FromMilliseconds(_connection.TimeoutMs + 50));
            _worker = null;
            _logger.LogInformation("Stopped controller for buffer '{BufferName}'", _connection.BufferName);
        }

        public void Dispose()
        {
            _logger.LogDebug("Disposing controller for buffer '{BufferName}'", _connection.BufferName);
            _isRunning = false;
            _worker?.Join(TimeSpan.FromMilliseconds(_connection.TimeoutMs + 50));
            _reader?.Dispose();
            _reader = null;
            _worker = null;
            _logger.LogInformation("Disposed controller for buffer '{BufferName}'", _connection.BufferName);
        }
    }
}