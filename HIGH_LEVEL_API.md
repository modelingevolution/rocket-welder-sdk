# High-Level API Design

## Overview

This document describes the high-level API for RocketWelder SDK that provides a clean developer experience (DX) for video processing pipelines with keypoint detection and segmentation.

## Design Goals

1. **Simple DX**: Hide transport, writers, frame IDs, and buffer management from users
2. **Type-safe**: Use strongly-typed definitions (KeyPoint, SegmentClass) instead of raw IDs
3. **Schema + Data separation**: Static schema definitions vs per-frame data contexts
4. **Unit of Work pattern**: Data contexts scoped to frame, auto-commit on delegate return
5. **Configuration via environment**: Transport endpoints, intervals from env vars

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│  RocketWelderClient (Facade - User Entry Point)                     │
│                                                                     │
│  Properties (Schema - Static):                                      │
│  ├─ IKeyPointsSchema KeyPoints { get; }                             │
│  └─ ISegmentationSchema Segmentation { get; }                       │
│                                                                     │
│  Methods:                                                           │
│  └─ Start(Action<Mat, ISegmentationDataContext,                     │
│                  IKeyPointsDataContext, Mat>)                       │
│                                                                     │
│  Configuration (from environment):                                  │
│  ├─ ROCKET_WELDER_KEYPOINTS_ENDPOINT                                │
│  ├─ ROCKET_WELDER_SEGMENTATION_ENDPOINT                             │
│  ├─ ROCKET_WELDER_VIDEO_SOURCE                                      │
│  └─ ROCKET_WELDER_MASTER_FRAME_INTERVAL                             │
└─────────────────────────────────────────────────────────────────────┘
                              │
           ┌──────────────────┴──────────────────┐
           │                                     │
           ▼                                     ▼
┌─────────────────────────────┐   ┌─────────────────────────────┐
│  IKeyPointsSchema           │   │  ISegmentationSchema        │
│  (Definition - Static)      │   │  (Definition - Static)      │
│                             │   │                             │
│  DefinePoint(name)          │   │  DefineClass(id, name)      │
│  → KeyPoint                 │   │  → SegmentClass             │
│                             │   │                             │
│  GetMetadata() → JSON       │   │  GetMetadata() → JSON       │
└─────────────────────────────┘   └─────────────────────────────┘
           │                                     │
           │ creates per frame (UoW)             │ creates per frame (UoW)
           ▼                                     ▼
┌─────────────────────────────┐   ┌─────────────────────────────┐
│  IKeyPointsDataContext      │   │  ISegmentationDataContext   │
│  (UoW - Scoped to Frame)    │   │  (UoW - Scoped to Frame)    │
│                             │   │                             │
│  Add(KeyPoint, x, y, conf)  │   │  Add(SegmentClass,          │
│                             │   │      instanceId, points)    │
│                             │   │                             │
│  [auto-commits on dispose]  │   │  [auto-commits on dispose]  │
└─────────────────────────────┘   └─────────────────────────────┘
```

---

## API Reference

### Value Types

```csharp
/// <summary>
/// Represents a defined keypoint in the schema.
/// Returned by IKeyPointsSchema.DefinePoint().
/// </summary>
public readonly record struct KeyPoint(int Id, string Name);

/// <summary>
/// Represents a defined segmentation class in the schema.
/// Returned by ISegmentationSchema.DefineClass().
/// </summary>
public readonly record struct SegmentClass(byte ClassId, string Name);
```

### Schema Interfaces (Static Definitions)

```csharp
/// <summary>
/// Schema for defining keypoints. Static, defined once at startup.
/// </summary>
public interface IKeyPointsSchema
{
    /// <summary>
    /// Defines a keypoint with a human-readable name.
    /// ID is auto-assigned sequentially (0, 1, 2, ...).
    /// </summary>
    /// <param name="name">Human-readable name (e.g., "nose", "left_eye")</param>
    /// <returns>KeyPoint struct for use in data contexts</returns>
    KeyPoint DefinePoint(string name);

    /// <summary>
    /// Gets all defined keypoints.
    /// </summary>
    IReadOnlyList<KeyPoint> DefinedPoints { get; }

    /// <summary>
    /// Gets metadata as JSON for readers/consumers.
    /// </summary>
    string GetMetadataJson();
}

/// <summary>
/// Schema for defining segmentation classes. Static, defined once at startup.
/// </summary>
public interface ISegmentationSchema
{
    /// <summary>
    /// Defines a segmentation class with explicit ID and name.
    /// </summary>
    /// <param name="classId">Class ID (matches ML model output)</param>
    /// <param name="name">Human-readable name (e.g., "person", "car")</param>
    /// <returns>SegmentClass struct for use in data contexts</returns>
    SegmentClass DefineClass(byte classId, string name);

    /// <summary>
    /// Gets all defined classes.
    /// </summary>
    IReadOnlyList<SegmentClass> DefinedClasses { get; }

    /// <summary>
    /// Gets metadata as JSON for readers/consumers.
    /// </summary>
    string GetMetadataJson();
}
```

### Data Context Interfaces (Per-Frame UoW)

```csharp
/// <summary>
/// Unit of Work for keypoints data, scoped to a single frame.
/// Auto-commits when the delegate returns.
/// </summary>
public interface IKeyPointsDataContext
{
    /// <summary>
    /// Current frame ID.
    /// </summary>
    ulong FrameId { get; }

    /// <summary>
    /// Adds a keypoint detection for this frame.
    /// </summary>
    /// <param name="point">KeyPoint from schema definition</param>
    /// <param name="x">X coordinate in pixels</param>
    /// <param name="y">Y coordinate in pixels</param>
    /// <param name="confidence">Detection confidence (0.0 - 1.0)</param>
    void Add(KeyPoint point, int x, int y, float confidence);
}

/// <summary>
/// Unit of Work for segmentation data, scoped to a single frame.
/// Auto-commits when the delegate returns.
/// </summary>
public interface ISegmentationDataContext
{
    /// <summary>
    /// Current frame ID.
    /// </summary>
    ulong FrameId { get; }

    /// <summary>
    /// Frame width in pixels.
    /// </summary>
    uint Width { get; }

    /// <summary>
    /// Frame height in pixels.
    /// </summary>
    uint Height { get; }

    /// <summary>
    /// Adds a segmentation instance for this frame.
    /// </summary>
    /// <param name="segmentClass">SegmentClass from schema definition</param>
    /// <param name="instanceId">Instance ID (for multiple instances of same class)</param>
    /// <param name="points">Contour points defining the instance boundary</param>
    void Add(SegmentClass segmentClass, byte instanceId, ReadOnlySpan<Point> points);
}
```

### RocketWelderClient (Main Facade)

```csharp
/// <summary>
/// Main entry point for RocketWelder SDK.
/// Provides schema definitions and frame processing loop.
/// </summary>
public interface IRocketWelderClient : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Schema for defining keypoints.
    /// </summary>
    IKeyPointsSchema KeyPoints { get; }

    /// <summary>
    /// Schema for defining segmentation classes.
    /// </summary>
    ISegmentationSchema Segmentation { get; }

    /// <summary>
    /// Starts the processing loop with full context.
    /// </summary>
    /// <param name="processFrame">
    /// Delegate called for each frame with:
    /// - inputFrame: Source video frame (Mat)
    /// - segmentation: Segmentation data context (UoW)
    /// - keypoints: KeyPoints data context (UoW)
    /// - outputFrame: Output frame for visualization (Mat)
    /// </param>
    /// <param name="cancellationToken">Cancellation token to stop processing</param>
    Task StartAsync(
        Action<Mat, ISegmentationDataContext, IKeyPointsDataContext, Mat> processFrame,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the processing loop (keypoints only).
    /// </summary>
    Task StartAsync(
        Action<Mat, IKeyPointsDataContext, Mat> processFrame,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the processing loop (segmentation only).
    /// </summary>
    Task StartAsync(
        Action<Mat, ISegmentationDataContext, Mat> processFrame,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory for creating RocketWelderClient instances.
/// </summary>
public static class RocketWelderClient
{
    /// <summary>
    /// Creates a client configured from environment variables.
    /// </summary>
    public static IRocketWelderClient FromEnvironment();

    /// <summary>
    /// Creates a client with explicit configuration.
    /// </summary>
    public static IRocketWelderClient Create(RocketWelderClientOptions options);
}
```

---

## Usage Examples

### Basic Usage

```csharp
using RocketWelder.SDK;

// Create client from environment
using var client = RocketWelderClient.FromEnvironment();

// Define schema (static, once)
var nose = client.KeyPoints.DefinePoint("nose");
var leftEye = client.KeyPoints.DefinePoint("left_eye");
var rightEye = client.KeyPoints.DefinePoint("right_eye");
var leftShoulder = client.KeyPoints.DefinePoint("left_shoulder");
var rightShoulder = client.KeyPoints.DefinePoint("right_shoulder");

var personClass = client.Segmentation.DefineClass(1, "person");
var carClass = client.Segmentation.DefineClass(2, "car");
var weldClass = client.Segmentation.DefineClass(3, "weld");

// Start processing loop
await client.StartAsync((inputFrame, segmentation, keypoints, outputFrame) =>
{
    // Run keypoint detection
    var detections = poseDetector.Detect(inputFrame);
    foreach (var detection in detections)
    {
        keypoints.Add(nose, detection.Nose.X, detection.Nose.Y, detection.Nose.Confidence);
        keypoints.Add(leftEye, detection.LeftEye.X, detection.LeftEye.Y, detection.LeftEye.Confidence);
        keypoints.Add(rightEye, detection.RightEye.X, detection.RightEye.Y, detection.RightEye.Confidence);
        // ... more keypoints
    }

    // Run segmentation
    var masks = segmenter.Segment(inputFrame);
    foreach (var mask in masks)
    {
        var segmentClass = mask.ClassId switch
        {
            1 => personClass,
            2 => carClass,
            3 => weldClass,
            _ => continue
        };
        segmentation.Add(segmentClass, mask.InstanceId, mask.ContourPoints);
    }

    // Draw visualization on output frame
    inputFrame.CopyTo(outputFrame);
    DrawDetections(outputFrame, detections, masks);

    // Data contexts auto-commit when delegate returns
});
```

### KeyPoints Only

```csharp
using var client = RocketWelderClient.FromEnvironment();

var nose = client.KeyPoints.DefinePoint("nose");
var leftWrist = client.KeyPoints.DefinePoint("left_wrist");
var rightWrist = client.KeyPoints.DefinePoint("right_wrist");

await client.StartAsync((inputFrame, keypoints, outputFrame) =>
{
    var pose = detector.Detect(inputFrame);

    keypoints.Add(nose, pose.Nose.X, pose.Nose.Y, pose.Nose.Confidence);
    keypoints.Add(leftWrist, pose.LeftWrist.X, pose.LeftWrist.Y, pose.LeftWrist.Confidence);
    keypoints.Add(rightWrist, pose.RightWrist.X, pose.RightWrist.Y, pose.RightWrist.Confidence);

    inputFrame.CopyTo(outputFrame);
    DrawPose(outputFrame, pose);
});
```

### Segmentation Only

```csharp
using var client = RocketWelderClient.FromEnvironment();

var weldPool = client.Segmentation.DefineClass(1, "weld_pool");
var spatter = client.Segmentation.DefineClass(2, "spatter");
var arc = client.Segmentation.DefineClass(3, "arc");

await client.StartAsync((inputFrame, segmentation, outputFrame) =>
{
    var results = weldAnalyzer.Analyze(inputFrame);

    if (results.WeldPool != null)
        segmentation.Add(weldPool, 0, results.WeldPool.Contour);

    foreach (var (spatterInstance, idx) in results.Spatters.Select((s, i) => (s, i)))
        segmentation.Add(spatter, (byte)idx, spatterInstance.Contour);

    if (results.Arc != null)
        segmentation.Add(arc, 0, results.Arc.Contour);

    inputFrame.CopyTo(outputFrame);
    DrawWeldAnalysis(outputFrame, results);
});
```

---

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ROCKET_WELDER_VIDEO_SOURCE` | Video source (file path, camera index, or URL) | `0` (default camera) |
| `ROCKET_WELDER_KEYPOINTS_ENDPOINT` | KeyPoints transport endpoint | `ipc:///tmp/rocket-welder-keypoints` |
| `ROCKET_WELDER_SEGMENTATION_ENDPOINT` | Segmentation transport endpoint | `ipc:///tmp/rocket-welder-segmentation` |
| `ROCKET_WELDER_MASTER_FRAME_INTERVAL` | Frames between master keypoint frames | `300` |
| `ROCKET_WELDER_TRANSPORT` | Transport type: `nng`, `tcp`, `websocket` | `nng` |

---

## Metadata Format

Schemas emit metadata as JSON for readers/consumers to understand the data:

### KeyPoints Metadata

```json
{
    "version": 1,
    "type": "keypoints",
    "points": [
        {"id": 0, "name": "nose"},
        {"id": 1, "name": "left_eye"},
        {"id": 2, "name": "right_eye"},
        {"id": 3, "name": "left_shoulder"},
        {"id": 4, "name": "right_shoulder"}
    ]
}
```

### Segmentation Metadata

```json
{
    "version": 1,
    "type": "segmentation",
    "classes": [
        {"classId": 1, "name": "person"},
        {"classId": 2, "name": "car"},
        {"classId": 3, "name": "weld"}
    ]
}
```

---

## Internal Implementation

The high-level API is built on top of the low-level transport abstraction:

```
┌─────────────────────────────────────────────────────────────────┐
│  High-Level API (User-facing)                                   │
│  RocketWelderClient, Schema, DataContext                        │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ uses internally
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  Protocol Layer (Internal)                                      │
│  KeyPointsSink, KeyPointsWriter                                 │
│  SegmentationResultSink, SegmentationResultWriter               │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ uses internally
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  Transport Layer (Internal)                                     │
│  IFrameSink, IFrameSource                                       │
│  NngFrameSink, TcpFrameSink, WebSocketFrameSink, etc.           │
└─────────────────────────────────────────────────────────────────┘
```

### DataContext Implementation (Internal)

```csharp
internal class KeyPointsDataContext : IKeyPointsDataContext
{
    private readonly IKeyPointsWriter _writer;

    public ulong FrameId { get; }

    public void Add(KeyPoint point, int x, int y, float confidence)
    {
        _writer.Append(point.Id, x, y, confidence);
    }

    internal void Commit()
    {
        _writer.Dispose();  // Flushes to sink
    }
}
```

### Processing Loop (Internal)

```csharp
internal async Task RunProcessingLoopAsync(
    Action<Mat, ISegmentationDataContext, IKeyPointsDataContext, Mat> processFrame,
    CancellationToken ct)
{
    ulong frameId = 0;

    while (!ct.IsCancellationRequested)
    {
        using var inputFrame = _videoSource.Read();
        if (inputFrame.Empty()) break;

        using var outputFrame = new Mat();

        // Create UoW contexts for this frame
        var keypointsContext = new KeyPointsDataContext(
            _keypointsSink.CreateWriter(frameId), frameId);
        var segmentationContext = new SegmentationDataContext(
            _segmentationSink.CreateWriter(frameId, (uint)inputFrame.Width, (uint)inputFrame.Height),
            frameId, (uint)inputFrame.Width, (uint)inputFrame.Height);

        try
        {
            // User processes frame
            processFrame(inputFrame, segmentationContext, keypointsContext, outputFrame);

            // Auto-commit both contexts
            keypointsContext.Commit();
            segmentationContext.Commit();
        }
        catch
        {
            // Rollback: dispose without commit (if supported)
            throw;
        }

        // Send output frame downstream (if configured)
        _outputSink?.Write(outputFrame);

        frameId++;
    }
}
```

---

## File Structure

```
csharp/RocketWelder.SDK/
├── HighLevel/
│   ├── KeyPoint.cs                      # readonly record struct
│   ├── SegmentClass.cs                  # readonly record struct
│   ├── IKeyPointsSchema.cs              # Schema interface
│   ├── ISegmentationSchema.cs           # Schema interface
│   ├── IKeyPointsDataContext.cs         # Data context interface
│   ├── ISegmentationDataContext.cs      # Data context interface
│   ├── IRocketWelderClient.cs           # Client interface
│   ├── RocketWelderClient.cs            # Client implementation + factory
│   ├── RocketWelderClientOptions.cs     # Configuration options
│   └── Internal/
│       ├── KeyPointsSchema.cs           # Schema implementation
│       ├── SegmentationSchema.cs        # Schema implementation
│       ├── KeyPointsDataContext.cs      # UoW implementation
│       └── SegmentationDataContext.cs   # UoW implementation
├── KeyPointsProtocol.cs                 # Low-level (existing)
├── RocketWelderClient.cs                # Low-level (existing, to be refactored)
└── Transport/                           # Low-level (existing)
```

---

**Last Updated:** 2025-12-04
**Status:** Design Document - Ready for Implementation
