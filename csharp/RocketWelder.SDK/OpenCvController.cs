using System;
using System.IO;
using System.Threading;
using Emgu.CV;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RocketWelder.SDK
{
    internal class OpenCvController : IController
    {
        private readonly ConnectionString _connection;
        private readonly ILogger<OpenCvController> _logger;
        private VideoCapture? _capture;
        private volatile bool _isRunning;
        private Thread? _worker;
        private GstMetadata? _metadata;
        private readonly bool _loop;

        public bool IsRunning => _isRunning;

        public GstMetadata? GetMetadata() => _metadata;

        public event Action<IController, Exception>? OnError;

        public OpenCvController(in ConnectionString connection, ILoggerFactory? loggerFactory = null)
        {
            _connection = connection;
            var factory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = factory.CreateLogger<OpenCvController>();

            // Parse loop parameter for file protocol
            _loop = connection.Protocol == Protocol.File &&
                    connection.Parameters.TryGetValue("loop", out var loopStr) &&
                    bool.TryParse(loopStr, out var loop) && loop;
        }

        public void Start(Action<Mat, Mat> onFrame, CancellationToken cancellationToken = default)
        {
            // For streams with duplex callback, allocate output Mat but process as one-way
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

            // Construct source based on protocol
            string source = GetSource();

            _logger.LogInformation("Opening video source: {Source} (loop={Loop})", source, _loop);

            // Create VideoCapture with source
            _capture = new VideoCapture(source);

            if (!_capture.IsOpened)
            {
                _capture?.Dispose();
                _capture = null;
                _isRunning = false;
                throw new InvalidOperationException($"Failed to open video source: {source}");
            }

            // Get video properties to build metadata
            var width = (int)_capture.Get(Emgu.CV.CvEnum.CapProp.FrameWidth);
            var height = (int)_capture.Get(Emgu.CV.CvEnum.CapProp.FrameHeight);
            var fps = _capture.Get(Emgu.CV.CvEnum.CapProp.Fps);
            var frameCount = (int)_capture.Get(Emgu.CV.CvEnum.CapProp.FrameCount);

            // Create GstCaps from video properties
            var caps = GstCaps.FromSimple(width, height, "RGB");
            _metadata = new GstMetadata(
                Type: "video",
                Version: "1.0",
                Caps: caps,
                ElementName: _connection.Protocol == Protocol.File ? "file-capture" : "opencv-capture"
            );

            _logger.LogInformation("Video source opened: {Width}x{Height} @ {Fps}fps, {FrameCount} frames",
                width, height, fps, frameCount);

            // Start processing on worker thread
            _worker = new Thread(() => ProcessFrames(onFrame, cancellationToken))
            {
                Name = $"RocketWelder-OpenCV-{Path.GetFileNameWithoutExtension(source)}",
                IsBackground = false
            };
            _worker.Start();
        }

        private string GetSource()
        {
            switch (_connection.Protocol)
            {
                case Protocol.File:
                    // For file protocol, use the file path directly
                    if (string.IsNullOrEmpty(_connection.FilePath))
                        throw new ArgumentException("File path is required for file protocol");

                    if (!File.Exists(_connection.FilePath))
                        throw new FileNotFoundException($"Video file not found: {_connection.FilePath}");

                    return _connection.FilePath;

                case Protocol.Mjpeg when _connection.Protocol.HasFlag(Protocol.Http):
                    return $"http://{_connection.Host}:{_connection.Port}";

                case Protocol.Mjpeg when _connection.Protocol.HasFlag(Protocol.Tcp):
                    return $"tcp://{_connection.Host}:{_connection.Port}";

                default:
                    throw new NotSupportedException($"Protocol {_connection.Protocol} is not supported by OpenCvController");
            }
        }

        private void ProcessFrames(Action<Mat> onFrame, CancellationToken cancellationToken)
        {
            using var frame = new Mat();
            var frameDelayMs = _metadata?.Caps?.Framerate != null && _metadata.Caps.Framerate.Numerator > 0
                ? 1000 * _metadata.Caps.Framerate.Denominator / _metadata.Caps.Framerate.Numerator
                : 33; // Default to ~30fps

            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Read frame from VideoCapture
                    if (!_capture!.Read(frame))
                    {
                        if (_connection.Protocol == Protocol.File && _loop)
                        {
                            // Loop: Reset to beginning
                            _capture.Set(Emgu.CV.CvEnum.CapProp.PosFrames, 0);
                            _logger.LogDebug("Looping video from beginning");
                            continue;
                        }
                        else if (_connection.Protocol == Protocol.File)
                        {
                            // File ended without loop
                            _logger.LogInformation("Video file ended");
                            break;
                        }
                        else
                        {
                            // Network stream issue
                            _logger.LogWarning("Failed to read frame from stream");
                            Thread.Sleep(10);
                            continue;
                        }
                    }

                    if (frame.IsEmpty)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    // Process frame
                    onFrame(frame);

                    // Control frame rate for file playback
                    if (_connection.Protocol == Protocol.File)
                    {
                        Thread.Sleep(frameDelayMs);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing frame");
                    OnError?.Invoke(this, ex);
                    if (!_isRunning) break;
                    Thread.Sleep(100);
                }
            }

            _isRunning = false;
        }

        public void Stop(CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Stopping OpenCV controller");
            _isRunning = false;
            _worker?.Join(TimeSpan.FromMilliseconds(_connection.TimeoutMs + 50));
            _worker = null;

            _capture?.Dispose();
            _capture = null;

            _logger.LogInformation("Stopped OpenCV controller");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}