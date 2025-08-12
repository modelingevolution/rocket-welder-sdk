using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using ModelingEvolution.ZeroBuffer;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Net.Sockets;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace RocketWelder.SDK
{
    public class RocketWelderClient : IDisposable
    {
        private readonly ConnectionString _connection;
        private Action<Mat>? _frameCallback;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _processingTask;
        private bool _isRunning;
        private bool _disposed;
        
        // ZeroBuffer components
        private IBufferReader? _reader;
        private IDuplexChannel? _duplexChannel;
        
        // Network components
        private HttpClient? _httpClient;
        private TcpClient? _tcpClient;
        private Stream? _networkStream;

        private RocketWelderClient(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentNullException(nameof(connectionString));
            
            _connection = ConnectionString.Parse(connectionString);
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

            return new RocketWelderClient(connectionString ?? "shm://default");
        }

        /// <summary>
        /// Creates a client from IConfiguration.
        /// Looks for "RocketWelder:ConnectionString" in configuration.
        /// </summary>
        public static RocketWelderClient From(IConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            // Try to get connection string from different configuration sources
            string? connectionString = 
                configuration["RocketWelder:ConnectionString"] ??
                configuration["ConnectionString"] ??
                configuration.GetConnectionString("RocketWelder");

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

            return new RocketWelderClient(connectionString ?? "shm://default");
        }

        /// <summary>
        /// Creates a client from environment variable CONNECTION_STRING.
        /// </summary>
        public static RocketWelderClient FromEnvironment()
        {
            string? connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");
            return new RocketWelderClient(connectionString ?? "shm://default");
        }

        /// <summary>
        /// Creates a client from a specific connection string.
        /// </summary>
        public static RocketWelderClient FromConnectionString(string connectionString)
        {
            return new RocketWelderClient(connectionString);
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
                    Console.Error.WriteLine($"Error in frame processing: {ex.Message}");
                    throw;
                }
            }, _cancellationTokenSource.Token);
        }

        private async Task ProcessSharedMemoryAsync(CancellationToken cancellationToken)
        {
            // Initialize ZeroBuffer for shared memory
            var bufferName = _connection.BufferName ?? "default";
            var bufferSize = _connection.BufferSize;
            var metadataSize = _connection.MetadataSize;
            
            // Create buffer configuration
            var config = new BufferConfiguration
            {
                Name = bufferName,
                BufferSize = bufferSize,
                MetadataSize = metadataSize
            };

            // Create reader based on mode
            if (_connection.Mode == "duplex")
            {
                _duplexChannel = BufferFactory.CreateDuplexChannel(config);
                _reader = _duplexChannel.Reader;
            }
            else
            {
                _reader = BufferFactory.CreateReader(config);
            }

            // Process frames
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Read frame from shared memory
                    var frameData = await _reader.ReadAsync(cancellationToken);
                    if (frameData != null && frameData.Length > 0)
                    {
                        // Decode frame data to Mat
                        // Assuming raw BGR format for now
                        // First 8 bytes contain width and height (4 bytes each)
                        if (frameData.Length > 8)
                        {
                            int width = BitConverter.ToInt32(frameData, 0);
                            int height = BitConverter.ToInt32(frameData, 4);
                            int pixelDataSize = width * height * 3; // BGR
                            
                            if (frameData.Length >= 8 + pixelDataSize)
                            {
                                // Create Mat from pixel data
                                using var frame = new Mat(height, width, MatType.CV_8UC3);
                                Marshal.Copy(frameData, 8, frame.Data, pixelDataSize);
                                
                                // Invoke callback
                                _frameCallback!(frame);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error reading frame: {ex.Message}");
                    await Task.Delay(100, cancellationToken);
                }
            }
        }

        private async Task ProcessMjpegHttpAsync(CancellationToken cancellationToken)
        {
            _httpClient = new HttpClient();
            var url = $"http://{_connection.Host}:{_connection.Port ?? 80}/{_connection.Path}";
            
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            using var stream = await response.Content.ReadAsStreamAsync();
            await ProcessMjpegStreamAsync(stream, cancellationToken);
        }

        private async Task ProcessMjpegTcpAsync(CancellationToken cancellationToken)
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(_connection.Host!, _connection.Port ?? 8080);
            _networkStream = _tcpClient.GetStream();
            
            await ProcessMjpegStreamAsync(_networkStream, cancellationToken);
        }

        private async Task ProcessMjpegStreamAsync(Stream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[1024 * 1024]; // 1MB buffer for JPEG frames
            var frameBuffer = new MemoryStream();
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0) break;
                    
                    // Simple MJPEG parsing - look for JPEG markers
                    for (int i = 0; i < bytesRead - 1; i++)
                    {
                        if (buffer[i] == 0xFF && buffer[i + 1] == 0xD8) // Start of JPEG
                        {
                            frameBuffer.SetLength(0);
                            frameBuffer.Position = 0;
                        }
                        
                        frameBuffer.WriteByte(buffer[i]);
                        
                        if (buffer[i] == 0xFF && buffer[i + 1] == 0xD9) // End of JPEG
                        {
                            frameBuffer.WriteByte(buffer[i + 1]);
                            i++; // Skip the next byte
                            
                            // Decode JPEG frame
                            var jpegData = frameBuffer.ToArray();
                            using var frame = Cv2.ImDecode(jpegData, ImreadModes.Color);
                            if (!frame.Empty())
                            {
                                _frameCallback!(frame);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error processing MJPEG stream: {ex.Message}");
                    await Task.Delay(100, cancellationToken);
                }
            }
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
            
            // Clean up resources
            _reader?.Dispose();
            _reader = null;
            _duplexChannel?.Dispose();
            _duplexChannel = null;
            _httpClient?.Dispose();
            _httpClient = null;
            _networkStream?.Dispose();
            _networkStream = null;
            _tcpClient?.Dispose();
            _tcpClient = null;
            
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