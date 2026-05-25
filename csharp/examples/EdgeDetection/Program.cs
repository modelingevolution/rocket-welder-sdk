using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RocketWelder.SDK;
using RocketWelder.SDK.Graphics;
using static RocketWelder.SDK.RocketWelderClient;
using ErrorEventArgs = ZeroBuffer.ErrorEventArgs;

/// <summary>
/// Edge-detection sink example for the adaptive-points pipeline.
///
/// Pipeline: BGR frame → grayscale → Canny → external contours → area filter →
///           cornerSubPix vertex refinement → SegmentationInstanceF emission.
///
/// Wire-format note (see README.md "Precision floor"):
/// The SDK's ISegmentationResultWriter.Append() accepts System.Drawing.Point[] (int) and
/// the wire protocol zig-zag-varint-encodes integer pixel coordinates. cornerSubPix's
/// sub-pixel output is rounded at the writer boundary; the float precision is retained
/// locally only as the input to the contour-quality confidence metric.
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("============================================");
        Console.WriteLine("RocketWelder SDK Edge Detection Example");
        Console.WriteLine("(SINK-ONLY — Canny + sub-pixel contour vertices)");
        Console.WriteLine("============================================");
        Console.WriteLine($"Arguments received: {args.Length}");
        for (int i = 0; i < args.Length; i++)
            Console.WriteLine($"  [{i}]: {args[i]}");
        Console.WriteLine("============================================");
        Console.WriteLine();

        await Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSimpleConsole(options =>
                {
                    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fff] ";
                    options.UseUtcTimestamp = false;
                    options.SingleLine = true;
                });
            })
            .ConfigureServices((context, services) =>
            {
                services.AddHostedService<EdgeDetectionService>();
                services.AddSingleton<RocketWelderClient>(sp =>
                {
                    var configuration = sp.GetRequiredService<IConfiguration>();
                    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                    return RocketWelderClient.From(configuration, loggerFactory);
                });
            })
            .RunConsoleAsync();
    }
}

/// <summary>
/// Configuration block bound from appsettings.json "EdgeDetection" section.
/// </summary>
public sealed class EdgeDetectionOptions
{
    public const string SectionName = "EdgeDetection";

    /// <summary>SegmentClass id emitted for each detected contour. Must match the operator's PassCompleted.FeatureClassIds.</summary>
    public byte ClassId { get; init; } = 1;

    /// <summary>Lower Canny hysteresis threshold.</summary>
    public double CannyThreshold1 { get; init; } = 50.0;

    /// <summary>Upper Canny hysteresis threshold.</summary>
    public double CannyThreshold2 { get; init; } = 150.0;

    /// <summary>Discard contours with area below this value (pixels²).</summary>
    public double MinContourArea { get; init; } = 100.0;

    /// <summary>Discard contours with area above this value (pixels²) — filters frame-sized noise.</summary>
    public double MaxContourArea { get; init; } = 100_000.0;

    /// <summary>Discard contours with fewer than this many vertices (cube edges need ≥4).</summary>
    public int MinVertices { get; init; } = 4;
}

/// <summary>
/// Pure detection logic — no SDK dependency. Testable in isolation against synthetic frames.
/// </summary>
public static class EdgeDetector
{
    /// <summary>
    /// Holds one detected contour: integer points ready for the SDK wire format,
    /// the sub-pixel refined float points used to compute confidence, and the
    /// confidence value itself.
    /// </summary>
    public readonly record struct DetectedContour(
        Point[] IntPoints,
        PointF[] RefinedPoints,
        float Confidence);

    /// <summary>
    /// Detect cube edges from a BGR frame.
    /// Returns the contour list in detection order; consumers select by ClassId.
    /// </summary>
    public static List<DetectedContour> Detect(Mat frame, EdgeDetectionOptions options)
    {
        var results = new List<DetectedContour>();

        using var gray = new Mat();
        using var edges = new Mat();

        // 1. BGR → gray.
        CvInvoke.CvtColor(frame, gray, ColorConversion.Bgr2Gray);

        // 2. Canny.
        CvInvoke.Canny(gray, edges, options.CannyThreshold1, options.CannyThreshold2);

        // 3. External contours, polyline approximation.
        using var contours = new VectorOfVectorOfPoint();
        using var hierarchy = new Mat();
        CvInvoke.FindContours(
            edges, contours, hierarchy,
            RetrType.External, ChainApproxMethod.ChainApproxSimple);

        for (int i = 0; i < contours.Size; i++)
        {
            using var contour = contours[i];
            int n = contour.Size;
            if (n < options.MinVertices)
                continue;

            double area = CvInvoke.ContourArea(contour);
            if (area < options.MinContourArea || area > options.MaxContourArea)
                continue;

            // Promote to float and refine to sub-pixel.
            // cornerSubPix is load-bearing for the contour-quality confidence metric.
            // (Round-trip wire precision is capped at ±0.5 px by the integer-only protocol;
            //  see README "Precision floor".)
            var intPoints = contour.ToArray();
            var seed = new PointF[n];
            for (int k = 0; k < n; k++)
                seed[k] = new PointF(intPoints[k].X, intPoints[k].Y);

            using var refinedVec = new VectorOfPointF(seed);
            CvInvoke.CornerSubPix(
                gray,
                refinedVec,
                new Size(5, 5),
                new Size(-1, -1),
                new MCvTermCriteria(30, 0.01));
            var refined = refinedVec.ToArray();

            // Confidence: refined contour area / refined convex-hull area.
            // Both inputs are the sub-pixel-refined PointF vertices, so the metric
            // reflects post-CornerSubPix contour quality (which is what the FR-2.5
            // tiebreaker will see at the consumer once the float result is reachable
            // through the wire). Convex shapes → ≈ 1.0; jagged/self-intersecting →
            // toward 0. Bounded [0,1] by construction (contour ⊆ hull).
            var refinedHull = CvInvoke.ConvexHull(refined, clockwise: false);
            using var refinedHullVec = new VectorOfPointF(refinedHull);
            double refinedArea = CvInvoke.ContourArea(refinedVec, oriented: false);
            double refinedHullArea = CvInvoke.ContourArea(refinedHullVec, oriented: false);
            float confidence = refinedHullArea > 0
                ? (float)Math.Clamp(refinedArea / refinedHullArea, 0.0, 1.0)
                : 0f;

            // Round sub-pixel result to int for SDK emission.
            var emitted = new Point[n];
            for (int k = 0; k < n; k++)
                emitted[k] = new Point(
                    (int)Math.Round(refined[k].X),
                    (int)Math.Round(refined[k].Y));

            results.Add(new DetectedContour(emitted, refined, confidence));
        }

        return results;
    }
}

public sealed class EdgeDetectionService : BackgroundService
{
    private readonly RocketWelderClient _client;
    private readonly IConfiguration _configuration;
    private readonly EdgeDetectionOptions _options;
    private readonly ILogger<EdgeDetectionService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private int _frameCount;
    private int _instancesEmitted;
    private readonly int _exitAfter;

    public EdgeDetectionService(
        RocketWelderClient client,
        IConfiguration configuration,
        ILogger<EdgeDetectionService> logger,
        IHostApplicationLifetime lifetime)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
        _lifetime = lifetime;
        _options = configuration.GetSection(EdgeDetectionOptions.SectionName)
            .Get<EdgeDetectionOptions>() ?? new EdgeDetectionOptions();
        _exitAfter = configuration.GetValue<int>("exit-after", -1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Edge Detection client (SINK-ONLY): {Connection}", _client.Connection);
        _logger.LogInformation(
            "Config: ClassId={ClassId} Canny=[{T1},{T2}] Area=[{AMin},{AMax}] MinVertices={MinV}",
            _options.ClassId, _options.CannyThreshold1, _options.CannyThreshold2,
            _options.MinContourArea, _options.MaxContourArea, _options.MinVertices);

        _client.OnError += OnError;

        var segUrl = _configuration["RocketWelder:SegmentationSinkUrl"]
            ?? Environment.GetEnvironmentVariable("SEGMENTATION_SINK_URL");
        _logger.LogInformation("Segmentation sink: {Url}", segUrl ?? "(NullSink)");

        _client.Start(ProcessFrame, stoppingToken);

        if (_exitAfter > 0)
            _logger.LogInformation("Will exit after {ExitAfter} frames", _exitAfter);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }

        _logger.LogInformation(
            "Stopping client. Total frames: {FrameCount}, instances emitted: {Instances}",
            _frameCount, _instancesEmitted);
        _client.Stop();
    }

    private void OnError(object? sender, ErrorEventArgs e)
    {
        _logger.LogError(e.Exception, "Client error occurred");
        _lifetime.StopApplication();
    }

    private void ProcessFrame(
        FrameMetadata metadata,
        Mat input,
        ISegmentationResultWriter segWriter,
        IKeyPointsWriter kpWriter,
        IStageWriter stageWriter)
    {
        _frameCount++;

        var contours = EdgeDetector.Detect(input, _options);

        byte instanceId = 0;
        foreach (var c in contours)
        {
            // Round-to-int has already happened in EdgeDetector. Points[0] is the EdgeStart
            // per FR-5.2 — Emgu.CV's FindContours (External + ChainApproxSimple) places the
            // first vertex at the top-most, then left-most pixel of the contour for axis-
            // aligned features. Consumer (rocket-welder2 AdaptivePointCapture) treats
            // Points[0] verbatim per KeypointStrategy.EdgeStart.
            segWriter.Append(_options.ClassId, instanceId, c.Confidence, c.IntPoints);
            instanceId++;
            _instancesEmitted++;
        }

        if (_frameCount % 30 == 0)
        {
            _logger.LogInformation(
                "Frame {Frame}: {Detected} contours, instances emitted total: {Total}",
                _frameCount, contours.Count, _instancesEmitted);
        }

        if (_exitAfter > 0 && _frameCount >= _exitAfter)
        {
            _logger.LogInformation("Reached {ExitAfter} frames, exiting...", _exitAfter);
            _lifetime.StopApplication();
        }
    }

    public override void Dispose()
    {
        _client?.Dispose();
        base.Dispose();
    }
}
