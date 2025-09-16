using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
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
        private readonly bool _preview;
        private readonly string _previewWindowName = "RocketWelder Preview";
        private PeriodicTimer? _frameTimer;

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

            // Parse preview parameter - show frames in OpenCV window
            _preview = connection.Parameters.TryGetValue("preview", out var previewStr) &&
                       bool.TryParse(previewStr, out var preview) && preview;
        }

        public void Start(Action<Mat, Mat> onFrame, CancellationToken cancellationToken = default)
        {
            // For streams with duplex callback, allocate output Mat but process as one-way
            Start((input) =>
            {
                using var output = new Mat();
                onFrame(input, output);

                // For preview in duplex mode, show the OUTPUT frame
                if (_preview)
                {
                    CvInvoke.Imshow(_previewWindowName, output);
                    // Check for 'q' key to quit preview (waitKey returns -1 if no key pressed)
                    var key = CvInvoke.WaitKey(1);
                    if (key == 'q' || key == 'Q')
                    {
                        _logger.LogInformation("Preview window closed by user");
                        CvInvoke.DestroyWindow(_previewWindowName);
                    }
                }
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

            // Note: Preview window will be created when we get the first frame

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
                    if (string.IsNullOrEmpty(_connection.Path))
                        throw new ArgumentException("File path is required for file protocol");

                    if (!File.Exists(_connection.Path))
                        throw new FileNotFoundException($"Video file not found: {_connection.Path}");

                    return _connection.Path;

                case Protocol.Mjpeg when _connection.Protocol.HasFlag(Protocol.Http):
                    return $"http://{_connection.Host}:{_connection.Port}";

                case Protocol.Mjpeg when _connection.Protocol.HasFlag(Protocol.Tcp):
                    return $"tcp://{_connection.Host}:{_connection.Port}";

                default:
                    throw new NotSupportedException($"Protocol {_connection.Protocol} is not supported by OpenCvController");
            }
        }

        private async void ProcessFrames(Action<Mat> onFrame, CancellationToken cancellationToken)
        {
            using var frame = new Mat();
            var frameDelayMs = TimeSpan.FromMilliseconds(1000d / (_metadata?.Caps.FrameRate ?? 30d));
            bool previewWindowCreated = false;

            // Create PeriodicTimer for file playback frame rate control
            _frameTimer = _connection.Protocol == Protocol.File ? new PeriodicTimer(frameDelayMs) : null;

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
                            await Task.Delay(10, cancellationToken);
                            continue;
                        }
                    }

                    if (frame.IsEmpty)
                    {
                        await Task.Delay(10, cancellationToken);
                        continue;
                    }

                    // Create preview window on first frame if requested
                    if (_preview && !previewWindowCreated)
                    {
                        CvInvoke.NamedWindow(_previewWindowName, Emgu.CV.CvEnum.WindowFlags.AutoSize);
                        _logger.LogInformation("Preview window created for {Width}x{Height} video", frame.Width, frame.Height);
                        previewWindowCreated = true;
                    }

                    // Process frame
                    onFrame(frame);

                    // Show preview if enabled (for one-way mode)
                    if (_preview)
                    {
                        CvInvoke.Imshow(_previewWindowName, frame);
                        // Check for 'q' key to quit preview
                        var key = CvInvoke.WaitKey(1);
                        if (key == 'q' || key == 'Q')
                        {
                            _logger.LogInformation("Preview window closed by user");
                            CvInvoke.DestroyWindow(_previewWindowName);
                        }
                    }

                    // Control frame rate for file playback using PeriodicTimer
                    if (_connection.Protocol == Protocol.File && _frameTimer != null)
                    {
                        try
                        {
                            await _frameTimer.WaitForNextTickAsync(cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing frame");
                    OnError?.Invoke(this, ex);
                    if (!_isRunning) break;
                    await Task.Delay(100, cancellationToken);
                }
            }

            _isRunning = false;
        }

        public void Stop(CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Stopping OpenCV controller");
            _isRunning = false;

            // Dispose the timer to stop it
            _frameTimer?.Dispose();
            _frameTimer = null;

            _worker?.Join(TimeSpan.FromMilliseconds(_connection.TimeoutMs + 50));
            _worker = null;

            _capture?.Dispose();
            _capture = null;

            // Clean up preview window
            if (_preview)
            {
                CvInvoke.DestroyWindow(_previewWindowName);
                CvInvoke.WaitKey(1); // Process any pending window events
            }

            _logger.LogInformation("Stopped OpenCV controller");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}