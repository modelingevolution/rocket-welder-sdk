using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Collections.Concurrent;
using Emgu.CV;
using ZeroBuffer;
using ZeroBuffer.DuplexChannel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text;
using System.Net.Http;
using System.IO;
using System.Net.Sockets;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using ErrorEventArgs = ZeroBuffer.ErrorEventArgs;


namespace RocketWelder.SDK
{
    // NO MEMORY COPY! NO FUCKING MEMORY COPY!
    // NO MEMORY ALLOCATIONS IN THE MAIN LOOP! NO FUCKING MEMORY ALLOCATIONS!
    // NO BRANCHING IN THE MAIN LOOP! NO FUCKING CONDITIONAL BRANCHING CHECKS! (Action<Mat> or Action<Mat, Mat>)
    interface IController
    {
        bool IsRunning { get; }
        GstMetadata? GetMetadata();
        event Action<IController, Exception>? OnError;
        void Start(Action<Mat, Mat> onFrame, CancellationToken cancellationToken = default);
        void Start(Action<Mat> onFrame, CancellationToken cancellationToken = default);
        void Stop(CancellationToken cancellationToken = default);
        void Dispose();
    }
    internal static class ControllerFactory
    {
        public static IController Create(in ConnectionString cs, ILoggerFactory? loggerFactory = null)
        {
            return cs.Protocol switch
            {
                Protocol.Shm when cs.ConnectionMode == ConnectionMode.Duplex => new DuplexShmController(cs, loggerFactory),
                Protocol.Shm when cs.ConnectionMode == ConnectionMode.OneWay => new OneWayShmController(cs, loggerFactory),
                Protocol.File => new OpenCvController(cs, loggerFactory),
                var p when p.HasFlag(Protocol.Mjpeg) => new OpenCvController(cs, loggerFactory),
                _ => throw new NotSupportedException($"Protocol {cs.Protocol} with mode {cs.ConnectionMode} is not supported")
            };
        }
    }

    /// <summary>
    /// Main client for connecting to RocketWelder video streams.
    /// Supports multiple protocols: ZeroBuffer (shared memory), MJPEG over HTTP, and MJPEG over TCP.
    /// </summary>
    public class RocketWelderClient : IDisposable
    {
        private readonly IController _controller;
        private readonly ILogger<RocketWelderClient> _logger;

        // Preview support
        private readonly bool _previewEnabled;
        private readonly Channel<Mat> _previewChannel;
        private readonly string _previewWindowName = "RocketWelder Preview";
        private Action<Mat>? _originalOneWayCallback;
        private Action<Mat, Mat>? _originalDuplexCallback;

        /// <summary>
        /// Gets the connection configuration.
        /// </summary>
        public ConnectionString Connection { get; }

        /// <summary>
        /// Gets whether the client is currently running.
        /// </summary>
        public bool IsRunning => _controller?.IsRunning ?? false;

        /// <summary>
        /// Gets the metadata from the stream (if available).
        /// </summary>
        public GstMetadata? Metadata => _controller.GetMetadata();
        
        /// <summary>
        /// Raised when the client has successfully started.
        /// </summary>
        public event EventHandler? Started;
        
        /// <summary>
        /// Raised when the client has stopped.
        /// </summary>
        public event EventHandler? Stopped;
        
        /// <summary>
        /// Raised when the client encounters an error.
        /// </summary>
        public event EventHandler<ErrorEventArgs>? OnError;


        private RocketWelderClient(ConnectionString connection, ILoggerFactory? loggerFactory = null)
        {
            Connection = connection;
            var factory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = factory.CreateLogger<RocketWelderClient>();
            _controller = ControllerFactory.Create(connection, loggerFactory);

            // Parse preview parameter
            _previewEnabled = connection.Parameters.TryGetValue("preview", out var preview) &&
                              preview.Equals("true", StringComparison.OrdinalIgnoreCase);

            // Create preview channel with bounded capacity
            _previewChannel = Channel.CreateBounded<Mat>(new BoundedChannelOptions(2)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });

            // Subscribe to controller errors
            _controller.OnError += OnControllerError;
        }
        
        private void OnControllerError(IController controller, Exception exception)
        {
            // All exceptions are terminal for streaming
            OnError?.Invoke(this, new ErrorEventArgs(exception));
            
            // Raise Stopped event if controller is no longer running
            if (!controller.IsRunning)
            {
                Stopped?.Invoke(this, EventArgs.Empty);
            }
        }
        
        
        
        /// <summary>
        /// Creates a client from command line arguments and environment variables.
        /// Environment variable CONNECTION_STRING is checked first, then overridden by args.
        /// </summary>
        public static RocketWelderClient From(string[] args)
        {
            // Command-line arguments only, no environment variables
            if (args == null || args.Length == 0)
                throw new ArgumentException("No command line arguments provided");
                
            string? connectionString = null;
            foreach (var arg in args)
            {
                if (arg.Contains("://"))
                {
                    connectionString = arg;
                    break;
                }
            }
            
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("No connection string found in command line arguments");
                
            var connection = ConnectionString.Parse(connectionString);
            return new RocketWelderClient(connection);
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
        /// Creates a client from IConfiguration with logger factory.
        /// Looks for "RocketWelder:ConnectionString" in configuration.
        /// </summary>
        public static RocketWelderClient From(IConfiguration configuration, ILoggerFactory? loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            
            // Try to get connection string from configuration
            string? connectionString = 
                configuration["CONNECTION_STRING"] ??
                configuration["RocketWelder:ConnectionString"] ??
                configuration["ConnectionString"] ??
                configuration.GetConnectionString("RocketWelder");
                
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("No connection string found in configuration");
                
            var connection = ConnectionString.Parse(connectionString);
            return new RocketWelderClient(connection, loggerFactory);
        }
        
        /// <summary>
        /// Creates a client from environment variable CONNECTION_STRING.
        /// </summary>
        public static RocketWelderClient FromEnvironment()
        {
            return FromEnvironment(null);
        }
        
        /// <summary>
        /// Creates a client from environment variable CONNECTION_STRING with logger factory.
        /// </summary>
        public static RocketWelderClient FromEnvironment(ILoggerFactory? loggerFactory)
        {
            string? connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("CONNECTION_STRING environment variable not set");
                
            var connection = ConnectionString.Parse(connectionString);
            return new RocketWelderClient(connection, loggerFactory);
        }
        
        /// <summary>
        /// Creates a client from a specific connection string.
        /// </summary>
        public static RocketWelderClient FromConnectionString(string connectionString)
        {
            return FromConnectionString(connectionString, null);
        }
        
        /// <summary>
        /// Creates a client from a specific connection string with logger factory.
        /// </summary>
        public static RocketWelderClient FromConnectionString(string connectionString, ILoggerFactory? loggerFactory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            var connection = ConnectionString.Parse(connectionString);
            return new RocketWelderClient(connection, loggerFactory);
        }


        /// <summary>
        /// Starts receiving frames from the video stream.
        /// </summary>
        public void Start(Action<Mat, Mat> onFrame, CancellationToken cancellationToken = default)
        {
            if (IsRunning)
                throw new InvalidOperationException("Client is already running");

            try
            {
                _logger.LogInformation("Starting RocketWelder client with connection: {Connection}", Connection);

                // If preview is enabled, wrap the callback to capture frames
                if (_previewEnabled)
                {
                    _originalDuplexCallback = onFrame;
                    Action<Mat, Mat> previewWrapper = (input, output) =>
                    {
                        // Call original callback
                        onFrame(input, output);
                        // Queue the OUTPUT frame for preview
                        _previewChannel.Writer.TryWrite(output.Clone());
                    };
                    _controller.Start(previewWrapper, cancellationToken);
                }
                else
                {
                    _controller.Start(onFrame, cancellationToken);
                }

                Started?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start RocketWelder client");
                OnError?.Invoke(this, new ErrorEventArgs(ex));
                throw;
            }
        }

        /// <summary>
        /// Starts receiving frames from the video stream.
        /// </summary>
        public void Start(Action<Mat> onFrame, CancellationToken cancellationToken = default)
        {
            if (IsRunning)
                throw new InvalidOperationException("Client is already running");

            try
            {
                _logger.LogInformation("Starting RocketWelder client with connection: {Connection}", Connection);

                // If preview is enabled, wrap the callback to capture frames
                if (_previewEnabled)
                {
                    _originalOneWayCallback = onFrame;
                    Action<Mat> previewWrapper = (frame) =>
                    {
                        // Call original callback
                        onFrame(frame);
                        // Queue frame for preview
                        _previewChannel.Writer.TryWrite(frame.Clone());
                    };
                    _controller.Start(previewWrapper, cancellationToken);
                }
                else
                {
                    _controller.Start(onFrame, cancellationToken);
                }

                Started?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start RocketWelder client");
                OnError?.Invoke(this, new ErrorEventArgs(ex));
                throw;
            }
        }
        
        /// <summary>
        /// Stops receiving frames and disconnects from the stream.
        /// </summary>
        public void Stop(CancellationToken cancellationToken = default)
        {
            if (!IsRunning)
                return;

            try
            {
                _logger.LogInformation("Stopping RocketWelder client");
                _controller.Stop(cancellationToken);

                // Signal preview to stop if enabled
                if (_previewEnabled)
                {
                    _previewChannel.Writer.TryComplete();
                }

                Stopped?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping RocketWelder client");
                OnError?.Invoke(this, new ErrorEventArgs(ex));
                throw;
            }
        }

        /// <summary>
        /// Display preview frames in a window (main thread only).
        /// - If preview=true: blocks and displays frames until stopped or 'q' pressed
        /// - If preview=false or not set: returns immediately
        /// </summary>
        public void Show(CancellationToken cancellationToken = default)
        {
            if (!_previewEnabled)
            {
                // No preview requested, return immediately
                return;
            }

            _logger.LogInformation("Starting preview display in main thread");

            bool windowCreated = false;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Try to read frame with timeout
                    try
                    {
                        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        cts.CancelAfter(100);

                        if (_previewChannel.Reader.TryRead(out var frame))
                        {
                            if (frame != null && !frame.IsEmpty)
                            {
                                // Create window on first frame
                                if (!windowCreated)
                                {
                                    CvInvoke.NamedWindow(_previewWindowName, Emgu.CV.CvEnum.WindowFlags.AutoSize);
                                    _logger.LogInformation("Preview window created for {Width}x{Height} video", frame.Width, frame.Height);
                                    windowCreated = true;
                                }

                                // Display frame
                                CvInvoke.Imshow(_previewWindowName, frame);
                                frame.Dispose(); // Clean up the cloned frame

                                // Process window events and check for 'q' key
                                var key = CvInvoke.WaitKey(1);
                                if (key == 'q' || key == 'Q')
                                {
                                    _logger.LogInformation("User pressed 'q', stopping preview");
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // No frame available, check if still running
                            if (!IsRunning)
                                break;

                            // Process window events even without new frame
                            var key = CvInvoke.WaitKey(1);
                            if (key == 'q' || key == 'Q')
                            {
                                _logger.LogInformation("User pressed 'q', stopping preview");
                                break;
                            }

                            // Small delay to avoid busy loop
                            Thread.Sleep(10);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            finally
            {
                // Clean up window
                CvInvoke.DestroyWindow(_previewWindowName);
                CvInvoke.WaitKey(1); // Process pending events
                _logger.LogInformation("Preview display stopped");
            }
        }
        
        public void Dispose()
        {
            if (IsRunning)
            {
                Stop();
            }
            
            if (_controller != null)
            {
                _controller.OnError -= OnControllerError;
                _controller.Dispose();
            }
            
            _logger.LogDebug("Disposed RocketWelder client");
        }
    }
}