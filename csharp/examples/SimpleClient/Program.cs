using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using RocketWelder.SDK;
using ZeroBuffer;
using ZeroBuffer.DuplexChannel;

/// <summary>
/// Calculates FPS based on a rolling window of frame timestamps.
/// </summary>
public class FpsCalculator
{
    private readonly int _windowSize;
    private readonly Queue<DateTime> _frameTimes = new Queue<DateTime>();
    private double _fps = 0.0;

    public FpsCalculator(int windowSize = 5)
    {
        _windowSize = windowSize;
    }

    public void Update()
    {
        var now = DateTime.Now;
        _frameTimes.Enqueue(now);
        
        // Keep only last N frame times
        while (_frameTimes.Count > _windowSize)
        {
            _frameTimes.Dequeue();
        }
        
        // Calculate FPS from frame window
        if (_frameTimes.Count >= 2)
        {
            var first = _frameTimes.First();
            var last = _frameTimes.Last();
            var timeSpan = (last - first).TotalSeconds;
            if (timeSpan > 0)
            {
                _fps = (_frameTimes.Count - 1) / timeSpan;
            }
        }
    }

    public double GetFps() => _fps;
}

/// <summary>
/// Handles rendering of overlays on video frames.
/// </summary>
public static class FrameOverlay
{
    public static void DrawText(Mat frame, string text, Point position, 
                                FontFace font = FontFace.HersheySimplex,
                                double fontScale = 0.5, 
                                MCvScalar? color = null, 
                                int thickness = 1)
    {
        var textColor = color ?? new MCvScalar(255, 255, 255);
        CvInvoke.PutText(frame, text, position, font, fontScale, textColor, thickness);
    }

    public static void DrawDuplexOverlay(Mat frame, int frameCount, double fps)
    {
        // Draw "DUPLEX" label
        DrawText(frame, "DUPLEX", new Point(10, 30), 
                fontScale: 1.0, color: new MCvScalar(0, 0, 255), thickness: 2);
        
        // Draw timestamp
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        DrawText(frame, timestamp, new Point(10, 60));
        
        // Draw frame counter
        DrawText(frame, $"Frame: {frameCount}", new Point(10, 90));
        
        // Draw FPS
        DrawText(frame, $"FPS: {fps:F1}", new Point(10, 120), 
                color: new MCvScalar(0, 255, 0));
    }
}

class Program
{
    static async Task Main(string[] args)
    {
        // Print all arguments for debugging
        Console.WriteLine("========================================");
        Console.WriteLine("RocketWelder SDK SimpleClient 2025");
        Console.WriteLine("========================================");
        Console.WriteLine($"Arguments received: {args.Length}");
        Console.WriteLine($"OpenCV: {typeof(Mat).Assembly.FullName}");
        using var m = new Mat(new Size(640, 640), DepthType.Cv8U, 1);
        for (int i = 0; i < args.Length; i++)
        {
            Console.WriteLine($"  [{i}]: {args[i]}");
        }
        Console.WriteLine("========================================");
        Console.WriteLine();

        await Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddHostedService<VideoProcessingService>();
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

public class VideoProcessingService : BackgroundService
{
    private readonly RocketWelderClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VideoProcessingService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private int _frameCount = 0;
    private int _exitAfter = -1;
    
    // Separate concerns - FPS calculation
    private readonly FpsCalculator _fpsCalculator = new FpsCalculator(windowSize: 5);

    public VideoProcessingService(
        RocketWelderClient client,
        IConfiguration configuration,
        ILogger<VideoProcessingService> logger,
        IHostApplicationLifetime lifetime)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
        _lifetime = lifetime;
        
        // Get exit-after from configuration
        _exitAfter = configuration.GetValue<int>("exit-after", -1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting RocketWelder client..." + _client.Connection);
        _client.OnError += OnError;
        // Check if we're in duplex mode or one-way mode
        if (_client.Connection.ConnectionMode == ConnectionMode.Duplex)
        {
            _logger.LogInformation("Running in DUPLEX mode - will process frames and return results");
            _logger.LogInformation($"Can be tested with: \n\n\tgst-launch-1.0 videotestsrc num-buffers={_exitAfter} pattern=ball ! video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! zerofilter channel-name={_client.Connection.BufferName} ! fakesink");
            _client.Start(ProcessFrameDuplex, stoppingToken);
        }
        else
        {
            _logger.LogInformation("Running in ONE-WAY mode - will receive and process frames in-place");
            _logger.LogInformation($"Can be tested with: \n\n\tgst-launch-1.0 videotestsrc num-buffers={_exitAfter} pattern=ball ! video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! zerosink buffer-name={_client.Connection.BufferName} sync=false");
            _client.Start(ProcessFrameOneWay, stoppingToken);
        }
        if (_exitAfter > 0)
        {
            _logger.LogInformation("Will exit after {ExitAfter} frames", _exitAfter);
        }

        // Run until cancelled or frame limit reached
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }

        _logger.LogInformation("Stopping client...");
        _logger.LogInformation("Total frames processed: {FrameCount}", _frameCount);
        _client.Stop();
    }

    private void OnError(object? sender, ErrorEventArgs e)
    {
        _logger.LogError(e.Exception, "Client error occurred");
        
        // Stop the application on any error (all errors are terminal)
        _lifetime.StopApplication();
    }


    private void ProcessFrameOneWay(Mat input)
    {
        _frameCount++;

        _logger.LogInformation("Processed frame {FrameCount} ({Width}x{Height}) in one-way mode", 
            _frameCount, input.Width, input.Height);

        // Check if we should exit
        if (_exitAfter > 0 && _frameCount >= _exitAfter)
        {
            _logger.LogInformation("Reached {ExitAfter} frames, exiting...", _exitAfter);
            _lifetime.StopApplication();
        }
    }

    private void ProcessFrameDuplex(Mat input, Mat output)
    {
        _frameCount++;
        _fpsCalculator.Update();

        // Copy input to output first
        input.CopyTo(output);

        // Use FrameOverlay to draw all overlays
        FrameOverlay.DrawDuplexOverlay(output, _frameCount, _fpsCalculator.GetFps());

        _logger.LogInformation("Processed frame {FrameCount} ({Width}x{Height}) in duplex mode", 
            _frameCount, input.Width, input.Height);

        // Check if we should exit
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