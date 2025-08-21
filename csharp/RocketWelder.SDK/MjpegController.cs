using System;
using System.Threading;
using Emgu.CV;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RocketWelder.SDK
{
    internal class MjpegController : IController
    {
        private readonly ConnectionString _connection;
        private readonly ILogger<MjpegController> _logger;
        private VideoCapture? _capture;
        private volatile bool _isRunning;
        private Thread? _worker;
        private GstMetadata? _metadata;
        
        public bool IsRunning => _isRunning;
        
        public GstMetadata? GetMetadata() => _metadata;
        
        public event Action<IController, Exception>? OnError;

        public MjpegController(in ConnectionString connection, ILoggerFactory? loggerFactory = null)
        {
            _connection = connection;
            var factory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = factory.CreateLogger<MjpegController>();
        }

        public void Start(Action<Mat, Mat> onFrame, CancellationToken cancellationToken = default)
        {
            // For MJPEG streams with duplex callback, allocate output Mat but process as one-way
            Start((input) =>
            {
                using var output = new Mat();
                onFrame(input, output);
            }, cancellationToken);
        }

        public void Start(Action<Mat> onFrame, CancellationToken cancellationToken = default)
        {
            if (_isRunning)
                throw new InvalidOperationException("Already running");

            _isRunning = true;

            // Construct URL based on protocol
            string url;
            if (_connection.Protocol.HasFlag(Protocol.Http))
            {
                url = $"http://{_connection.Host}:{_connection.Port}";
            }
            else if (_connection.Protocol.HasFlag(Protocol.Tcp))
            {
                url = $"tcp://{_connection.Host}:{_connection.Port}";
            }
            else
            {
                throw new NotSupportedException($"Protocol {_connection.Protocol} is not supported for MJPEG");
            }

            _logger.LogInformation("Connecting to MJPEG stream at {Url}", url);

            // Create VideoCapture with URL
            _capture = new VideoCapture(url);
            
            if (!_capture.IsOpened)
            {
                _capture?.Dispose();
                _capture = null;
                _isRunning = false;
                throw new InvalidOperationException($"Failed to open MJPEG stream at {url}");
            }

            // Get video properties to build metadata
            var width = (int)_capture.Get(Emgu.CV.CvEnum.CapProp.FrameWidth);
            var height = (int)_capture.Get(Emgu.CV.CvEnum.CapProp.FrameHeight);
            var fps = _capture.Get(Emgu.CV.CvEnum.CapProp.Fps);
            
            // Create GstCaps from video properties
            var caps = GstCaps.FromSimple(width, height, "RGB");
            _metadata = new GstMetadata(
                Type: "video",
                Version: "1.0",
                Caps: caps,
                ElementName: "mjpeg-capture"
            );
            
            _logger.LogInformation("MJPEG stream opened: {Width}x{Height} @ {Fps}fps", width, height, fps);

            // Start processing on worker thread
            _worker = new Thread(() => ProcessFrames(onFrame, cancellationToken))
            {
                Name = $"RocketWelder-MJPEG-{_connection.Host}",
                IsBackground = false
            };
            _worker.Start();
        }

        private void ProcessFrames(Action<Mat> onFrame, CancellationToken cancellationToken)
        {
            using var frame = new Mat();
            
            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Read frame from VideoCapture
                    if (!_capture!.Read(frame))
                    {
                        _logger.LogWarning("Failed to read frame from MJPEG stream");
                        Thread.Sleep(10); // Brief pause before retry
                        continue;
                    }

                    if (frame.IsEmpty)
                    {
                        Thread.Sleep(10); // Brief pause if frame is empty
                        continue;
                    }

                    // Process frame
                    onFrame(frame);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing MJPEG frame");
                    OnError?.Invoke(this, ex);
                    if (!_isRunning) break;
                    Thread.Sleep(100); // Longer pause on error
                }
            }
        }

        public void Stop(CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Stopping MJPEG controller for {Host}:{Port}", _connection.Host, _connection.Port);
            _isRunning = false;
            _worker?.Join(TimeSpan.FromMilliseconds(_connection.TimeoutMs + 50));
            _worker = null;
            
            _capture?.Dispose();
            _capture = null;
            
            _logger.LogInformation("Stopped MJPEG controller for {Host}:{Port}", _connection.Host, _connection.Port);
        }

        public void Dispose()
        {
            _logger.LogDebug("Disposing MJPEG controller for {Host}:{Port}", _connection.Host, _connection.Port);
            _isRunning = false;
            _worker?.Join(TimeSpan.FromMilliseconds(100));
            _capture?.Dispose();
            _capture = null;
            _worker = null;
            _logger.LogInformation("Disposed MJPEG controller for {Host}:{Port}", _connection.Host, _connection.Port);
        }
    }
}