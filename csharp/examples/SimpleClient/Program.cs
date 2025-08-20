using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Emgu.CV;
using RocketWelder.SDK;
using ZeroBuffer.DuplexChannel;

class Program
{
    static async Task Main(string[] args)
    {
        // Print all arguments for debugging
        Console.WriteLine("========================================");
        Console.WriteLine("RocketWelder SDK SimpleClient");
        Console.WriteLine("========================================");
        Console.WriteLine($"Arguments received: {args.Length}");
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
                    return RocketWelderClient.From(configuration);
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
        
        // Check if we're in duplex mode or one-way mode
        if (_client.Connection.ConnectionMode == ConnectionMode.Duplex)
        {
            _logger.LogInformation("Running in DUPLEX mode - will process frames and return results");
            _logger.LogInformation($"Can be tested with: \n\n\tgst-launch-1.0 videotestsrc num-buffers={_exitAfter} pattern=ball ! video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! zerofilter channel-name={_client.Connection.BufferName} ! fakesink");
           
        }
        else
        {
            _logger.LogInformation("Running in ONE-WAY mode - will receive and process frames in-place");
            _logger.LogInformation($"Can be tested with: \n\n\tgst-launch-1.0 videotestsrc num-buffers={_exitAfter} pattern=ball ! video/x-raw,width=640,height=480,framerate=30/1,format=RGB ! zerosink buffer-name={_client.Connection.BufferName} sync=false");
            
        }
        _client.Start(ProcessFrameDuplex);
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

    

    private void ProcessFrameDuplex(Mat input, Mat output)
    {
        _frameCount++;

        // Copy input to output first
        input.CopyTo(output);

        // Add overlay text to the output
        CvInvoke.PutText(output, "DUPLEX", new System.Drawing.Point(10, 30),
                   Emgu.CV.CvEnum.FontFace.HersheySimplex, 1.0, new Emgu.CV.Structure.MCvScalar(0, 0, 255), 2);

        // Add timestamp overlay
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        CvInvoke.PutText(output, timestamp, new System.Drawing.Point(10, 60),
                   Emgu.CV.CvEnum.FontFace.HersheySimplex, 0.5, new Emgu.CV.Structure.MCvScalar(255, 255, 255), 1);

        // Add frame counter
        CvInvoke.PutText(output, $"Frame: {_frameCount}", new System.Drawing.Point(10, 90),
                   Emgu.CV.CvEnum.FontFace.HersheySimplex, 0.5, new Emgu.CV.Structure.MCvScalar(255, 255, 255), 1);

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