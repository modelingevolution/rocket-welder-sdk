using System.Drawing;
using System.Net.WebSockets;
using ModelingEvolution.EventAggregator.Blazor;
using ModelingEvolution.EventAggregator.Server;
using RocketWelder.SDK.Blazor.Sample.App.Components;
using RocketWelder.SDK.Protocols;

var builder = WebApplication.CreateBuilder(args);

// Enable static web assets discovery from referenced projects
if (builder.Environment.IsDevelopment())
{
    builder.WebHost.UseStaticWebAssets();
}

// Add Razor Components with both Server and WebAssembly interactivity
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// EventAggregator for Server-to-WASM event forwarding
builder.Services.AddEventAggregatorBlazor().AsServerSide();

var app = builder.Build();

app.UseWebSockets();

// Map EventAggregator WebSocket endpoint
app.MapEventAggregator();

app.UseStaticFiles();
app.UseAntiforgery();

// WebSocket endpoint for segmentation streaming demo
app.Map("/ws/segmentation", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    await StreamSegmentationAsync(ws, context.RequestAborted);
});

// WebSocket endpoint for keypoints streaming demo
app.Map("/ws/keypoints", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    await StreamKeyPointsAsync(ws, context.RequestAborted);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(RocketWelder.SDK.Blazor.Sample.Client._Imports).Assembly);

app.Run();

/// <summary>
/// Stream segmentation polygons at 30 FPS.
/// Simulates ML model output with animated random polygons.
/// </summary>
static async Task StreamSegmentationAsync(WebSocket ws, CancellationToken ct)
{
    const int Width = 800;
    const int Height = 600;
    const int PolygonCount = 8;

    var random = new Random(42);
    var buffer = new byte[8192];
    ulong frameId = 0;

    // Pre-generate polygon centers and radii
    var polygons = new (int centerX, int centerY, int radius, float phase, byte classId)[PolygonCount];
    for (int i = 0; i < PolygonCount; i++)
    {
        polygons[i] = (
            random.Next(100, Width - 100),
            random.Next(100, Height - 100),
            random.Next(30, 80),
            random.NextSingle() * MathF.PI * 2,
            (byte)(i % 16)
        );
    }

    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33)); // ~30 FPS

    while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
    {
        frameId++;
        float time = frameId * 0.05f;

        // Generate animated polygon instances
        var instances = new SegmentationInstance[PolygonCount];
        for (int p = 0; p < PolygonCount; p++)
        {
            var (cx, cy, baseRadius, phase, classId) = polygons[p];
            int pointCount = 6 + (p % 4); // 6-9 points per polygon
            var points = new Point[pointCount];

            float animatedRadius = baseRadius * (0.8f + 0.2f * MathF.Sin(time + phase));
            float rotation = time * 0.5f + phase;

            for (int i = 0; i < pointCount; i++)
            {
                float angle = (float)(2 * Math.PI * i / pointCount) + rotation;
                // Star-like shape variation
                float radiusVariation = 1f + 0.3f * MathF.Sin(3 * angle + time);
                float r = animatedRadius * radiusVariation;
                points[i] = new Point(
                    cx + (int)(r * MathF.Cos(angle)),
                    cy + (int)(r * MathF.Sin(angle))
                );
            }

            instances[p] = new SegmentationInstance(classId, (byte)p, Confidence.Full, points);
        }

        var frame = new SegmentationFrame(frameId, (uint)Width, (uint)Height, instances);
        int written = SegmentationProtocol.Write(buffer, frame);

        await ws.SendAsync(
            new ArraySegment<byte>(buffer, 0, written),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            ct);

        await timer.WaitForNextTickAsync(ct);
    }
}

/// <summary>
/// Stream keypoints at 30 FPS with master/delta encoding.
/// Simulates pose estimation output with smoothly moving keypoints.
/// </summary>
static async Task StreamKeyPointsAsync(WebSocket ws, CancellationToken ct)
{
    const int KeyPointCount = 17; // Standard pose model (COCO format)
    const int MasterInterval = 30; // Send master frame every 30 frames (1 second at 30 FPS)

    var buffer = new byte[4096];
    ulong frameId = 0;

    // Initial keypoint positions (rough human pose shape)
    var basePositions = new (int x, int y)[]
    {
        (400, 100), // 0: nose
        (390, 90),  // 1: left eye
        (410, 90),  // 2: right eye
        (380, 100), // 3: left ear
        (420, 100), // 4: right ear
        (350, 180), // 5: left shoulder
        (450, 180), // 6: right shoulder
        (320, 280), // 7: left elbow
        (480, 280), // 8: right elbow
        (300, 380), // 9: left wrist
        (500, 380), // 10: right wrist
        (370, 320), // 11: left hip
        (430, 320), // 12: right hip
        (360, 440), // 13: left knee
        (440, 440), // 14: right knee
        (350, 560), // 15: left ankle
        (450, 560), // 16: right ankle
    };

    var previousKeyPoints = new KeyPoint[KeyPointCount];
    var currentKeyPoints = new KeyPoint[KeyPointCount];

    // Initialize keypoints
    for (int i = 0; i < KeyPointCount; i++)
    {
        currentKeyPoints[i] = new KeyPoint(i, basePositions[i].x, basePositions[i].y, 900);
    }

    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33)); // ~30 FPS

    while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
    {
        frameId++;
        float time = frameId * 0.1f;

        // Copy current to previous
        Array.Copy(currentKeyPoints, previousKeyPoints, KeyPointCount);

        // Animate keypoints with smooth sine wave motion
        for (int i = 0; i < KeyPointCount; i++)
        {
            var (bx, by) = basePositions[i];
            float phase = i * 0.5f;

            // Gentle swaying motion
            int dx = (int)(15 * MathF.Sin(time + phase));
            int dy = (int)(8 * MathF.Cos(time * 0.7f + phase));

            // Confidence varies smoothly
            ushort confidence = (ushort)(800 + (int)(100 * MathF.Sin(time * 0.3f + phase)));

            currentKeyPoints[i] = new KeyPoint(i, bx + dx, by + dy, confidence);
        }

        int written;
        if (KeyPointsProtocol.ShouldWriteMasterFrame(frameId, MasterInterval))
        {
            written = KeyPointsProtocol.WriteMasterFrame(buffer, frameId, currentKeyPoints);
        }
        else
        {
            written = KeyPointsProtocol.WriteDeltaFrame(buffer, frameId, currentKeyPoints, previousKeyPoints);
        }

        await ws.SendAsync(
            new ArraySegment<byte>(buffer, 0, written),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            ct);

        await timer.WaitForNextTickAsync(ct);
    }
}
