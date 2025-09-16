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
using EventStore.Client;
using MicroPlumberd;
using ModelingEvolution.Drawing;
using RocketWelder.SDK;
using RocketWelder.SDK.Ui;
using RocketWelder.SDK.Ui.Internals;
using ZeroBuffer;
using ZeroBuffer.DuplexChannel;
using VectorF = ModelingEvolution.Drawing.Vector<float>;
using PointF = ModelingEvolution.Drawing.Point<float>;
using RectangleF = ModelingEvolution.Drawing.Rectangle<float>;
using Timeout = System.Threading.Timeout;

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

    public static void DrawDuplexOverlay(Mat frame, int frameCount, double fps, PointF crosshairPos)
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
        
        // Draw crosshair
        DrawCrosshair(frame, crosshairPos);
    }
    
    public static void DrawCrosshair(Mat frame, PointF position)
    {
        // Draw a crosshair at the specified position
        var color = new MCvScalar(0, 255, 255); // Yellow
        var lineThickness = 2;
        var size = 20;
        var x = (int)position.X;
        var y = (int)position.Y;
        
        // Horizontal line
        CvInvoke.Line(frame, 
            new Point(x - size, y), 
            new Point(x + size, y), 
            color, lineThickness);
        
        // Vertical line
        CvInvoke.Line(frame, 
            new Point(x, y - size), 
            new Point(x, y + size), 
            color, lineThickness);
        
        // Center dot
        CvInvoke.Circle(frame, new Point(x, y), 3, new MCvScalar(255, 0, 0), -1);
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
        using var m = new Mat(new System.Drawing.Size(640, 640), DepthType.Cv8U, 1);
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

                // Only add UI services if EventStore is configured
                var configuration = context.Configuration;
                if (!string.IsNullOrEmpty(configuration["EventStore"]))
                {
                    services.AddRocketWelderUi();
                }
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
    private readonly IPlumberInstance _plumber;
    private readonly IServiceProvider _serviceProvider;
    private int _frameCount = 0;
    private int _exitAfter = -1;
    
    // Separate concerns - FPS calculation
    private readonly FpsCalculator _fpsCalculator = new FpsCalculator(windowSize: 5);
    
    // UI controls
    private IUiService? _uiService;
    private ArrowGridControl? _arrowGrid;
    
    // Crosshair movement
    private PointF _crosshairPosition;
    private VectorF _velocity = new VectorF(0f, 0f);
    private RectangleF _frameBounds;
    private const float MovementSpeed = 5f; // pixels per frame
    private Timer? _uiUpdateTimer;

    public VideoProcessingService(
        RocketWelderClient client,
        IConfiguration configuration,
        ILogger<VideoProcessingService> logger,
        IHostApplicationLifetime lifetime, IPlumberInstance plumber,
        IServiceProvider serviceProvider)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
        _lifetime = lifetime;
        _plumber = plumber;
        _serviceProvider = serviceProvider;
        
        // Get exit-after from configuration
        _exitAfter = configuration.GetValue<int>("exit-after", -1);
        
        // Initialize crosshair at center (will be set properly when we know frame size)
        _crosshairPosition = new PointF(320f, 240f);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SessionId: " + _configuration["SessionId"]);

        // Only check EventStore if it's configured
        var eventStoreConfig = _configuration["EventStore"];
        if (!string.IsNullOrEmpty(eventStoreConfig))
        {
            _logger.LogInformation("EventStore: " + eventStoreConfig);
            await CheckEventStore(stoppingToken);
        }
        else
        {
            _logger.LogInformation("No EventStore configured, skipping EventStore connection");
        }

        _logger.LogInformation("Starting RocketWelder client..." + _client.Connection);
        _client.OnError += OnError;
        
        // Initialize UI service if SessionId is available
        await InitializeUiControls();
        
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

        // Check if preview is enabled and handle display
        if (_client.Connection.Parameters.TryGetValue("preview", out var preview) &&
            preview.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Showing preview... Press 'q' in preview window to stop");
            _client.Show(stoppingToken);
        }
        else
        {
            // Run until cancelled or frame limit reached
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
        }

        _logger.LogInformation("Stopping client...");
        _logger.LogInformation("Total frames processed: {FrameCount}", _frameCount);
        _client.Stop();
    }

    private async Task CheckEventStore(CancellationToken stoppingToken)
    {
        var conn = EventStoreClientSettings.Create(_configuration["EventStore"]);
        await conn.WaitUntilReady(TimeSpan.FromSeconds(5));
        EventStoreClient client = new EventStoreClient(conn);
        var evt = await client.ReadAllAsync(Direction.Forwards, Position.Start, 1, false, null)
            .FirstAsync(cancellationToken: stoppingToken);
        _logger.LogInformation("EventStore connected, read 1 event: "+evt.Event.EventStreamId);
    }

    private async Task InitializeUiControls()
    {
        var sessionIdString = _configuration["SessionId"];
        if (string.IsNullOrEmpty(sessionIdString))
        {
            _logger.LogInformation("No SessionId configured, UI controls will be disabled");
            return;
        }
        
        if (!Guid.TryParse(sessionIdString, out var sessionId))
        {
            _logger.LogWarning("Invalid SessionId format: {SessionId}", sessionIdString);
            return;
        }
        
        try
        {
            _logger.LogInformation("Initializing UI service with SessionId: {SessionId}", sessionId);
            _uiService = UiService.FromSessionId(sessionId);
            await _uiService.Initialize(_serviceProvider);
            
            // Create ArrowGrid control
            _arrowGrid = _uiService.Factory.DefineArrowGrid("crosshair-control");
            
            // Hook up events
            _arrowGrid.ArrowDown += OnArrowDown;
            _arrowGrid.ArrowUp += OnArrowUp;
            
            // Add to bottom-center region
            _uiService[RegionName.PreviewBottomCenter].Add(_arrowGrid);
            
            // Send initial control definition
            await _uiService.Do();
            
            // Start timer to call Do() every 500ms
            _uiUpdateTimer = new Timer(async _ => 
            {
                try
                {
                    if (_uiService != null)
                    {
                        await _uiService.Do();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error calling UiService.Do()");
                }
            }, null, TimeSpan.FromSeconds(1/30), TimeSpan.FromSeconds(1 / 30));
            
            _logger.LogInformation("ArrowGrid control initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize UI controls");
            _uiService = null;
            _arrowGrid = null;
        }
    }
    
    private void OnArrowDown(object sender, ArrowDirection direction)
    {
        _logger.LogInformation("Arrow {Direction} pressed", direction);
        
        // Set velocity based on arrow direction using unit vectors and scalar multiplication
        _velocity = direction switch
        {
            ArrowDirection.Up => new VectorF(0f, -1f) * MovementSpeed,
            ArrowDirection.Down => new VectorF(0f, 1f) * MovementSpeed,
            ArrowDirection.Left => new VectorF(-1f, 0f) * MovementSpeed,
            ArrowDirection.Right => new VectorF(1f, 0f) * MovementSpeed,
            _ => new VectorF(0f, 0f)
        };
    }
    
    private void OnArrowUp(object sender, ArrowDirection direction)
    {
        _logger.LogInformation("Arrow {Direction} released", direction);
        
        // Stop movement when arrow is released
        _velocity = new VectorF(0f, 0f);
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

        // Initialize frame bounds and crosshair position on first frame
        if (_frameCount == 1)
        {
            _frameBounds = new RectangleF(20f, 20f, input.Width - 40f, input.Height - 40f);
            _crosshairPosition = new PointF(input.Width / 2f, input.Height / 2f);
        }
        
        // Update crosshair position based on velocity using point + vector operator
        _crosshairPosition = (_crosshairPosition+_velocity).Clamp(_frameBounds);
        

        // Copy input to output first
        input.CopyTo(output);

        // Use FrameOverlay to draw all overlays including crosshair
        FrameOverlay.DrawDuplexOverlay(output, _frameCount, _fpsCalculator.GetFps(), _crosshairPosition);

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
        _uiUpdateTimer?.Dispose();
        _arrowGrid?.Dispose();
        _uiService?.DisposeAsync().AsTask().Wait();
        _client?.Dispose();
        base.Dispose();
    }
}