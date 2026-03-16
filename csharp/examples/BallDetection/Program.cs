using System.Drawing;
using BlazorBlaze.Server;
using BlazorBlaze.VectorGraphics;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RocketWelder.SDK;
using RocketWelder.SDK.Graphics;
using static RocketWelder.SDK.RocketWelderClient;
using ErrorEventArgs = ZeroBuffer.ErrorEventArgs;

/// <summary>
/// Simple example detecting a ball from videotestsrc pattern=ball.
/// Outputs ball edge as segmentation, center as keypoint, and position as text overlay.
///
/// This is a SINK-ONLY example - it does NOT modify the output frame.
/// Data is streamed via Unix sockets for downstream consumers.
///
/// Optional configuration (uses NullSink if not set):
///   - RocketWelder:SegmentationSinkUrl or SEGMENTATION_SINK_URL
///   - RocketWelder:KeyPointsSinkUrl or KEYPOINTS_SINK_URL
///   - RocketWelder:GraphicsSinkUrl or GRAPHICS_SINK_URL
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("RocketWelder SDK Ball Detection Example");
        Console.WriteLine("(SINK-ONLY - no frame modification)");
        Console.WriteLine("========================================");
        Console.WriteLine($"Arguments received: {args.Length}");
        for (int i = 0; i < args.Length; i++)
        {
            Console.WriteLine($"  [{i}]: {args[i]}");
        }
        Console.WriteLine("========================================");
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
                services.AddHostedService<BallDetectionService>();
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
/// Detects a ball from videotestsrc pattern=ball.
/// </summary>
public static class BallDetector
{
    public const byte BallClassId = 1;
    public const int CenterKeypointId = 0;

    /// <summary>
    /// Detect ball contour and center from frame.
    /// </summary>
    /// <returns>Tuple of (contour points, center, confidence). Null if no ball found.</returns>
    public static (Point[]? Contour, Point? Center, float Confidence) DetectBall(Mat frame)
    {
        using var gray = new Mat();
        using var thresh = new Mat();

        // Convert to grayscale
        CvInvoke.CvtColor(frame, gray, ColorConversion.Bgr2Gray);

        // Threshold to find bright ball
        CvInvoke.Threshold(gray, thresh, 200, 255, ThresholdType.Binary);

        // Find contours
        using var contours = new Emgu.CV.Util.VectorOfVectorOfPoint();
        using var hierarchy = new Mat();
        CvInvoke.FindContours(thresh, contours, hierarchy, RetrType.External, ChainApproxMethod.ChainApproxSimple);

        if (contours.Size == 0)
            return (null, null, 0.0f);

        // Get largest contour (the ball)
        int largestIdx = 0;
        double largestArea = 0;
        for (int i = 0; i < contours.Size; i++)
        {
            var area = CvInvoke.ContourArea(contours[i]);
            if (area > largestArea)
            {
                largestArea = area;
                largestIdx = i;
            }
        }

        if (largestArea < 100) // Too small, likely noise
            return (null, null, 0.0f);

        var largest = contours[largestIdx].ToArray();

        // Calculate center using moments
        var moments = CvInvoke.Moments(contours[largestIdx]);
        Point? center = null;
        float confidence = 0.0f;

        if (moments.M00 > 0)
        {
            int cx = (int)(moments.M10 / moments.M00);
            int cy = (int)(moments.M01 / moments.M00);
            center = new Point(cx, cy);
            confidence = (float)Math.Min(1.0, largestArea / 10000);
        }

        return (largest, center, confidence);
    }
}

public class BallDetectionService : BackgroundService
{
    private readonly RocketWelderClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BallDetectionService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private int _frameCount = 0;
    private int _segWritten = 0;
    private int _keyWritten = 0;
    private int _exitAfter = -1;

    public BallDetectionService(
        RocketWelderClient client,
        IConfiguration configuration,
        ILogger<BallDetectionService> logger,
        IHostApplicationLifetime lifetime)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
        _lifetime = lifetime;
        _exitAfter = configuration.GetValue<int>("exit-after", -1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Ball Detection client (SINK-ONLY): {Connection}", _client.Connection);
        _client.OnError += OnError;

        // Log sink configuration (NullSink used if not configured)
        var segUrl = _configuration["RocketWelder:SegmentationSinkUrl"] ?? Environment.GetEnvironmentVariable("SEGMENTATION_SINK_URL");
        var kpUrl = _configuration["RocketWelder:KeyPointsSinkUrl"] ?? Environment.GetEnvironmentVariable("KEYPOINTS_SINK_URL");
        var gfxUrl = _configuration["RocketWelder:GraphicsSinkUrl"] ?? Environment.GetEnvironmentVariable("GRAPHICS_SINK_URL");

        _logger.LogInformation("Segmentation sink: {Url}", segUrl ?? "(NullSink)");
        _logger.LogInformation("Keypoints sink: {Url}", kpUrl ?? "(NullSink)");
        _logger.LogInformation("Graphics sink: {Url}", gfxUrl ?? "(NullSink)");

        // Use the Start overload that provides writers
        _logger.LogInformation("Running in DUPLEX mode (sink-only, no frame modification)");
        _logger.LogInformation($"Test with: gst-launch-1.0 videotestsrc num-buffers={_exitAfter} pattern=ball ! video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! zerofilter channel-name={_client.Connection.BufferName} ! fakesink");

        _client.Start(ProcessFrameWithWriters, stoppingToken);

        if (_exitAfter > 0)
        {
            _logger.LogInformation("Will exit after {ExitAfter} frames", _exitAfter);
        }

        // Check if preview is enabled
        if (_client.Connection.Parameters.TryGetValue("preview", out var preview) &&
            preview.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Showing preview... Press 'q' to stop");
            _client.Show(stoppingToken);
        }
        else
        {
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _logger.LogInformation("Stopping client... Total frames: {FrameCount}", _frameCount);
        _client.Stop();
    }

    private void OnError(object? sender, ErrorEventArgs e)
    {
        _logger.LogError(e.Exception, "Client error occurred");
        _lifetime.StopApplication();
    }

    private void ProcessFrameWithWriters(FrameMetadata metadata, Mat input, ISegmentationResultWriter segWriter, IKeyPointsWriter kpWriter, IStageWriter stageWriter)
    {
        _frameCount++;

        // Detect ball
        var (contour, center, confidence) = BallDetector.DetectBall(input);

        // Write segmentation data (contour) if ball found
        if (contour != null && contour.Length >= 3)
        {
            segWriter.Append(BallDetector.BallClassId, 0, confidence, contour);
            _segWritten+=1;
        }

        // Write keypoint data (center) if found
        if (center.HasValue)
        {
            kpWriter.Append(BallDetector.CenterKeypointId, center.Value.X, center.Value.Y, confidence);
            _keyWritten +=1;
        }

        // Draw ball position and exposure time as text overlay
        var layer = stageWriter[0];
        layer.SetFontSize(24);
        layer.SetFontColor(new RgbColor(255, 255, 255)); // White text for visibility
        if (center.HasValue)
        {
            layer.DrawText($"Ball: ({center.Value.X}, {center.Value.Y})", 10, 30);
        }
        else
        {
            layer.DrawText("Ball: not detected", 10, 30);
        }

        var exposureText = metadata.HasExposureTime
            ? $"ET: {metadata.ExposureTimeUs}us"
            : "ET: N/A";
        layer.DrawText(exposureText, 10, 60);

        // Log every 30 frames
        if (_frameCount % 30 == 0)
        {
            if (center.HasValue)
            {
                _logger.LogInformation("Frame {Frame}: Ball at ({X}, {Y}), confidence: {Conf:F2}, ET: {ExposureTime}, Segmentations written: {Seg}, Keypoints written: {Keys}",
                    _frameCount, center.Value.X, center.Value.Y, confidence, exposureText, _segWritten, _keyWritten);
            }
            else
            {
                _logger.LogInformation("Frame {Frame}: No ball detected, ET: {ExposureTime}", _frameCount, exposureText);
            }
        }

        CheckExit();
    }

    private void CheckExit()
    {
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
