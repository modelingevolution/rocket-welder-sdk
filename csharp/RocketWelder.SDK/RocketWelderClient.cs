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
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics;
using ErrorEventArgs = ZeroBuffer.ErrorEventArgs;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using RocketWelder.SDK.Transport;
using RocketWelder.BinaryProtocol;

namespace RocketWelder.SDK
{
    // VarintExtensions moved to RocketWelder.SDK.Protocol package

    class SegmentationResultWriter : ISegmentationResultWriter
    {
        // Protocol (per frame): [FrameId: 8B][Width: varint][Height: varint]
        //           [classId: 1B][instanceId: 1B][pointCount: varint][points: delta+varint...]
        //           [classId: 1B][instanceId: 1B][pointCount: varint][points: delta+varint...]
        //           ...
        // Frame boundaries handled by transport layer (IFrameSink with length-prefix framing)

        private readonly ulong _frameId;
        private readonly uint _width;
        private readonly uint _height;
        private readonly IFrameSink _frameSink;
        private readonly MemoryStream _buffer = new();
        private bool _headerWritten = false;
        private bool _disposed = false;

        /// <summary>
        /// Creates a writer that writes to stream WITH varint length-prefix framing.
        /// ALL protocols use framing - this is mandatory for frame boundary detection.
        /// </summary>
        public SegmentationResultWriter(ulong frameId, uint width, uint height, Stream destination, bool leaveOpen = false)
        {
            _frameId = frameId;
            _width = width;
            _height = height;
            _frameSink = new StreamFrameSink(destination, leaveOpen);
        }

        /// <summary>
        /// Creates a writer that writes via IFrameSink with proper frame boundaries.
        /// Use this for transport-agnostic streaming (TCP, WebSocket, NNG, or file with framing).
        /// </summary>
        public SegmentationResultWriter(ulong frameId, uint width, uint height, IFrameSink frameSink)
        {
            _frameId = frameId;
            _width = width;
            _height = height;
            _frameSink = frameSink ?? throw new ArgumentNullException(nameof(frameSink));
        }

        private void EnsureHeaderWritten()
        {
            if (_headerWritten) return;

            // Write FrameId (8 bytes, explicit little-endian for cross-platform compatibility)
            Span<byte> frameIdBytes = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(frameIdBytes, _frameId);
            _buffer.Write(frameIdBytes);

            // Write Width and Height as varints
            _buffer.WriteVarint(_width);
            _buffer.WriteVarint(_height);

            _headerWritten = true;
        }

        public void Append(byte classId, byte instanceId, in ReadOnlySpan<Point> points)
        {
            EnsureHeaderWritten();

            // Write classId and instanceId (buffered for performance)
            Span<byte> header = stackalloc byte[2];
            header[0] = classId;
            header[1] = instanceId;
            _buffer.Write(header);

            // Write point count
            _buffer.WriteVarint((uint)points.Length);

            // Write points with delta encoding
            if (points.Length == 0) return;

            // First point - write absolute coordinates
            _buffer.WriteVarint(points[0].X.ZigZagEncode());
            _buffer.WriteVarint(points[0].Y.ZigZagEncode());

            // Remaining points - write deltas
            for (int i = 1; i < points.Length; i++)
            {
                int deltaX = points[i].X - points[i - 1].X;
                int deltaY = points[i].Y - points[i - 1].Y;
                _buffer.WriteVarint(deltaX.ZigZagEncode());
                _buffer.WriteVarint(deltaY.ZigZagEncode());
            }
        }

        public void Append(byte classId, byte instanceId, Point[] points)
        {
            Append(classId, instanceId, points.AsSpan());
        }

        public void Append(byte classId, byte instanceId, IEnumerable<Point> points)
        {
            // Try to avoid allocation by using span directly for known collection types
            if (points is Point[] array)
            {
                Append(classId, instanceId, array.AsSpan());
            }
            else if (points is List<Point> list)
            {
                // Zero-allocation access to List<T> internal array
                Append(classId, instanceId, CollectionsMarshal.AsSpan(list));
            }
            else
            {
                // Unavoidable allocation for arbitrary IEnumerable
                var tempArray = points.ToArray();
                Append(classId, instanceId, tempArray.AsSpan());
            }
        }

        public Task AppendAsync(byte classId, byte instanceId, Point[] points)
        {
            Append(classId, instanceId, points);
            return Task.CompletedTask;
        }

        public Task AppendAsync(byte classId, byte instanceId, IEnumerable<Point> points)
        {
            Append(classId, instanceId, points);
            return Task.CompletedTask;
        }

        public void Flush()
        {
            if (_disposed) return;

            // Ensure header is written (even if no instances appended)
            EnsureHeaderWritten();

            // Write buffered frame atomically via sink (zero-copy using GetBuffer)
            _frameSink.WriteFrame(new ReadOnlySpan<byte>(_buffer.GetBuffer(), 0, (int)_buffer.Length));
            _frameSink.Flush();
        }

        public async Task FlushAsync()
        {
            if (_disposed) return;

            // Ensure header is written (even if no instances appended)
            EnsureHeaderWritten();

            // Write buffered frame atomically via sink (zero-copy using GetBuffer)
            await _frameSink.WriteFrameAsync(new ReadOnlyMemory<byte>(_buffer.GetBuffer(), 0, (int)_buffer.Length)).ConfigureAwait(false);
            await _frameSink.FlushAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Ensure header is written (even if no instances appended)
            EnsureHeaderWritten();

            // Send complete frame atomically via sink (zero-copy using GetBuffer)
            _frameSink.WriteFrame(new ReadOnlySpan<byte>(_buffer.GetBuffer(), 0, (int)_buffer.Length));

            // Clean up buffer
            _buffer.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            // Ensure header is written (even if no instances appended)
            EnsureHeaderWritten();

            // Send complete frame atomically via sink (zero-copy using GetBuffer)
            await _frameSink.WriteFrameAsync(new ReadOnlyMemory<byte>(_buffer.GetBuffer(), 0, (int)_buffer.Length)).ConfigureAwait(false);

            // Clean up buffer
            await _buffer.DisposeAsync().ConfigureAwait(false);
        }
    }


    /// <summary>
    /// Writes segmentation results for a single frame.
    /// </summary>
    public interface ISegmentationResultWriter : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Append an instance with contour points (zero-copy, preferred).
        /// </summary>
        void Append(byte classId, byte instanceId, in ReadOnlySpan<Point> points);

        /// <summary>
        /// Append an instance with contour points (array overload).
        /// </summary>
        void Append(byte classId, byte instanceId, Point[] points);

        /// <summary>
        /// Append an instance with contour points (enumerable overload for flexibility).
        /// </summary>
        void Append(byte classId, byte instanceId, IEnumerable<Point> points);

        /// <summary>
        /// Append an instance with contour points asynchronously (array overload).
        /// </summary>
        Task AppendAsync(byte classId, byte instanceId, Point[] points);

        /// <summary>
        /// Append an instance with contour points asynchronously (enumerable overload).
        /// </summary>
        Task AppendAsync(byte classId, byte instanceId, IEnumerable<Point> points);

        /// <summary>
        /// Flush buffered data to underlying stream without disposing.
        /// </summary>
        void Flush();

        /// <summary>
        /// Flush buffered data to underlying stream asynchronously without disposing.
        /// </summary>
        Task FlushAsync();
    }


    /// <summary>
    /// [DEPRECATED] Use ISegmentationResultSink instead.
    /// Legacy factory interface for backward compatibility.
    /// </summary>
    [Obsolete("Use ISegmentationResultSink instead. This interface will be removed in a future version.")]
    public interface ISegmentationResultStorage
    {
        /// <summary>
        /// Create a writer for the current frame.
        /// </summary>
        ISegmentationResultWriter CreateWriter(ulong frameId, uint width, uint height);
    }

    /// <summary>
    /// Factory for creating segmentation result writers per frame (transport-agnostic).
    /// </summary>
    public interface ISegmentationResultSink : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Create a writer for the current frame.
        /// </summary>
        ISegmentationResultWriter CreateWriter(ulong frameId, uint width, uint height);
    }

    /// <summary>
    /// Streaming reader for segmentation results via IAsyncEnumerable.
    /// Designed for real-time streaming over TCP/WebSocket/NNG.
    /// </summary>
    public interface ISegmentationResultSource : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Stream frames as they arrive from the transport.
        /// Supports cancellation and backpressure.
        /// </summary>
        IAsyncEnumerable<SegmentationFrame> ReadFramesAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A complete segmentation frame with all instances.
    /// Non-ref struct for use with IAsyncEnumerable.
    /// </summary>
    public readonly struct SegmentationFrame
    {
        public ulong FrameId { get; }
        public uint Width { get; }
        public uint Height { get; }
        public IReadOnlyList<SegmentationInstance> Instances { get; }

        public SegmentationFrame(ulong frameId, uint width, uint height, IReadOnlyList<SegmentationInstance> instances)
        {
            FrameId = frameId;
            Width = width;
            Height = height;
            Instances = instances;
        }
    }

    /// <summary>
    /// A single instance in a segmentation frame.
    /// Contains class ID, instance ID, and contour points.
    /// </summary>
    public readonly struct SegmentationInstance
    {
        public byte ClassId { get; }
        public byte InstanceId { get; }
        public ReadOnlyMemory<Point> Points { get; }

        public SegmentationInstance(byte classId, byte instanceId, Point[] points)
        {
            ClassId = classId;
            InstanceId = instanceId;
            Points = points;
        }

        /// <summary>
        /// Converts points to normalized coordinates [0-1] range.
        /// </summary>
        public PointF[] ToNormalized(uint width, uint height)
        {
            if (width == 0 || height == 0)
                throw new ArgumentException("Width and height must be greater than zero");

            var points = Points.Span;
            var result = new PointF[points.Length];
            float widthF = width;
            float heightF = height;

            for (int i = 0; i < points.Length; i++)
            {
                result[i] = new PointF(points[i].X / widthF, points[i].Y / heightF);
            }

            return result;
        }
    }

    /// <summary>
    /// Streaming reader for segmentation results.
    /// Reads frames from IFrameSource and yields them via IAsyncEnumerable.
    /// </summary>
    public class SegmentationResultSource : ISegmentationResultSource
    {
        private readonly IFrameSource _frameSource;
        private bool _disposed;

        // Max points per instance - prevents OOM attacks
        private const int MaxPointsPerInstance = 10_000_000;

        public SegmentationResultSource(IFrameSource frameSource)
        {
            _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        }

        public async IAsyncEnumerable<SegmentationFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested && !_disposed)
            {
                // Read next frame from transport
                var frameData = await _frameSource.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                if (frameData.IsEmpty)
                    yield break;

                // Parse frame
                var frame = ParseFrame(frameData);
                yield return frame;
            }
        }

        private SegmentationFrame ParseFrame(ReadOnlyMemory<byte> frameData)
        {
            // Zero-copy: get underlying array segment without allocation
            if (!MemoryMarshal.TryGetArray(frameData, out var segment))
                throw new InvalidOperationException("Cannot get array segment from memory");

            using var stream = new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false);

            // Read header: [FrameId: 8B LE][Width: varint][Height: varint]
            Span<byte> frameIdBytes = stackalloc byte[8];
            if (stream.Read(frameIdBytes) != 8)
                throw new EndOfStreamException("Failed to read FrameId");

            ulong frameId = BinaryPrimitives.ReadUInt64LittleEndian(frameIdBytes);
            uint width = stream.ReadVarint();
            uint height = stream.ReadVarint();

            // Read instances until end of frame
            var instances = new List<SegmentationInstance>();

            while (stream.Position < stream.Length)
            {
                // Read instance header: [classId: 1B][instanceId: 1B]
                int classIdByte = stream.ReadByte();
                if (classIdByte == -1) break;

                int instanceIdByte = stream.ReadByte();
                if (instanceIdByte == -1)
                    throw new EndOfStreamException("Unexpected end of stream reading instanceId");

                byte classId = (byte)classIdByte;
                byte instanceId = (byte)instanceIdByte;

                // Read point count
                uint pointCount = stream.ReadVarint();
                if (pointCount > MaxPointsPerInstance)
                    throw new InvalidDataException($"Point count {pointCount} exceeds maximum {MaxPointsPerInstance}");

                // Read points
                var points = new Point[pointCount];
                if (pointCount > 0)
                {
                    // First point (absolute, zigzag encoded)
                    int x = stream.ReadVarint().ZigZagDecode();
                    int y = stream.ReadVarint().ZigZagDecode();
                    points[0] = new Point(x, y);

                    // Remaining points (delta encoded)
                    for (int i = 1; i < pointCount; i++)
                    {
                        int deltaX = stream.ReadVarint().ZigZagDecode();
                        int deltaY = stream.ReadVarint().ZigZagDecode();
                        x += deltaX;
                        y += deltaY;
                        points[i] = new Point(x, y);
                    }
                }

                instances.Add(new SegmentationInstance(classId, instanceId, points));
            }

            return new SegmentationFrame(frameId, width, height, instances);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _frameSource.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await _frameSource.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Factory for creating segmentation result writers (transport-agnostic).
    /// </summary>
    public class SegmentationResultSink : ISegmentationResultSink
    {
        private readonly IFrameSink _frameSink;
        private bool _disposed;

        public SegmentationResultSink(IFrameSink frameSink)
        {
            _frameSink = frameSink ?? throw new ArgumentNullException(nameof(frameSink));
        }

        public ISegmentationResultWriter CreateWriter(ulong frameId, uint width, uint height)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SegmentationResultSink));

            return new SegmentationResultWriter(frameId, width, height, _frameSink);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _frameSink.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await _frameSink.DisposeAsync();
        }
    }

    // NO MEMORY COPY! NO FUCKING MEMORY COPY!
    // NO MEMORY ALLOCATIONS IN THE MAIN LOOP! NO FUCKING MEMORY ALLOCATIONS!
    // NO BRANCHING IN THE MAIN LOOP! NO FUCKING CONDITIONAL BRANCHING CHECKS! (Action<Mat> or Action<Mat, Mat>)
    interface IController
    {
        bool IsRunning { get; }
        GstMetadata? GetMetadata();
        event Action<IController, Exception>? OnError;
        void Start(Action<FrameMetadata, Mat, Mat> onFrame, CancellationToken cancellationToken = default);
        void Start(Action<Mat, Mat> onFrame, CancellationToken cancellationToken = default);
        void Start(Action<Mat> onFrame, CancellationToken cancellationToken = default);
        void Stop(CancellationToken cancellationToken = default);
        void Dispose();
    }

    /// <summary>
    /// No-op segmentation writer used when GstCaps are not yet available.
    /// All operations are ignored silently.
    /// </summary>
    internal sealed class NoOpSegmentationWriter : ISegmentationResultWriter
    {
        public static readonly NoOpSegmentationWriter Instance = new();
        private NoOpSegmentationWriter() { }

        public void Append(byte classId, byte instanceId, in ReadOnlySpan<Point> points) { }
        public void Append(byte classId, byte instanceId, Point[] points) { }
        public void Append(byte classId, byte instanceId, IEnumerable<Point> points) { }
        public Task AppendAsync(byte classId, byte instanceId, Point[] points) => Task.CompletedTask;
        public Task AppendAsync(byte classId, byte instanceId, IEnumerable<Point> points) => Task.CompletedTask;
        public void Flush() { }
        public Task FlushAsync() => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// No-op keypoints writer used when GstCaps are not yet available.
    /// All operations are ignored silently.
    /// </summary>
    internal sealed class NoOpKeyPointsWriter : IKeyPointsWriter
    {
        public static readonly NoOpKeyPointsWriter Instance = new();
        private NoOpKeyPointsWriter() { }

        public void Append(int keypointId, int x, int y, float confidence) { }
        public void Append(int keypointId, Point p, float confidence) { }
        public Task AppendAsync(int keypointId, int x, int y, float confidence) => Task.CompletedTask;
        public Task AppendAsync(int keypointId, Point p, float confidence) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
    /// Configuration keys for NNG Pub/Sub URLs used by RocketWelderClient.
    /// These URLs are used by rocket-welder2 to connect to the Python AI container's output channels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>NNG IPC URL Format:</b> <c>ipc:///tmp/{container-name}-{channel}.ipc</c>
    /// </para>
    /// <para>
    /// <b>Example URLs:</b>
    /// <list type="bullet">
    ///   <item><description>Segmentation: <c>ipc:///tmp/ai-container-segmentation.ipc</c></description></item>
    ///   <item><description>KeyPoints: <c>ipc:///tmp/ai-container-keypoints.ipc</c></description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Configuration in appsettings.json:</b>
    /// <code>
    /// {
    ///   "RocketWelder": {
    ///     "ConnectionString": "shm://video-buffer?mode=duplex",
    ///     "SegmentationSinkUrl": "ipc:///tmp/ai-segmentation.ipc",
    ///     "KeyPointsSinkUrl": "ipc:///tmp/ai-keypoints.ipc"
    ///   }
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// <b>Environment Variables (alternative):</b>
    /// <list type="bullet">
    ///   <item><description><c>SEGMENTATION_SINK_URL</c></description></item>
    ///   <item><description><c>KEYPOINTS_SINK_URL</c></description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public static class RocketWelderConfigKeys
    {
        /// <summary>
        /// Configuration key for the segmentation results NNG Pub URL.
        /// The Python AI container publishes segmentation results to this URL.
        /// rocket-welder2 subscribes to receive the results.
        /// </summary>
        public const string SegmentationSinkUrl = "RocketWelder:SegmentationSinkUrl";

        /// <summary>
        /// Configuration key for the keypoints NNG Pub URL.
        /// The Python AI container publishes keypoints to this URL.
        /// rocket-welder2 subscribes to receive the results.
        /// </summary>
        public const string KeyPointsSinkUrl = "RocketWelder:KeyPointsSinkUrl";

        /// <summary>
        /// Environment variable name for segmentation sink URL (alternative to config).
        /// </summary>
        public const string SegmentationSinkUrlEnv = "SEGMENTATION_SINK_URL";

        /// <summary>
        /// Environment variable name for keypoints sink URL (alternative to config).
        /// </summary>
        public const string KeyPointsSinkUrlEnv = "KEYPOINTS_SINK_URL";
    }

    /// <summary>
    /// Main client for connecting to RocketWelder video streams.
    /// Supports multiple protocols: ZeroBuffer (shared memory), MJPEG over HTTP, and MJPEG over TCP.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>NNG Pub/Sub Integration:</b>
    /// When using the Start overload with ISegmentationResultWriter and IKeyPointsWriter,
    /// the client creates NNG Publisher sinks for streaming AI results.
    /// </para>
    /// <para>
    /// <b>Configuration:</b> Set sink URLs via IConfiguration or environment variables:
    /// <list type="bullet">
    ///   <item><description><c>RocketWelder:SegmentationSinkUrl</c> or <c>SEGMENTATION_SINK_URL</c></description></item>
    ///   <item><description><c>RocketWelder:KeyPointsSinkUrl</c> or <c>KEYPOINTS_SINK_URL</c></description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public class RocketWelderClient : IDisposable
    {
        private readonly IController _controller;
        private readonly ILogger<RocketWelderClient> _logger;
        private readonly IConfiguration? _configuration;
        private readonly ILoggerFactory? _loggerFactory;

        // Preview support
        private readonly bool _previewEnabled;
        private readonly Channel<Mat> _previewChannel;
        private readonly string _previewWindowName = "RocketWelder Preview";
        private Action<Mat>? _originalOneWayCallback;
        private Action<Mat, Mat>? _originalDuplexCallback;

        // NNG Sinks for AI output (lazily created when needed)
        private ISegmentationResultSink? _segmentationSink;
        private IKeyPointsSink? _keyPointsSink;

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


        private RocketWelderClient(ConnectionString connection, ILoggerFactory? loggerFactory = null, IConfiguration? configuration = null)
        {
            Connection = connection;
            _configuration = configuration;
            _loggerFactory = loggerFactory;
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

        /// <summary>
        /// Gets the segmentation sink URL from configuration or environment.
        /// </summary>
        private string? GetSegmentationSinkUrl()
        {
            return _configuration?[RocketWelderConfigKeys.SegmentationSinkUrl]
                ?? Environment.GetEnvironmentVariable(RocketWelderConfigKeys.SegmentationSinkUrlEnv);
        }

        /// <summary>
        /// Gets the keypoints sink URL from configuration or environment.
        /// </summary>
        private string? GetKeyPointsSinkUrl()
        {
            return _configuration?[RocketWelderConfigKeys.KeyPointsSinkUrl]
                ?? Environment.GetEnvironmentVariable(RocketWelderConfigKeys.KeyPointsSinkUrlEnv);
        }

        /// <summary>
        /// Logs the NNG sink URL configuration at startup for debugging.
        /// </summary>
        private void LogNngConfiguration()
        {
            var segUrl = GetSegmentationSinkUrl();
            var kpUrl = GetKeyPointsSinkUrl();

            _logger.LogInformation(
                "NNG sink URLs configured: seg={SegUrl}, kp={KpUrl}",
                segUrl ?? "(not configured)",
                kpUrl ?? "(not configured)");
        }

        /// <summary>
        /// Creates or returns the segmentation result sink.
        /// </summary>
        private ISegmentationResultSink GetOrCreateSegmentationSink()
        {
            if (_segmentationSink != null)
                return _segmentationSink;

            var url = GetSegmentationSinkUrl();
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException(
                    $"Segmentation sink URL not configured. Set '{RocketWelderConfigKeys.SegmentationSinkUrl}' in configuration " +
                    $"or '{RocketWelderConfigKeys.SegmentationSinkUrlEnv}' environment variable. " +
                    $"Example: ipc:///tmp/ai-segmentation.ipc");

            _logger.LogInformation("Creating NNG Publisher for segmentation at: {Url}", url);
            var frameSink = Transport.NngFrameSink.CreatePublisher(url);
            _segmentationSink = new SegmentationResultSink(frameSink);
            return _segmentationSink;
        }

        /// <summary>
        /// Creates or returns the keypoints sink.
        /// </summary>
        private IKeyPointsSink GetOrCreateKeyPointsSink()
        {
            if (_keyPointsSink != null)
                return _keyPointsSink;

            var url = GetKeyPointsSinkUrl();
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException(
                    $"KeyPoints sink URL not configured. Set '{RocketWelderConfigKeys.KeyPointsSinkUrl}' in configuration " +
                    $"or '{RocketWelderConfigKeys.KeyPointsSinkUrlEnv}' environment variable. " +
                    $"Example: ipc:///tmp/ai-keypoints.ipc");

            _logger.LogInformation("Creating NNG Publisher for keypoints at: {Url}", url);
            var frameSink = Transport.NngFrameSink.CreatePublisher(url);
            _keyPointsSink = new KeyPointsSink(frameSink, masterFrameInterval: 300, ownsSink: true);
            return _keyPointsSink;
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
        /// Also reads NNG sink URLs from configuration for AI output streaming.
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
            return new RocketWelderClient(connection, loggerFactory, configuration);
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
        /// Starts receiving frames with segmentation and keypoints output support.
        /// Creates NNG Publishers for streaming AI results to rocket-welder2.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This overload enables AI models to write segmentation results and keypoints
        /// that are automatically published via NNG Pub/Sub to rocket-welder2 for storage
        /// and comparison.
        /// </para>
        /// <para>
        /// <b>Configuration Required:</b>
        /// <list type="bullet">
        ///   <item><description><c>RocketWelder:SegmentationSinkUrl</c> or <c>SEGMENTATION_SINK_URL</c></description></item>
        ///   <item><description><c>RocketWelder:KeyPointsSinkUrl</c> or <c>KEYPOINTS_SINK_URL</c></description></item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>Example:</b>
        /// <code>
        /// client.Start((input, segWriter, kpWriter, output) =>
        /// {
        ///     // Run AI inference
        ///     var result = aiModel.Infer(input);
        ///
        ///     // Write segmentation results
        ///     foreach (var instance in result.Instances)
        ///         segWriter.Append(instance.ClassId, instance.InstanceId, instance.ContourPoints);
        ///
        ///     // Write keypoints
        ///     foreach (var kp in result.KeyPoints)
        ///         kpWriter.Append(kp.Id, kp.X, kp.Y, kp.Confidence);
        ///
        ///     // Draw output
        ///     result.DrawTo(output);
        /// });
        /// </code>
        /// </para>
        /// </remarks>
        /// <param name="onFrame">Callback receiving input Mat, segmentation writer, keypoints writer, and output Mat</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        public void Start(Action<Mat, ISegmentationResultWriter, IKeyPointsWriter, Mat> onFrame, CancellationToken cancellationToken = default)
        {
            if (IsRunning)
                throw new InvalidOperationException("Client is already running");

            try
            {
                _logger.LogInformation("Starting RocketWelder client with AI output support: {Connection}", Connection);

                // Log NNG sink URL configuration at startup (for debugging)
                LogNngConfiguration();

                // Initialize sinks (will throw if not configured)
                var segSink = GetOrCreateSegmentationSink();
                var kpSink = GetOrCreateKeyPointsSink();

                // Wrapper callback that creates per-frame writers
                // Controller provides FrameMetadata (frame number, timestamp) and Mats
                // We create writers from sinks and pass to user callback
                _controller.Start((FrameMetadata frameMetadata, Mat inputMat, Mat outputMat) =>
                {
                    // Get caps from controller metadata (width/height for segmentation)
                    var caps = _controller.GetMetadata()?.Caps;
                    if (caps == null)
                    {
                        _logger.LogWarning("GstCaps not available for frame {FrameNumber}, skipping AI output", frameMetadata.FrameNumber);
                        onFrame(inputMat, NoOpSegmentationWriter.Instance, NoOpKeyPointsWriter.Instance, outputMat);
                        return;
                    }

                    // Create per-frame writers from sinks
                    using var segWriter = segSink.CreateWriter(frameMetadata.FrameNumber, (uint)caps.Value.Width, (uint)caps.Value.Height);
                    using var kpWriter = kpSink.CreateWriter(frameMetadata.FrameNumber);

                    // Call user callback with writers
                    onFrame(inputMat, segWriter, kpWriter, outputMat);

                    // Writers auto-flush on dispose
                }, cancellationToken);

                Started?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start RocketWelder client with AI output support");
                OnError?.Invoke(this, new ErrorEventArgs(ex));
                throw;
            }
        }

        /// <summary>
        /// Gets the segmentation sink for external use (e.g., custom frame processing).
        /// Returns null if not configured.
        /// </summary>
        public ISegmentationResultSink? SegmentationSink => _segmentationSink;

        /// <summary>
        /// Gets the keypoints sink for external use (e.g., custom frame processing).
        /// Returns null if not configured.
        /// </summary>
        public IKeyPointsSink? KeyPointsSink => _keyPointsSink;

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

            // Dispose NNG sinks
            if (_segmentationSink != null)
            {
                _segmentationSink.Dispose();
                _segmentationSink = null;
            }

            if (_keyPointsSink != null)
            {
                _keyPointsSink.Dispose();
                _keyPointsSink = null;
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