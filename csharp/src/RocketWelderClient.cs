using System;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using ZeroBuffer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text;

namespace RocketWelder.SDK
{
    public class RocketWelderClient : IDisposable
    {
        private readonly ConnectionString _connection;
        private readonly ILogger<RocketWelderClient> _logger;
        public ConnectionString Connection => _connection;
        private Action<Mat>? _frameCallback;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _processingTask;
        private bool _isRunning;
        
        private Reader? _reader;
        private Writer? _writer;
        
        // Cached video format from metadata
        private GstCaps? _videoFormat;

        private RocketWelderClient(string connectionString, ILogger<RocketWelderClient>? logger = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentNullException(nameof(connectionString));
            
            _connection = ConnectionString.Parse(connectionString);
            _logger = logger ?? NullLogger<RocketWelderClient>.Instance;
        }

        /// <summary>
        /// Creates a client from command line arguments and environment variables.
        /// Environment variable CONNECTION_STRING is checked first, then overridden by args.
        /// </summary>
        public static RocketWelderClient From(string[] args)
        {
            // Check environment variable first
            string? connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");

            // Override with command line args if present
            if (args != null)
            {
                foreach (var arg in args)
                {
                    if (arg.StartsWith("shm://") || 
                        arg.StartsWith("mjpeg+http://") || 
                        arg.StartsWith("mjpeg+tcp://"))
                    {
                        connectionString = arg;
                        break;
                    }
                }
            }

            return new RocketWelderClient(connectionString ?? "shm://default", NullLogger<RocketWelderClient>.Instance);
        }

        /// <summary>
        /// Creates a client from IConfiguration.
        /// Looks for "RocketWelder:ConnectionString" in configuration.
        /// </summary>
        public static RocketWelderClient From(IConfiguration configuration)
        {
            return From(configuration, null);
        }
        
        /// <summary>
        /// Creates a client from IConfiguration with logger.
        /// Looks for "RocketWelder:ConnectionString" in configuration.
        /// </summary>
        public static RocketWelderClient From(IConfiguration configuration, ILogger<RocketWelderClient>? logger)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            // Try to get connection string from different configuration sources
            // First check environment variable directly (for backward compatibility)
            string? connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");
            
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                connectionString = 
                    configuration["CONNECTION_STRING"] ??  // Environment variable via IConfiguration
                    configuration["RocketWelder:ConnectionString"] ??
                    configuration["ConnectionString"] ??
                    configuration.GetConnectionString("RocketWelder");
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                // Check if we have individual connection components
                var protocol = configuration["RocketWelder:Protocol"];
                var host = configuration["RocketWelder:Host"];
                var port = configuration["RocketWelder:Port"];
                var path = configuration["RocketWelder:Path"] ?? configuration["RocketWelder:BufferName"];
                
                if (!string.IsNullOrWhiteSpace(protocol))
                {
                    // Build connection string from components
                    if (protocol.Equals("shm", StringComparison.OrdinalIgnoreCase))
                    {
                        connectionString = $"shm://{path ?? "default"}";
                    }
                    else if (!string.IsNullOrWhiteSpace(host))
                    {
                        var portPart = !string.IsNullOrWhiteSpace(port) ? $":{port}" : "";
                        var pathPart = !string.IsNullOrWhiteSpace(path) ? $"/{path}" : "";
                        connectionString = $"{protocol}://{host}{portPart}{pathPart}";
                    }
                }
            }

            return new RocketWelderClient(connectionString ?? "shm://default", logger);
        }

        /// <summary>
        /// Creates a client from environment variable CONNECTION_STRING.
        /// </summary>
        public static RocketWelderClient FromEnvironment()
        {
            string? connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");
            return new RocketWelderClient(connectionString ?? "shm://default", NullLogger<RocketWelderClient>.Instance);
        }

        /// <summary>
        /// Creates a client from a specific connection string.
        /// </summary>
        public static RocketWelderClient FromConnectionString(string connectionString)
        {
            return new RocketWelderClient(connectionString, NullLogger<RocketWelderClient>.Instance);
        }
        
        /// <summary>
        /// Creates a client from a specific connection string with logger.
        /// </summary>
        public static RocketWelderClient FromConnectionString(string connectionString, ILogger<RocketWelderClient> logger)
        {
            return new RocketWelderClient(connectionString, logger);
        }

        /// <summary>
        /// Sets the callback for frame processing.
        /// </summary>
        public void OnFrame(Action<Mat> callback)
        {
            _frameCallback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        /// <summary>
        /// Starts frame processing.
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;
            if (_frameCallback == null)
                throw new InvalidOperationException("Frame callback must be set before starting");

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Start processing based on protocol
            _processingTask = Task.Run(async () =>
            {
                try
                {
                    if (_connection.Protocol == Protocol.Shm)
                    {
                        await ProcessSharedMemoryAsync(_cancellationTokenSource.Token);
                    }
                    else if (_connection.Protocol == (Protocol.Mjpeg | Protocol.Http))
                    {
                        await ProcessMjpegHttpAsync(_cancellationTokenSource.Token);
                    }
                    else if (_connection.Protocol == (Protocol.Mjpeg | Protocol.Tcp))
                    {
                        await ProcessMjpegTcpAsync(_cancellationTokenSource.Token);
                    }
                    else
                    {
                        throw new NotSupportedException($"Protocol '{_connection.Protocol}' is not supported");
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when stopping
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in frame processing");
                    throw;
                }
            }, _cancellationTokenSource.Token);
        }

        private async Task ProcessSharedMemoryAsync(CancellationToken cancellationToken)
        {
            var bufferName = _connection.BufferName ?? "default";
            var bufferSize = _connection.BufferSize;
            var metadataSize = _connection.MetadataSize;
            
            var config = new BufferConfig(
                metadataSize: (int)metadataSize,
                payloadSize: (int)bufferSize
            );

            // Create reader - this creates the shared memory buffer
            _reader = new Reader(bufferName, config);
            
            // If duplex mode, also create a writer
            if (_connection.Mode == "duplex")
            {
                // For duplex, we'd need a separate buffer for writing back
                // For now, we'll just read
                _logger.LogWarning("Duplex mode not fully implemented yet, operating in read-only mode");
            }

            _logger.LogDebug("Created shared memory buffer: {BufferName} (size: {BufferSize}, metadata: {MetadataSize})", 
                bufferName, bufferSize, metadataSize);
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Read frame from shared memory
                    var frame = _reader.ReadFrame();
                    if (!frame.IsValid)
                    {
                        _logger.LogInformation("No valid frame read, waiting for next frame");
                        continue;
                    }
                    // Parse metadata on first frame or when not yet parsed
                    if (_videoFormat == null) 
                        ParseMetadata();


                    // Use video format from metadata, or fallback to connection parameters
                    var format = _videoFormat ?? throw new InvalidOperationException("No video format detected");


                    // Create Mat from the raw data using zero-copy pointer
                    unsafe
                    {
                        // Create Mat wrapping the shared memory directly
                        // Use Span for zero-copy access if Pointer is not available
                        fixed (byte* ptr = frame.Span)
                        {
                            using var mat = format.CreateMat(new IntPtr(ptr));

                            // Call the frame callback with zero-copy Mat
                            _frameCallback!(mat);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading from shared memory");
                    await Task.Delay(100, cancellationToken);
                }
            }
        }

        private void ParseMetadata()
        {
            try
            {
                var metadataSpan = _reader!.GetMetadata();
                if (metadataSpan.Length <= 12) return; // Need at least 8-byte prefix + 4-byte GStreamer prefix
                
                // The C# GetMetadata returns the raw metadata including the 8-byte size prefix
                // Skip the first 8 bytes (uint64_t size prefix from ZeroBuffer Writer)
                metadataSpan = metadataSpan.Slice(8);
                
                // Now read GStreamer's 4-byte size prefix (little-endian)
                uint jsonSize = metadataSpan[0] |
                                ((uint)metadataSpan[1] << 8) |
                                ((uint)metadataSpan[2] << 16) |
                                ((uint)metadataSpan[3] << 24);

                // Validate size
                if (jsonSize == 0 || jsonSize > metadataSpan.Length - 4) 
                {
                    _logger.LogWarning("Invalid JSON size: {Size} (available: {Available})", 
                        jsonSize, metadataSpan.Length - 4);
                    return;
                }

                // Parse JSON metadata (skip the 4-byte GStreamer size prefix)
                var jsonSpan = metadataSpan.Slice(4, (int)jsonSize);
                var jsonString = Encoding.UTF8.GetString(jsonSpan);
                        
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;
                        
                // Try to parse from caps string first (most complete)
                if (root.TryGetProperty("caps", out var capsElem))
                {
                    var caps = capsElem.GetString();
                    if (!string.IsNullOrEmpty(caps))
                    {
                        _videoFormat = GstCaps.Parse(caps, null);
                        _logger.LogInformation("Parsed video format from caps: {VideoFormat}", _videoFormat);
                        return;
                    }
                }
                        
                // Fallback to individual properties
                if (!root.TryGetProperty("width", out var widthElem) ||
                    !root.TryGetProperty("height", out var heightElem)) return;

                var width = widthElem.GetInt32();
                var height = heightElem.GetInt32();
                var format = root.TryGetProperty("format", out var formatElem) 
                    ? formatElem.GetString() ?? "RGB" 
                    : "RGB";
                            
                _videoFormat = GstCaps.FromSimple(width, height, format);
                            
                _logger.LogInformation("Parsed video format from properties: {VideoFormat}", _videoFormat);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse metadata, using defaults");
            }
        }

        

        private async Task ProcessMjpegHttpAsync(CancellationToken cancellationToken)
        {
            var url = $"http://{_connection.Host}:{_connection.Port ?? 80}/{_connection.Path}";
            await ProcessMjpegWithVideoCaptureAsync(url, cancellationToken);
        }

        private async Task ProcessMjpegTcpAsync(CancellationToken cancellationToken)
        {
            var url = $"tcp://{_connection.Host}:{_connection.Port ?? 8080}/{_connection.Path}";
            await ProcessMjpegWithVideoCaptureAsync(url, cancellationToken);
        }

        private async Task ProcessMjpegWithVideoCaptureAsync(string url, CancellationToken cancellationToken)
        {
            // Use Emgu CV's VideoCapture which can handle MJPEG streams directly
            using var capture = new VideoCapture(url);
            
            if (!capture.IsOpened)
            {
                throw new InvalidOperationException($"Failed to open video stream: {url}");
            }
            
            _logger.LogInformation("Opened MJPEG stream: {Url}", url);
            
            using var frame = new Mat();
            
            await Task.Run(() =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Read frame from the stream
                        if (capture.Read(frame) && !frame.IsEmpty)
                        {
                            // Process the frame
                            _frameCallback!(frame);
                        }
                        else
                        {
                            // Small delay if no frame available
                            Thread.Sleep(10);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error reading from MJPEG stream");
                        Thread.Sleep(100);
                    }
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Stops frame processing.
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cancellationTokenSource?.Cancel();
            
            try
            {
                _processingTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Cancellation is expected
            }
            
            _reader?.Dispose();
            _reader = null;
            _writer?.Dispose();
            _writer = null;
            
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _processingTask = null;
        }

        /// <summary>
        /// Gets whether the client is currently running.
        /// </summary>
        public bool IsRunning => _isRunning;

        public void Dispose()
        {
            Stop();
        }
    }

    /// <summary>
    /// Statistics for performance monitoring.
    /// </summary>
    public class Statistics
    {
        public double FramesPerSecond { get; set; }
        public long DroppedFrames { get; set; }
        public double AverageLatency { get; set; }
    }
}