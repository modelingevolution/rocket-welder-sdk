using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using RocketWelder.SDK.Transport;

namespace RocketWelder.SDK.Internal;

/// <summary>
/// Implementation of <see cref="IRocketWelderClient"/>.
/// </summary>
internal sealed class RocketWelderClientImpl : IRocketWelderClient
{
    private readonly RocketWelderClientOptions _options;
    private readonly KeyPointsSchema _keyPointsSchema = new();
    private readonly SegmentationSchema _segmentationSchema = new();

    private IKeyPointsSink? _keyPointsSink;
    private ISegmentationResultSink? _segmentationSink;
    private bool _disposed;

    public RocketWelderClientImpl(RocketWelderClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IKeyPointsSchema KeyPoints => _keyPointsSchema;
    public ISegmentationSchema Segmentation => _segmentationSchema;

    public Task StartAsync(
        Action<Mat, ISegmentationDataContext, IKeyPointsDataContext, Mat> processFrame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processFrame);

        return RunProcessingLoopAsync(
            (input, output, frameId, width, height) =>
            {
                using var keypointsContext = CreateKeyPointsContext(frameId);
                using var segmentationContext = CreateSegmentationContext(frameId, width, height);

                processFrame(input, segmentationContext, keypointsContext, output);
            },
            useKeyPoints: true,
            useSegmentation: true,
            cancellationToken);
    }

    public Task StartAsync(
        Action<Mat, IKeyPointsDataContext, Mat> processFrame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processFrame);

        return RunProcessingLoopAsync(
            (input, output, frameId, width, height) =>
            {
                using var keypointsContext = CreateKeyPointsContext(frameId);

                processFrame(input, keypointsContext, output);
            },
            useKeyPoints: true,
            useSegmentation: false,
            cancellationToken);
    }

    public Task StartAsync(
        Action<Mat, ISegmentationDataContext, Mat> processFrame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processFrame);

        return RunProcessingLoopAsync(
            (input, output, frameId, width, height) =>
            {
                using var segmentationContext = CreateSegmentationContext(frameId, width, height);

                processFrame(input, segmentationContext, output);
            },
            useKeyPoints: false,
            useSegmentation: true,
            cancellationToken);
    }

    private KeyPointsDataContext CreateKeyPointsContext(ulong frameId)
    {
        if (_keyPointsSink == null)
            throw new InvalidOperationException("KeyPoints sink not initialized");

        var writer = _keyPointsSink.CreateWriter(frameId);
        return new KeyPointsDataContext(writer, frameId);
    }

    private SegmentationDataContext CreateSegmentationContext(ulong frameId, uint width, uint height)
    {
        if (_segmentationSink == null)
            throw new InvalidOperationException("Segmentation sink not initialized");

        var writer = _segmentationSink.CreateWriter(frameId, width, height);
        return new SegmentationDataContext(writer, frameId);
    }

    private async Task RunProcessingLoopAsync(
        Action<Mat, Mat, ulong, uint, uint> processFrame,
        bool useKeyPoints,
        bool useSegmentation,
        CancellationToken cancellationToken)
    {
        // Initialize transports from strongly-typed connection strings
        if (useKeyPoints)
        {
            var cs = _options.KeyPoints;
            var frameSink = CreateFrameSink(cs);
            _keyPointsSink = new KeyPointsSink(frameSink, cs.MasterFrameInterval, ownsSink: true);
        }

        if (useSegmentation)
        {
            var cs = _options.Segmentation;
            var frameSink = CreateFrameSink(cs);
            _segmentationSink = new SegmentationResultSink(frameSink);
        }

        // Open video source
        var videoSource = GetVideoSource();
        using var capture = new VideoCapture(videoSource);
        if (!capture.IsOpened)
            throw new InvalidOperationException($"Failed to open video source: {_options.VideoSource}");

        ulong frameId = 0;
        using var inputFrame = new Mat();
        using var outputFrame = new Mat();

        while (!cancellationToken.IsCancellationRequested)
        {
            // Read frame
            if (!capture.Read(inputFrame) || inputFrame.IsEmpty)
                break;

            var width = (uint)inputFrame.Width;
            var height = (uint)inputFrame.Height;

            // Process frame
            processFrame(inputFrame, outputFrame, frameId, width, height);

            frameId++;

            // Yield to allow cancellation check
            await Task.Yield();
        }
    }

    private string GetVideoSource()
    {
        var vs = _options.VideoSource;
        return vs.SourceType switch
        {
            VideoSourceType.Camera => vs.CameraIndex?.ToString() ?? "0",
            VideoSourceType.File => vs.Path ?? throw new InvalidOperationException("File path not specified"),
            VideoSourceType.SharedMemory => vs.Path ?? throw new InvalidOperationException("Shared memory buffer not specified"),
            VideoSourceType.Rtsp => vs.Path ?? throw new InvalidOperationException("RTSP URL not specified"),
            VideoSourceType.Http => vs.Path ?? throw new InvalidOperationException("HTTP URL not specified"),
            _ => throw new NotSupportedException($"Unsupported video source type: {vs.SourceType}")
        };
    }

    private static IFrameSink CreateFrameSink(KeyPointsConnectionString cs)
        => CreateFrameSink(cs.Protocol, cs.Address);

    private static IFrameSink CreateFrameSink(SegmentationConnectionString cs)
        => CreateFrameSink(cs.Protocol, cs.Address);

    private static IFrameSink CreateFrameSink(TransportProtocol protocol, string address)
    {
        return protocol.Kind switch
        {
            TransportKind.File => new StreamFrameSink(File.Create(address)),
            TransportKind.Socket => UnixSocketFrameSink.Connect(address),
            _ => throw new NotSupportedException($"Unsupported transport protocol: {protocol}")
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _keyPointsSink?.Dispose();
        _segmentationSink?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_keyPointsSink != null)
            await _keyPointsSink.DisposeAsync().ConfigureAwait(false);

        if (_segmentationSink != null)
            await _segmentationSink.DisposeAsync().ConfigureAwait(false);
    }
}
