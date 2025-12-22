# Multi-Stream Overlay Architecture

## Status
- **Approved** - Option A selected

## Problem Statement

Current `VectorOverlay` creates separate components for each decoder type, resulting in:
- 3 SKCanvasView instances (3 WebGL contexts)
- 3 RenderingStage instances
- 3 LayerPool instances
- CSS alignment issues between independent canvases

## Solution: Option A - Separate Stages, Composite Rendering

Each stream keeps its own `RenderingStreamV2` with independent stage/pool. A thin `CompositeRenderingStream` wrapper renders them sequentially to achieve Z-order compositing.

### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                 CompositeRenderingStream                     │
├─────────────────────────────────────────────────────────────┤
│  List<RenderingStreamV2>                                    │
│  ┌─────────────────┐ ┌─────────────────┐ ┌────────────────┐ │
│  │ Stream[0]       │ │ Stream[1]       │ │ Stream[2]      │ │
│  │ SegDecoder      │ │ KpDecoder       │ │ ActDecoder     │ │
│  │ Own Stage       │ │ Own Stage       │ │ Own Stage      │ │
│  │ Own Pool        │ │ Own Pool        │ │ Own Pool       │ │
│  │ Layers=[0]      │ │ Layers=[0,1]    │ │ Layers=[0]     │ │
│  └─────────────────┘ └─────────────────┘ └────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                               │
                               ▼
                    ┌─────────────────────┐
                    │ Single SKCanvasView │
                    │ OnPaint: Render()   │
                    └─────────────────────┘
```

### Threading Model

```
DECODE THREADS (independent)              RENDER THREAD
┌─────────────────────────┐               ┌────────────────────┐
│ Thread 1 (Segmentation) │               │ OnPaint()          │
│ WS → Decode → Stage     │───┐           │                    │
├─────────────────────────┤   │           │ for stream in list:│
│ Thread 2 (Keypoints)    │   ├──────────▶│   stream.Render()  │
│ WS → Decode → Stage     │───┤           │                    │
├─────────────────────────┤   │           │ (sequential, no    │
│ Thread 3 (Actions)      │───┘           │  contention)       │
│ WS → Decode → Stage     │               └────────────────────┘
└─────────────────────────┘
```

**No shared mutable state** - each stream has its own stage/pool.

### Z-Order

Render order = list order:
1. `segStream.Render(canvas)` → drawn first (back)
2. `kpStream.Render(canvas)` → drawn second (middle)
3. `actionsStream.Render(canvas)` → drawn third (front)

Within each stream, layers are composited by index (0 before 1 before 2...).

### Memory Analysis (1080p)

| Component | Per Layer | Layers | Total |
|-----------|-----------|--------|-------|
| Segmentation stream | 8.3 MB | ~2-3 | ~20 MB |
| Keypoints stream | 8.3 MB | ~4-6 | ~40 MB |
| Actions stream | 8.3 MB | ~2-3 | ~20 MB |
| **Total** | | | **~80 MB** |

Acceptable for desktop/WASM. ~30% more than shared pool, but no synchronization complexity.

---

## Implementation

### Phase 1: CompositeRenderingStream (blazor-blaze)

**File: `BlazorBlaze/VectorGraphics/CompositeRenderingStream.cs`**

```csharp
namespace BlazorBlaze.VectorGraphics;

/// <summary>
/// Combines multiple RenderingStreamV2 instances into a single composited output.
/// Each stream runs independently with its own stage/pool.
/// Z-order is determined by the order streams are added.
/// </summary>
public class CompositeRenderingStream : IAsyncDisposable
{
    private readonly List<RenderingStreamV2> _streams = new();
    private bool _disposed;

    /// <summary>
    /// Adds a stream to the composite. Streams render in add order (first = back).
    /// </summary>
    public void AddStream(RenderingStreamV2 stream)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CompositeRenderingStream));
        _streams.Add(stream);
    }

    /// <summary>
    /// True if all streams are connected.
    /// </summary>
    public bool IsConnected => _streams.All(s => s.IsConnected);

    /// <summary>
    /// Connects all streams to their WebSocket endpoints.
    /// </summary>
    public async Task ConnectAllAsync(CancellationToken ct = default)
    {
        foreach (var stream in _streams)
        {
            if (!stream.IsConnected)
                await stream.ConnectAsync(stream.Uri, ct);
        }
    }

    /// <summary>
    /// Disconnects all streams.
    /// </summary>
    public async Task DisconnectAllAsync()
    {
        foreach (var stream in _streams)
            await stream.DisconnectAsync();
    }

    /// <summary>
    /// Renders all streams to the canvas in order (first stream = back).
    /// </summary>
    public void Render(SKCanvas canvas)
    {
        foreach (var stream in _streams)
            stream.Render(canvas);
    }

    /// <summary>
    /// Gets aggregate stats across all streams.
    /// </summary>
    public (ulong TotalFrames, float MinFps, Bytes TotalTransfer) GetStats()
    {
        ulong totalFrames = 0;
        float minFps = float.MaxValue;
        long totalBytes = 0;

        foreach (var stream in _streams)
        {
            totalFrames += stream.Frame;
            if (stream.Fps < minFps) minFps = stream.Fps;
            totalBytes += stream.TransferRate;
        }

        return (totalFrames, _streams.Count > 0 ? minFps : 0, totalBytes);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var stream in _streams)
            await stream.DisposeAsync();

        _streams.Clear();
    }
}
```

### Phase 2: RenderingStreamV2 Enhancement

Add `Uri` property to store the connection URI for reconnection:

```csharp
// In RenderingStreamV2.cs
public Uri? Uri { get; private set; }

public async Task ConnectAsync(Uri uri, CancellationToken ct = default)
{
    Uri = uri;  // Store for reconnection
    // ... existing code ...
}
```

### Phase 3: Update Decoders (rocket-welder-sdk)

**SegmentationDecoder** - already uses single layer, just make it explicit:

```csharp
public class SegmentationDecoder : IFrameDecoder
{
    private readonly byte _layer;  // Single layer

    public SegmentationDecoder(IStage stage, byte layer = 0, RgbColor? defaultColor = null)
    {
        _stage = stage;
        _layer = layer;
        // ...
    }

    public DecodeResultV2 Decode(ReadOnlySpan<byte> data)
    {
        // ...
        _stage.Clear(_layer);
        var canvas = _stage[_layer];
        // ...
    }
}
```

**KeypointsDecoder** - uses 2 layers:

```csharp
public class KeypointsDecoder : IFrameDecoder
{
    private readonly byte _skeletonLayer;
    private readonly byte _pointsLayer;

    public KeypointsDecoder(IStage stage, byte skeletonLayer = 0, byte pointsLayer = 1)
    {
        _stage = stage;
        _skeletonLayer = skeletonLayer;
        _pointsLayer = pointsLayer;
    }

    public DecodeResultV2 Decode(ReadOnlySpan<byte> data)
    {
        // ...
        _stage.Clear(_skeletonLayer);
        _stage.Clear(_pointsLayer);

        var skeletonCanvas = _stage[_skeletonLayer];
        var pointsCanvas = _stage[_pointsLayer];

        // Draw skeleton lines to skeletonCanvas
        // Draw keypoint circles to pointsCanvas
        // ...
    }
}
```

### Phase 4: Demo Page (rocket-welder-sdk)

**File: `samples/RocketWelder.SDK.Blazor.Sample.Client/Pages/MultiStreamDemo.razor`**

```razor
@page "/multi-stream"
@using BlazorBlaze.VectorGraphics
@using RocketWelder.SDK.Blazor
@inject ILoggerFactory LoggerFactory
@inject NavigationManager NavigationManager
@implements IAsyncDisposable

<h2>Multi-Stream Overlay Demo</h2>

<p>Demonstrates composite rendering of multiple ML streams (segmentation + keypoints)
with independent decode threads and unified rendering.</p>

<div class="controls mb-3">
    <button class="btn btn-success me-2" @onclick="Connect" disabled="@_connected">
        Connect All Streams
    </button>
    <button class="btn btn-danger" @onclick="Disconnect" disabled="@(!_connected)">
        Disconnect
    </button>
</div>

<div class="stats mb-3 p-2 bg-dark text-light rounded">
    <span class="me-3">Segmentation: <strong>@_segFps.ToString("F1") FPS</strong></span>
    <span class="me-3">Keypoints: <strong>@_kpFps.ToString("F1") FPS</strong></span>
    <span>Transfer: <strong>@_transfer/s</strong></span>
</div>

<div style="width: 800px; height: 600px; border: 1px solid #ccc; background: #1a1a2e;">
    <SKCanvasView OnPaintSurface="OnPaint" EnableRenderLoop="true"
                  style="width: 100%; height: 100%;" />
</div>

@code {
    private const int Width = 800;
    private const int Height = 600;

    private CompositeRenderingStream? _composite;
    private RenderingStreamV2? _segStream;
    private RenderingStreamV2? _kpStream;

    private bool _connected;
    private float _segFps;
    private float _kpFps;
    private Bytes _transfer;

    protected override void OnInitialized()
    {
        // Build segmentation stream (layer 0)
        _segStream = new RenderingStreamBuilder(Width, Height, LoggerFactory)
            .WithDecoder(stage => new SegmentationDecoder(stage, layer: 0))
            .Build();

        // Build keypoints stream (layers 0, 1 within its own stage)
        _kpStream = new RenderingStreamBuilder(Width, Height, LoggerFactory)
            .WithDecoder(stage => new KeypointsDecoder(stage, skeletonLayer: 0, pointsLayer: 1))
            .Build();

        // Combine into composite (order = Z-order: seg behind kp)
        _composite = new CompositeRenderingStream();
        _composite.AddStream(_segStream);
        _composite.AddStream(_kpStream);
    }

    private async Task Connect()
    {
        var baseUri = new Uri(NavigationManager.BaseUri);
        var wsScheme = baseUri.Scheme == "https" ? "wss" : "ws";

        var segUri = new Uri($"{wsScheme}://{baseUri.Host}:{baseUri.Port}/ws/segmentation");
        var kpUri = new Uri($"{wsScheme}://{baseUri.Host}:{baseUri.Port}/ws/keypoints");

        await _segStream!.ConnectAsync(segUri);
        await _kpStream!.ConnectAsync(kpUri);

        _connected = true;
        StateHasChanged();
    }

    private async Task Disconnect()
    {
        await _composite!.DisconnectAllAsync();
        _connected = false;
        StateHasChanged();
    }

    private void OnPaint(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(26, 26, 46)); // Dark background

        _composite?.Render(canvas);

        // Update stats
        _segFps = _segStream?.Fps ?? 0;
        _kpFps = _kpStream?.Fps ?? 0;
        _transfer = (_segStream?.TransferRate ?? 0) + (_kpStream?.TransferRate ?? 0);

        // Periodic UI update
        if ((_segStream?.Frame ?? 0) % 10 == 0)
            InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        if (_composite != null)
            await _composite.DisposeAsync();
    }
}
```

### Phase 5: Server Endpoints

The sample app already has `/ws/segmentation` and `/ws/keypoints` endpoints. Verify they exist and work independently.

---

## Test Plan

### Unit Tests
- [ ] `CompositeRenderingStream` adds streams in order
- [ ] `Render()` calls each stream's `Render()` in order
- [ ] `DisconnectAllAsync()` disconnects all streams
- [ ] `DisposeAsync()` disposes all streams

### Integration Tests (Playwright)
1. Navigate to `/multi-stream`
2. Click "Connect All Streams"
3. Verify both streams show FPS > 0
4. Verify canvas renders (take screenshot)
5. Click "Disconnect"
6. Verify FPS drops to 0

### Manual Verification
- Segmentation polygons visible
- Keypoints skeleton visible on top of segmentation
- Keypoint circles visible on top of skeleton
- Smooth animation at target FPS

---

## Files to Create/Modify

### blazor-blaze
| File | Action |
|------|--------|
| `src/BlazorBlaze/VectorGraphics/CompositeRenderingStream.cs` | **Create** |
| `src/BlazorBlaze/VectorGraphics/RenderingStreamV2.cs` | Add `Uri` property |

### rocket-welder-sdk
| File | Action |
|------|--------|
| `csharp/RocketWelder.SDK.Blazor/SegmentationDecoder.cs` | Add `layer` parameter |
| `csharp/RocketWelder.SDK.Blazor/KeypointsDecoder.cs` | Add `skeletonLayer`, `pointsLayer` parameters |
| `csharp/samples/.../Pages/MultiStreamDemo.razor` | **Create** |

### rocket-welder2 (later)
| File | Action |
|------|--------|
| `VectorOverlay.razor` | Use `CompositeRenderingStream` |
| `PreviewPage_v2.razor` | Single `VectorOverlay` |

---

## Success Criteria

1. Demo page shows both streams rendering simultaneously
2. Segmentation renders behind keypoints (Z-order correct)
3. Each stream has independent FPS (can differ)
4. No shared state issues (no race conditions)
5. Memory usage reasonable (~80 MB for 1080p)
