using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using RocketWelder.SDK.Transport;

namespace RocketWelder.SDK.HighLevel.Internal;

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
                var keypointsContext = CreateKeyPointsContext(frameId);
                var segmentationContext = CreateSegmentationContext(frameId, width, height);

                try
                {
                    processFrame(input, segmentationContext, keypointsContext, output);

                    // Auto-commit both contexts
                    keypointsContext.Commit();
                    segmentationContext.Commit();
                }
                catch
                {
                    // On error, still try to clean up
                    throw;
                }
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
                var keypointsContext = CreateKeyPointsContext(frameId);

                try
                {
                    processFrame(input, keypointsContext, output);
                    keypointsContext.Commit();
                }
                catch
                {
                    throw;
                }
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
                var segmentationContext = CreateSegmentationContext(frameId, width, height);

                try
                {
                    processFrame(input, segmentationContext, output);
                    segmentationContext.Commit();
                }
                catch
                {
                    throw;
                }
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
        // Initialize transports
        if (useKeyPoints)
        {
            var keyPointsFrameSink = CreateFrameSink(_options.KeyPointsEndpoint);
            _keyPointsSink = new KeyPointsSink(keyPointsFrameSink, _options.MasterFrameInterval, ownsSink: true);
        }

        if (useSegmentation)
        {
            var segmentationFrameSink = CreateFrameSink(_options.SegmentationEndpoint);
            _segmentationSink = new SegmentationResultSink(segmentationFrameSink);
        }

        // Open video source
        using var capture = new VideoCapture(_options.VideoSource);
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

    private IFrameSink CreateFrameSink(string endpoint)
    {
        // Parse endpoint and create appropriate transport
        if (endpoint.StartsWith("ipc://") || endpoint.StartsWith("tcp://"))
        {
            // NNG transport
            return NngFrameSink.CreatePusher(endpoint);
        }
        else if (endpoint.StartsWith("file://"))
        {
            // File transport
            var path = endpoint.Substring("file://".Length);
            var stream = File.Create(path);
            return new StreamFrameSink(stream);
        }
        else if (File.Exists(endpoint) || !endpoint.Contains("://"))
        {
            // Assume file path
            var stream = File.Create(endpoint);
            return new StreamFrameSink(stream);
        }
        else
        {
            throw new ArgumentException($"Unsupported endpoint format: {endpoint}");
        }
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
